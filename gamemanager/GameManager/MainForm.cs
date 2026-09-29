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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GameManager.Core;

namespace GameManager
{
    /// <summary>
    /// List of installed Steam games with their type, compatibility and patch status, plus the OpenComposite row and
    /// the patch row. Built in code: no designer file, no resources.
    /// </summary>
    public sealed class MainForm : Form
    {
        private const int StatusColumn = 4;

        private readonly SteamLocator locator;
        private readonly string compatibilityPath;
        private readonly PatchPlanner planner;
        private readonly Button refreshButton;
        private readonly Label statusLabel;
        private readonly ListView gameList;
        private readonly TextBox warningsBox;
        private readonly OpenCompositeBar openCompositeBar;
        private readonly PatchBar patchBar;

        // F4/R35: warnings from operations live in their own de-duplicated log and are re-appended after every scan,
        // so Refresh never erases them and repeats never pile up.
        private readonly WarningLog operationWarnings = new WarningLog();

        // R33: one operation at a time across every toolbar row.
        private readonly OperationGate gate = new OperationGate();

        private IReadOnlyList<string> lastScanWarnings = new string[0];

        // P3: warnings about patch statuses (a missing DLL, a changed one, a bad cache) describe the disk right now.
        // They are replaced on every status read, never accumulated in operationWarnings.
        private IReadOnlyList<string> statusWarnings = new string[0];
        private IReadOnlyList<GameEntry> lastEntries = new GameEntry[0];
        private IReadOnlyList<GamePatchStatus> lastStatuses = new GamePatchStatus[0];
        private string scanSummary = "";

        // False for the "Steam was not found" line, which has no warning count.
        private bool summaryHasWarningCount;
        private CancellationTokenSource scanCancellation;
        private bool statusRefreshWaitsForScan;

        // Set once a scan has put a list on screen; a queued status refresh has nothing to refresh without one.
        private bool scanProducedList;

        // The newest status read wins: an older one that finishes late must not overwrite it.
        private int statusSequence;

        // Counts notices, so a status read knows whether one appeared while it was reading and must not be covered.
        private int noticeSequence;

        // True while the status line shows a notice or an error instead of the summary.
        private bool noticeShowing;

        public MainForm(SteamLocator locator, string compatibilityPath, OpenCompositeCache openComposite, PatchPlanner planner, PatchService patchService)
        {
            this.locator = locator ?? throw new ArgumentNullException(nameof(locator));
            this.compatibilityPath = compatibilityPath ?? throw new ArgumentNullException(nameof(compatibilityPath));
            this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
            if (openComposite == null)
            {
                throw new ArgumentNullException(nameof(openComposite));
            }
            if (patchService == null)
            {
                throw new ArgumentNullException(nameof(patchService));
            }

            Text = "Game Manager - OpenXR Toolkit PSVR2";
            Size = new Size(1280, 720);
            MinimumSize = new Size(760, 420);
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
                MultiSelect = false,
                ShowItemToolTips = true,
            };
            gameList.Columns.Add("Name", 220);
            gameList.Columns.Add("AppID", 70);
            gameList.Columns.Add("Type", 110);
            gameList.Columns.Add("Compatibility", 150);
            gameList.Columns.Add("Status", 130);
            gameList.Columns.Add("openvr_api.dll", 330);
            gameList.Columns.Add("Folder", 280);
            gameList.SelectedIndexChanged += OnSelectionChanged;

            var warningsLabel = new Label { Text = "Warnings", AutoSize = true, Margin = new Padding(3, 6, 3, 0) };

            warningsBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
            };

            openCompositeBar = new OpenCompositeBar(openComposite, gate, AddWarning, ShowNotice);
            patchBar = new PatchBar(planner, patchService, gate, AddWarning, ShowNotice);
            patchBar.StatusesChanged += OnStatusesChanged;
            patchBar.RelaunchStarted += OnRelaunchStarted;
            openCompositeBar.CacheChanged += OnStatusesChanged;
            gate.Changed += OnGateChanged;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 75));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            layout.Controls.Add(topBar, 0, 0);
            layout.Controls.Add(openCompositeBar, 0, 1);
            layout.Controls.Add(patchBar, 0, 2);
            layout.Controls.Add(gameList, 0, 3);
            layout.Controls.Add(warningsLabel, 0, 4);
            layout.Controls.Add(warningsBox, 0, 5);
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
            // At most once per 24 hours, and only when OpenComposite was downloaded before. After the scan, so its
            // warnings sit below the scan's own.
            await openCompositeBar.StartupCheckAsync();
        }

        private async void OnRefreshClick(object sender, EventArgs e)
        {
            await ScanAsync();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            // P1: a Patch or Restore is planning or copying files. Closing now would cut the executor off in the middle
            // of a plan, so the window stays until it finishes.
            if (patchBar.IsOperationRunning)
            {
                e.Cancel = true;
                ShowNotice("A patch, restore or update is running. Wait until it finishes, then close the window.");
                return;
            }
            // R5: stop a running scan between two games instead of letting it walk the rest of the library.
            scanCancellation?.Cancel();
            openCompositeBar.CancelPendingWork();
            gate.Changed -= OnGateChanged;
        }

        private void OnGateChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }
            if (InvokeRequired)
            {
                // The gate can change on any thread that releases a lease.
                if (IsHandleCreated)
                {
                    try
                    {
                        BeginInvoke(new Action(() => OnGateChanged(sender, e)));
                    }
                    catch (InvalidOperationException)
                    {
                        // The window closed between the check and the call.
                    }
                }
                return;
            }
            RefreshRefreshButton();
        }

        /// <summary>
        /// Refresh reads state.json and the game folders; it stays off while an operation writes them (R33) and while
        /// a scan runs.
        /// </summary>
        private void RefreshRefreshButton()
        {
            refreshButton.Enabled = scanCancellation == null && !gate.IsBusy;
        }

        private void OnRelaunchStarted(object sender, EventArgs e)
        {
            // R22: an elevated copy is starting; this one closes so the two never write at the same time.
            Close();
        }

        private void AddWarning(string line)
        {
            if (IsDisposed)
            {
                return;
            }
            if (operationWarnings.Add(line))
            {
                RenderWarnings();
                if (!noticeShowing && scanCancellation == null && scanProducedList)
                {
                    // Keeps the warning count on the status line current.
                    ShowStatusLine();
                }
            }
        }

        /// <summary>
        /// R35: transient notices ("another operation is running") go to the status line, never to the warnings.
        /// </summary>
        private void ShowNotice(string text)
        {
            if (!IsDisposed)
            {
                noticeSequence++;
                noticeShowing = true;
                statusLabel.Text = text;
            }
        }

        private async Task ScanAsync()
        {
            var cancellation = new CancellationTokenSource();
            scanCancellation = cancellation;
            scanProducedList = false;
            statusSequence++;
            var listWarnings = new List<string>();
            var scanStatusWarnings = new List<string>();
            try
            {
                RefreshRefreshButton();
                statusLabel.Text = "Scanning…";
                gameList.Items.Clear();
                lastEntries = new GameEntry[0];
                lastStatuses = new GamePatchStatus[0];
                patchBar.SetSelection(null, null);
                patchBar.SetStatuses(null);
                // F4: only the scan's own warnings and the status warnings are cleared here; operationWarnings survives
                // and is re-appended.
                lastScanWarnings = new string[0];
                statusWarnings = new string[0];
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
                var (result, statuses) = await Task.Run(() =>
                {
                    // Read on every scan, so an edited compatibility.json takes effect on Refresh.
                    CompatibilityList compatibility = CompatibilityList.Load(compatibilityPath, listWarnings);
                    GameListResult scanned = GameListBuilder.Build(locator, compatibility, progress, token);
                    // R24: right after the scan, every patch record is checked against the file on disk.
                    IReadOnlyList<GamePatchStatus> read = planner.GetStatuses(scanned.Entries, scanStatusWarnings);
                    return (scanned, read);
                });
                ShowResult(result, statuses, listWarnings, scanStatusWarnings);
                scanProducedList = true;
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
                    noticeShowing = true;
                    statusLabel.Text = "Scan failed. Details below.";
                    lastScanWarnings = new[] { ex.ToString() };
                    RenderWarnings();
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
                    RefreshRefreshButton();
                }
            }
            if (statusRefreshWaitsForScan && !IsDisposed)
            {
                statusRefreshWaitsForScan = false;
                // A patch or restore finished while this scan was reading the disk, so its statuses may be stale. A
                // scan that produced no list (failed or cancelled) has nothing to refresh.
                if (scanProducedList)
                {
                    await RefreshStatusesAsync();
                }
            }
        }

        private void RenderWarnings()
        {
            warningsBox.Text = string.Join(Environment.NewLine, CurrentWarnings());
        }

        private List<string> CurrentWarnings()
        {
            var warnings = new List<string>(lastScanWarnings);
            warnings.AddRange(statusWarnings);
            warnings.AddRange(operationWarnings.Items);
            // R35: a line reported both by the scan and by an operation is shown once.
            return warnings.Distinct().ToList();
        }

        /// <summary>
        /// The status line: what the scan found, the number of warnings now on screen, and the offer to re-patch or
        /// update. Recomputed from the current lists every time, never kept as text.
        /// </summary>
        private void ShowStatusLine()
        {
            var text = new StringBuilder(scanSummary);
            if (summaryHasWarningCount)
            {
                text.Append(' ').Append(CurrentWarnings().Count).Append(" warnings.");
            }
            // R24: after a scan (and after any operation), offer Re-patch all / Update all when there is something to do.
            string offer = DisplayText.PostUpdateSummary(lastStatuses);
            if (offer.Length > 0)
            {
                text.Append(' ').Append(offer);
            }
            noticeShowing = false;
            statusLabel.Text = text.ToString();
        }

        private void ShowResult(GameListResult result, IReadOnlyList<GamePatchStatus> statuses, IReadOnlyList<string> listWarnings, IReadOnlyList<string> newStatusWarnings)
        {
            if (IsDisposed)
            {
                return;
            }

            var warnings = new List<string>(listWarnings);
            warnings.AddRange(result.Warnings);
            lastScanWarnings = warnings;
            statusWarnings = newStatusWarnings;
            RenderWarnings();

            if (result.SteamRoot == null)
            {
                summaryHasWarningCount = false;
                scanSummary = "Steam was not found on this PC. Install Steam and start it once, then click Refresh.";
                ApplyStatuses(result.Entries, statuses);
                return;
            }

            int blocked = 0;
            gameList.BeginUpdate();
            try
            {
                for (int i = 0; i < result.Entries.Count; i++)
                {
                    GameEntry entry = result.Entries[i];
                    var item = new ListViewItem(entry.Game.Name) { Tag = i };
                    item.SubItems.Add(entry.Game.AppId.ToString(CultureInfo.InvariantCulture));
                    item.SubItems.Add(DisplayText.Kind(entry.Classification.Kind));
                    item.SubItems.Add(DisplayText.Compatibility(entry.Compatibility));
                    item.SubItems.Add("");
                    item.SubItems.Add(DisplayText.OpenVrDlls(entry.Classification.OpenVrDlls));
                    item.SubItems.Add(entry.Game.InstallDir);
                    if (entry.Compatibility.Blocked)
                    {
                        // R9: greyed out. Anti-cheat games never get Patch.
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

            summaryHasWarningCount = true;
            scanSummary = result.Entries.Count + " games found in " + result.SteamRoot + ". "
                + blocked + " blocked (anti-cheat).";
            ApplyStatuses(result.Entries, statuses);
        }

        /// <summary>
        /// Shows statuses in the Status column and the tooltips, refreshes the status line and hands the selected
        /// game to the patch row. Used after a scan and after an operation changed files.
        /// </summary>
        private void ApplyStatuses(IReadOnlyList<GameEntry> entries, IReadOnlyList<GamePatchStatus> statuses, bool keepNotice = false)
        {
            lastEntries = entries;
            lastStatuses = statuses;
            foreach (ListViewItem item in gameList.Items)
            {
                int index = (int)item.Tag;
                item.SubItems[StatusColumn].Text = DisplayText.StatusText(statuses[index].Status);
                item.ToolTipText = Tooltip(entries[index], statuses[index]);
            }
            patchBar.SetStatuses(statuses);
            if (!keepNotice)
            {
                // A notice shown while the statuses were being read stays on the status line.
                ShowStatusLine();
            }
            OnSelectionChanged(this, EventArgs.Empty);
        }

        private static string Tooltip(GameEntry entry, GamePatchStatus status)
        {
            var parts = new List<string>();
            string compatibility = DisplayText.CompatibilityDetails(entry.Compatibility);
            if (compatibility.Length > 0)
            {
                parts.Add(compatibility);
            }
            parts.AddRange(status.Details);
            return string.Join(Environment.NewLine, parts);
        }

        private void OnSelectionChanged(object sender, EventArgs e)
        {
            if (gameList.SelectedItems.Count == 1)
            {
                int index = (int)gameList.SelectedItems[0].Tag;
                if (index < lastEntries.Count && index < lastStatuses.Count)
                {
                    patchBar.SetSelection(lastEntries[index], lastStatuses[index]);
                    return;
                }
            }
            patchBar.SetSelection(null, null);
        }

        private async void OnStatusesChanged(object sender, EventArgs e)
        {
            await RefreshStatusesAsync();
        }

        /// <summary>
        /// Reads the patch statuses again after an operation. P3: does nothing while a scan runs (the scan owns the
        /// list and reads the disk itself; it asks for one more read when it ends, in case it read too early).
        /// </summary>
        private async Task RefreshStatusesAsync()
        {
            if (IsDisposed)
            {
                return;
            }
            if (scanCancellation != null)
            {
                statusRefreshWaitsForScan = true;
                return;
            }
            IReadOnlyList<GameEntry> entries = lastEntries;
            var warnings = new List<string>();
            int mine = ++statusSequence;
            int noticesAtStart = noticeSequence;
            try
            {
                IReadOnlyList<GamePatchStatus> statuses = await Task.Run(() => planner.GetStatuses(entries, warnings));
                // A scan that started meanwhile owns the list now, and a newer status read supersedes this one.
                if (IsDisposed || scanCancellation != null || mine != statusSequence || !ReferenceEquals(entries, lastEntries))
                {
                    return;
                }
                // Replaces the previous status warnings instead of adding to them (before the line counts them).
                statusWarnings = warnings;
                RenderWarnings();
                ApplyStatuses(entries, statuses, noticesAtStart != noticeSequence);
            }
            catch (Exception ex)
            {
                // async void handlers must not let exceptions escape: that would close the app. The message replaces
                // the previous status warnings: it describes the disk right now.
                if (!IsDisposed && mine == statusSequence)
                {
                    statusWarnings = new[] { "Could not read the patch status: " + ex.Message };
                    RenderWarnings();
                }
            }
        }
    }
}
