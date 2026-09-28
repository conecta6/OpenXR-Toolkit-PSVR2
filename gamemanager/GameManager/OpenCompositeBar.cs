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
    /// this class only wires buttons, the license dialog and messages.
    /// </summary>
    public sealed class OpenCompositeBar : FlowLayoutPanel
    {
        private readonly OpenCompositeCache cache;
        private readonly Action<string> addWarning;
        private readonly Button downloadButton;
        private readonly Button checkButton;
        private readonly Button acceptButton;
        private readonly Label summaryLabel;
        private readonly CancellationTokenSource closing = new CancellationTokenSource();
        private bool busy;

        public OpenCompositeBar(OpenCompositeCache cache, Action<string> addWarning)
        {
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
            this.addWarning = addWarning ?? throw new ArgumentNullException(nameof(addWarning));

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
            RefreshState(new List<string>());
        }

        /// <summary>
        /// Stops a running download or check. Called when the window closes.
        /// </summary>
        public void CancelPendingWork()
        {
            closing.Cancel();
        }

        /// <summary>
        /// R14 at startup: checks upstream at most once per 24 hours, and only when a build is already cached
        /// (so the first start never touches the network). Silent except for warnings and the accept button.
        /// </summary>
        public Task StartupCheckAsync()
        {
            return RunAsync((warnings, token) => Task.Run(() => cache.CheckForUpdatesIfDueAsync(warnings, token)), false);
        }

        private async void OnDownloadClick(object sender, EventArgs e)
        {
            if (busy || !EnsureLicenseAccepted())
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
            if (busy)
            {
                // Can happen when the license dialog was shown (a nested message loop) and another operation,
                // such as the startup check, started while it was up. Only user-triggered callers (showResult)
                // need telling; a silent StartupCheckAsync colliding with itself cannot happen.
                if (showResult)
                {
                    addWarning("Another OpenComposite operation is running; try again when it finishes.");
                }
                return;
            }
            busy = true;
            downloadButton.Enabled = false;
            checkButton.Enabled = false;
            acceptButton.Enabled = false;
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
                busy = false;
            }
            if (IsDisposed)
            {
                return;
            }

            RefreshState(warnings);
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
            if (showResult && outcomes.Count > 0)
            {
                bool failed = outcomes.Any(o => o.Status == DownloadStatus.Failed);
                MessageBox.Show(
                    FindForm(),
                    string.Join(Environment.NewLine, outcomes.Select(o => o.Message)),
                    "OpenComposite",
                    MessageBoxButtons.OK,
                    failed ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
        }

        private void RefreshState(IList<string> warnings)
        {
            IReadOnlyList<CachedBuild> builds = cache.GetBuilds(warnings);
            summaryLabel.Text = DisplayText.OpenCompositeSummary(builds);
            downloadButton.Enabled = !busy;
            checkButton.Enabled = !busy && builds.Count > 0;
            acceptButton.Visible = builds.Any(b => b.HasPendingUpdate);
            acceptButton.Enabled = !busy;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                closing.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
