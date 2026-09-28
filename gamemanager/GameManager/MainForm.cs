// MIT License
//
// Copyright(c) 2026 OpenXR-Toolkit-PSVR2 contributors
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this softwareand associated documentation files(the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and /or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions :
//
// The above copyright noticeand this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GameManager.Core;

namespace GameManager
{
    /// <summary>
    /// List of installed Steam games with their type and compatibility, plus the OpenComposite cache controls.
    /// Built in code: no designer file, no resources.
    /// </summary>
    public sealed class MainForm : Form
    {
        private readonly SteamLocator locator;
        private readonly string compatibilityPath;
        private readonly Button refreshButton;
        private readonly Label statusLabel;
        private readonly ListView gameList;
        private readonly TextBox warningsBox;
        private readonly OpenCompositeBar openCompositeBar;

        // F4: OpenComposite warnings live in their own list and are re-appended after every scan, so Refresh
        // (which rebuilds warningsBox.Text from the scan's own warnings) never erases them.
        private readonly List<string> openCompositeWarnings = new List<string>();
        private IReadOnlyList<string> lastScanWarnings = new string[0];
        private CancellationTokenSource scanCancellation;

        public MainForm(SteamLocator locator, string compatibilityPath, OpenCompositeCache openComposite)
        {
            this.locator = locator ?? throw new ArgumentNullException(nameof(locator));
            this.compatibilityPath = compatibilityPath ?? throw new ArgumentNullException(nameof(compatibilityPath));
            if (openComposite == null)
            {
                throw new ArgumentNullException(nameof(openComposite));
            }

            Text = "Game Manager - OpenXR Toolkit PSVR2";
            Size = new Size(1200, 700);
            MinimumSize = new Size(700, 400);
            StartPosition = FormStartPosition.CenterScreen;

            refreshButton = new Button { Text = "Refresh", AutoSize = true };
            refreshButton.Click += OnRefreshClick;

            statusLabel = new Label { AutoSize = true, Margin = new Padding(8, 8, 3, 0), UseMnemonic = false };

            var topBar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
            topBar.Controls.Add(refreshButton);
            topBar.Controls.Add(statusLabel);

            gameList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                ShowItemToolTips = true,
            };
            gameList.Columns.Add("Name", 220);
            gameList.Columns.Add("AppID", 80);
            gameList.Columns.Add("Type", 120);
            gameList.Columns.Add("Compatibility", 150);
            gameList.Columns.Add("openvr_api.dll", 380);
            gameList.Columns.Add("Folder", 300);

            var warningsLabel = new Label { Text = "Warnings", AutoSize = true, Margin = new Padding(3, 6, 3, 0) };

            warningsBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
            };

            openCompositeBar = new OpenCompositeBar(openComposite, AddWarning);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 75));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            layout.Controls.Add(topBar, 0, 0);
            layout.Controls.Add(openCompositeBar, 0, 1);
            layout.Controls.Add(gameList, 0, 2);
            layout.Controls.Add(warningsLabel, 0, 3);
            layout.Controls.Add(warningsBox, 0, 4);
            Controls.Add(layout);

            Shown += OnShown;
            FormClosing += OnFormClosing;
        }

        private async void OnShown(object sender, EventArgs e)
        {
            await ScanAsync();
            if (IsDisposed)
            {
                return;
            }
            // After the scan, so the scan does not clear its warnings. At most once per 24 hours, and only when
            // OpenComposite was downloaded before.
            await openCompositeBar.StartupCheckAsync();
        }

        private async void OnRefreshClick(object sender, EventArgs e)
        {
            await ScanAsync();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            // R5: stop a running scan between two games instead of letting it walk the rest of the library.
            scanCancellation?.Cancel();
            openCompositeBar.CancelPendingWork();
        }

        private void AddWarning(string line)
        {
            if (IsDisposed)
            {
                return;
            }
            openCompositeWarnings.Add(line);
            RenderWarnings();
        }

        private async Task ScanAsync()
        {
            var cancellation = new CancellationTokenSource();
            scanCancellation = cancellation;
            var listWarnings = new List<string>();
            try
            {
                refreshButton.Enabled = false;
                statusLabel.Text = "Scanning…";
                gameList.Items.Clear();
                // F4: only the scan's own warnings are cleared here; openCompositeWarnings survives and is
                // re-appended by RenderWarnings, both here and in ShowResult below.
                lastScanWarnings = new string[0];
                RenderWarnings();

                // Created on the UI thread, so its callback runs on the UI thread.
                var progress = new Progress<string>(name =>
                {
                    if (!IsDisposed)
                    {
                        statusLabel.Text = "Scanning… " + name;
                    }
                });
                CancellationToken token = cancellation.Token;
                GameListResult result = await Task.Run(() =>
                {
                    // Read on every scan, so an edited compatibility.json takes effect on Refresh.
                    CompatibilityList compatibility = CompatibilityList.Load(compatibilityPath, listWarnings);
                    return GameListBuilder.Build(locator, compatibility, progress, token);
                });
                ShowResult(result, listWarnings);
            }
            catch (OperationCanceledException)
            {
                // The window is closing: there is nothing left to show.
            }
            catch (Exception ex)
            {
                // async void handlers must not let exceptions escape: that would close the app.
                if (!IsDisposed)
                {
                    statusLabel.Text = "Scan failed. Details below.";
                    warningsBox.Text = ex.ToString();
                }
            }
            finally
            {
                if (scanCancellation == cancellation)
                {
                    scanCancellation = null;
                }
                cancellation.Dispose();
                if (!IsDisposed)
                {
                    refreshButton.Enabled = true;
                }
            }
        }

        private void RenderWarnings()
        {
            var warnings = new List<string>(lastScanWarnings);
            warnings.AddRange(openCompositeWarnings);
            warningsBox.Text = string.Join(Environment.NewLine, warnings);
        }

        private void ShowResult(GameListResult result, IReadOnlyList<string> listWarnings)
        {
            if (IsDisposed)
            {
                return;
            }

            var warnings = new List<string>(listWarnings);
            warnings.AddRange(result.Warnings);
            lastScanWarnings = warnings;
            RenderWarnings();

            if (result.SteamRoot == null)
            {
                statusLabel.Text = "Steam was not found on this PC. Install Steam and start it once, then click Refresh.";
                return;
            }

            int blocked = 0;
            gameList.BeginUpdate();
            try
            {
                foreach (GameEntry entry in result.Entries)
                {
                    var item = new ListViewItem(entry.Game.Name);
                    item.SubItems.Add(entry.Game.AppId.ToString(CultureInfo.InvariantCulture));
                    item.SubItems.Add(DisplayText.Kind(entry.Classification.Kind));
                    item.SubItems.Add(DisplayText.Compatibility(entry.Compatibility));
                    item.SubItems.Add(DisplayText.OpenVrDlls(entry.Classification.OpenVrDlls));
                    item.SubItems.Add(entry.Game.InstallDir);
                    item.ToolTipText = DisplayText.CompatibilityDetails(entry.Compatibility);
                    if (entry.Compatibility.Blocked)
                    {
                        // R9: greyed out. Anti-cheat games will never get patch actions.
                        item.ForeColor = SystemColors.GrayText;
                        blocked++;
                    }
                    gameList.Items.Add(item);
                }
            }
            finally
            {
                gameList.EndUpdate();
            }

            statusLabel.Text = result.Entries.Count + " games found in " + result.SteamRoot + ". "
                + blocked + " blocked (anti-cheat). " + warnings.Count + " warnings.";
        }
    }
}
