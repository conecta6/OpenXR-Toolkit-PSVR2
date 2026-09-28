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
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using GameManager.Core;

namespace GameManager
{
    /// <summary>
    /// Read-only list of installed Steam games. Built in code: no designer file, no resources.
    /// </summary>
    public sealed class MainForm : Form
    {
        private readonly SteamLocator locator;
        private readonly Button refreshButton;
        private readonly Label statusLabel;
        private readonly ListView gameList;
        private readonly TextBox warningsBox;

        public MainForm(SteamLocator locator)
        {
            this.locator = locator ?? throw new ArgumentNullException(nameof(locator));

            Text = "Game Manager - OpenXR Toolkit PSVR2";
            Size = new Size(1200, 700);
            MinimumSize = new Size(700, 400);
            StartPosition = FormStartPosition.CenterScreen;

            refreshButton = new Button { Text = "Refresh", AutoSize = true };
            refreshButton.Click += OnRefreshClick;

            statusLabel = new Label { AutoSize = true, Margin = new Padding(8, 8, 3, 0) };

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
            };
            gameList.Columns.Add("Name", 220);
            gameList.Columns.Add("AppID", 80);
            gameList.Columns.Add("Type", 120);
            gameList.Columns.Add("openvr_api.dll", 420);
            gameList.Columns.Add("Folder", 320);

            var warningsLabel = new Label { Text = "Warnings", AutoSize = true, Margin = new Padding(3, 6, 3, 0) };

            warningsBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
            };

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 75));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            layout.Controls.Add(topBar, 0, 0);
            layout.Controls.Add(gameList, 0, 1);
            layout.Controls.Add(warningsLabel, 0, 2);
            layout.Controls.Add(warningsBox, 0, 3);
            Controls.Add(layout);

            Shown += OnShown;
        }

        private async void OnShown(object sender, EventArgs e)
        {
            await ScanAsync();
        }

        private async void OnRefreshClick(object sender, EventArgs e)
        {
            await ScanAsync();
        }

        private async Task ScanAsync()
        {
            refreshButton.Enabled = false;
            statusLabel.Text = "Scanning…";
            gameList.Items.Clear();
            warningsBox.Clear();

            // Created on the UI thread, so its callback runs on the UI thread.
            var progress = new Progress<string>(name => statusLabel.Text = "Scanning… " + name);
            try
            {
                GameListResult result = await Task.Run(() => GameListBuilder.Build(locator, progress));
                ShowResult(result);
            }
            catch (Exception ex)
            {
                // async void handlers must not let exceptions escape: that would close the app.
                statusLabel.Text = "Scan failed. Details below.";
                warningsBox.Text = ex.ToString();
            }
            finally
            {
                refreshButton.Enabled = true;
            }
        }

        private void ShowResult(GameListResult result)
        {
            if (result.SteamRoot == null)
            {
                statusLabel.Text = "Steam was not found on this PC. Install Steam and start it once, then click Refresh.";
                return;
            }

            gameList.BeginUpdate();
            try
            {
                foreach (GameEntry entry in result.Entries)
                {
                    var item = new ListViewItem(entry.Game.Name);
                    item.SubItems.Add(entry.Game.AppId.ToString(CultureInfo.InvariantCulture));
                    item.SubItems.Add(DisplayText.Kind(entry.Classification.Kind));
                    item.SubItems.Add(DisplayText.OpenVrDlls(entry.Classification.OpenVrDlls));
                    item.SubItems.Add(entry.Game.InstallDir);
                    gameList.Items.Add(item);
                }
            }
            finally
            {
                gameList.EndUpdate();
            }

            warningsBox.Text = string.Join(Environment.NewLine, result.Warnings);
            statusLabel.Text = result.Entries.Count + " games found in " + result.SteamRoot + ". "
                + result.Warnings.Count + " warnings.";
        }
    }
}
