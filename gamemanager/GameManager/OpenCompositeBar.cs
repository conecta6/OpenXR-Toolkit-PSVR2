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
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GameManager.Core;

namespace GameManager
{
    /// <summary>
    /// Second row of the window: the OpenComposite cache (phase 4). All work goes through OpenCompositeCache;
    /// this class only wires buttons, the license dialog and messages. It shares one OperationGate with the patch
    /// controls (R33), so the cached DLL never changes while a patch is planning or copying it.
    /// </summary>
    public sealed class OpenCompositeBar : FlowLayoutPanel
    {
        private const string OperationName = "OpenComposite download, check or accept";

        private readonly OpenCompositeCache cache;
        private readonly OperationGate gate;
        private readonly Action<string> addWarning;
        private readonly Action<Func<string, bool>> removeWarnings;
        private readonly Action<string> showNotice;
        private readonly Button downloadButton;
        private readonly Button checkButton;
        private readonly Button acceptButton;
        private readonly Label summaryLabel;
        private readonly CancellationTokenSource closing = new CancellationTokenSource();
        private int cachedBuildCount;
        private bool hasPendingUpdate;

        public OpenCompositeBar(OpenCompositeCache cache, OperationGate gate, Action<string> addWarning, Action<Func<string, bool>> removeWarnings, Action<string> showNotice)
        {
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
            this.gate = gate ?? throw new ArgumentNullException(nameof(gate));
            this.addWarning = addWarning ?? throw new ArgumentNullException(nameof(addWarning));
            this.removeWarnings = removeWarnings ?? throw new ArgumentNullException(nameof(removeWarnings));
            this.showNotice = showNotice ?? throw new ArgumentNullException(nameof(showNotice));

            Dock = DockStyle.Fill;
            AutoSize = true;
            WrapContents = false;

            downloadButton = new Button { Text = "Download OpenComposite", AutoSize = true };
            downloadButton.Click += OnDownloadClick;
            checkButton = new Button { Text = "Check for OpenComposite update", AutoSize = true };
            checkButton.Click += OnCheckClick;
            acceptButton = new Button { Text = "Use new OpenComposite build", AutoSize = true, Visible = false };
            acceptButton.Click += OnAcceptClick;
            summaryLabel = new Label { AutoSize = true, Margin = new Padding(8, 8, 3, 0), UseMnemonic = false };

            Controls.Add(downloadButton);
            Controls.Add(checkButton);
            Controls.Add(acceptButton);
            Controls.Add(summaryLabel);
            gate.Changed += OnGateChanged;
            RefreshState(new List<string>());
        }

        /// <summary>
        /// Raised after a download or an accepted update changed the cached DLLs, so "Update available" is read again.
        /// </summary>
        public event EventHandler CacheChanged;

        /// <summary>
        /// Stops a running download or check. Called when the window closes.
        /// </summary>
        public void CancelPendingWork()
        {
            closing.Cancel();
        }

        /// <summary>
        /// R14 at startup: checks upstream at most once per 24 hours, and only when a build is already cached
        /// (so the first start never touches the network). Silent except for warnings and the accept button;
        /// skipped when another operation already holds the gate (it runs again at the next start).
        /// </summary>
        public Task StartupCheckAsync()
        {
            return RunAsync((warnings, token) => Task.Run(() => cache.CheckForUpdatesIfDueAsync(warnings, token)), false);
        }

        private async void OnDownloadClick(object sender, EventArgs e)
        {
            if (!EnsureNotBusy() || !EnsureLicenseAccepted())
            {
                return;
            }
            await RunAsync(async (warnings, token) =>
            {
                var outcomes = new List<DownloadOutcome>();
                foreach (OpenCompositeArch arch in OpenCompositeCache.AllArchitectures)
                {
                    outcomes.Add(await Task.Run(() => cache.DownloadAsync(arch, warnings, token)));
                }
                return outcomes;
            }, true);
        }

        private async void OnCheckClick(object sender, EventArgs e)
        {
            // R35: with the license not accepted (never, or settings.json was lost) the check would do nothing;
            // the notice is shown instead of failing silently.
            if (!EnsureNotBusy() || !EnsureLicenseAccepted())
            {
                return;
            }
            await RunAsync((warnings, token) => Task.Run(() => cache.CheckForUpdatesAsync(warnings, token)), true);
        }

        private async void OnAcceptClick(object sender, EventArgs e)
        {
            await RunAsync((warnings, token) => Task.Run(() =>
            {
                var outcomes = new List<DownloadOutcome>();
                foreach (CachedBuild build in cache.GetBuilds(new List<string>()))
                {
                    if (build.HasPendingUpdate)
                    {
                        outcomes.Add(cache.AcceptPendingUpdate(build.Arch, warnings));
                    }
                }
                return (IReadOnlyList<DownloadOutcome>)outcomes;
            }), true);
        }

        /// <summary>
        /// R33/R35: false, with a status-line notice (not a warning), when another operation is running.
        /// </summary>
        private bool EnsureNotBusy()
        {
            if (!gate.IsBusy)
            {
                return true;
            }
            ShowBusyNotice();
            return false;
        }

        private void ShowBusyNotice()
        {
            showNotice("Another operation is running (" + gate.CurrentOperation + "). Try again when it finishes.");
        }

        /// <summary>
        /// R13: shows the notice when it was never accepted. False when the user declines or the choice cannot be saved.
        /// </summary>
        private bool EnsureLicenseAccepted()
        {
            var warnings = new List<string>();
            if (cache.IsLicenseAccepted(warnings))
            {
                return true;
            }
            foreach (string warning in warnings)
            {
                addWarning(warning);
            }
            using (var notice = new LicenseNoticeForm())
            {
                if (notice.ShowDialog(FindForm()) != DialogResult.OK)
                {
                    return false;
                }
            }
            try
            {
                cache.AcceptLicense();
                // The "license notice has not been accepted" warnings from earlier are no longer true.
                removeWarnings(OpenCompositeCache.IsLicenseNotAcceptedMessage);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                addWarning("Could not save the license acceptance: " + ex.Message);
                return false;
            }
        }

        private async Task RunAsync(Func<List<string>, CancellationToken, Task<IReadOnlyList<DownloadOutcome>>> work, bool showResult)
        {
            if (IsDisposed)
            {
                return;
            }
            // R33: the gate is taken here, after the license dialog (a nested message loop) may have let another
            // operation start; then a user-triggered call gets a notice and the silent startup check skips.
            IDisposable lease = gate.TryEnter(OperationName);
            if (lease == null)
            {
                if (showResult)
                {
                    ShowBusyNotice();
                }
                return;
            }
            var warnings = new List<string>();
            IReadOnlyList<DownloadOutcome> outcomes = new DownloadOutcome[0];
            try
            {
                outcomes = await work(warnings, closing.Token);
            }
            catch (OperationCanceledException) when (closing.IsCancellationRequested)
            {
                // The window is closing.
                return;
            }
            catch (Exception ex)
            {
                // async void callers must never let an exception escape: that would close the app.
                warnings.Add("OpenComposite: " + ex.Message);
            }
            finally
            {
                lease.Dispose();
            }
            if (IsDisposed)
            {
                return;
            }

            RefreshState(warnings);
            if (outcomes.Any(o => o.Status == DownloadStatus.Downloaded || o.Status == DownloadStatus.UpdateAccepted))
            {
                CacheChanged?.Invoke(this, EventArgs.Empty);
            }
            foreach (DownloadOutcome outcome in outcomes)
            {
                if (outcome.Status == DownloadStatus.Failed || outcome.Status == DownloadStatus.LicenseNotAccepted)
                {
                    warnings.Add(outcome.Message);
                }
            }
            foreach (string warning in warnings.Distinct())
            {
                addWarning(warning);
            }
            if (showResult)
            {
                // R35: a user-triggered operation always answers, even when there was nothing to do.
                bool failed = outcomes.Count == 0
                    || outcomes.Any(o => o.Status == DownloadStatus.Failed || o.Status == DownloadStatus.LicenseNotAccepted);
                string text = outcomes.Count > 0
                    ? string.Join(Environment.NewLine, outcomes.Select(o => o.Message))
                    : "Nothing was done: OpenComposite has not been downloaded yet, or no new build is waiting.";
                MessageBox.Show(FindForm(), text, "OpenComposite", MessageBoxButtons.OK, failed ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
        }

        private void RefreshState(IList<string> warnings)
        {
            IReadOnlyList<CachedBuild> builds = cache.GetBuilds(warnings);
            summaryLabel.Text = DisplayText.OpenCompositeSummary(builds);
            cachedBuildCount = builds.Count;
            hasPendingUpdate = builds.Any(b => b.HasPendingUpdate);
            RefreshButtons();
        }

        private void RefreshButtons()
        {
            bool free = !gate.IsBusy;
            downloadButton.Enabled = free;
            checkButton.Enabled = free && cachedBuildCount > 0;
            acceptButton.Visible = hasPendingUpdate;
            acceptButton.Enabled = free;
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
            RefreshButtons();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                gate.Changed -= OnGateChanged;
                closing.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
