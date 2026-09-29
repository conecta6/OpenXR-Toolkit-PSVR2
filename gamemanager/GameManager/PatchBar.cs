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
using System.Threading.Tasks;
using System.Windows.Forms;
using GameManager.Core;

namespace GameManager
{
    /// <summary>
    /// Third row of the window: Patch and Restore for the selected game, the opencomposite.ini option and simulation
    /// mode. All work goes through PatchPlanner and PatchService; this class wires buttons, dialogs and messages, and
    /// shares the OperationGate with the OpenComposite row (R33).
    /// </summary>
    public sealed class PatchBar : FlowLayoutPanel
    {
        private readonly PatchPlanner planner;
        private readonly PatchService service;
        private readonly OperationGate gate;
        private readonly Action<string> addWarning;
        private readonly Action<string> showNotice;
        private readonly Button patchButton;
        private readonly Button restoreButton;
        private readonly CheckBox writeIniBox;
        private readonly NumericUpDown ratioBox;
        private readonly CheckBox simulationBox;
        private GameEntry selectedEntry;
        private GamePatchStatus selectedStatus;
        private bool running;

        public PatchBar(PatchPlanner planner, PatchService service, OperationGate gate, Action<string> addWarning, Action<string> showNotice)
        {
            this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            this.gate = gate ?? throw new ArgumentNullException(nameof(gate));
            this.addWarning = addWarning ?? throw new ArgumentNullException(nameof(addWarning));
            this.showNotice = showNotice ?? throw new ArgumentNullException(nameof(showNotice));

            Dock = DockStyle.Fill;
            AutoSize = true;
            WrapContents = false;

            patchButton = new Button { Text = "Patch", AutoSize = true };
            patchButton.Click += OnPatchClick;
            restoreButton = new Button { Text = "Restore", AutoSize = true };
            restoreButton.Click += OnRestoreClick;
            writeIniBox = new CheckBox { Text = "Write opencomposite.ini", AutoSize = true, Margin = new Padding(16, 6, 3, 0) };
            var ratioLabel = new Label { Text = "supersampleRatio", AutoSize = true, Margin = new Padding(3, 8, 3, 0) };
            ratioBox = new NumericUpDown
            {
                Minimum = (decimal)OpenCompositeIni.MinSupersampleRatio,
                Maximum = (decimal)OpenCompositeIni.MaxSupersampleRatio,
                Increment = 0.05m,
                DecimalPlaces = 2,
                Value = (decimal)OpenCompositeIni.DefaultSupersampleRatio,
                Width = 60,
                Enabled = false,
                Margin = new Padding(3, 5, 3, 0),
            };
            writeIniBox.CheckedChanged += (sender, e) => ratioBox.Enabled = writeIniBox.Checked;
            simulationBox = new CheckBox { Text = "Simulation - do not change files", AutoSize = true, Margin = new Padding(16, 6, 3, 0) };

            Controls.Add(patchButton);
            Controls.Add(restoreButton);
            Controls.Add(writeIniBox);
            Controls.Add(ratioLabel);
            Controls.Add(ratioBox);
            Controls.Add(simulationBox);
            gate.Changed += OnGateChanged;
            RefreshButtons();
        }

        /// <summary>
        /// Raised after an operation that may have changed files or records, so the window reads statuses again.
        /// </summary>
        public event EventHandler StatusesChanged;

        /// <summary>
        /// Raised after an elevated copy was started (R22); the window closes.
        /// </summary>
        public event EventHandler RelaunchStarted;

        /// <summary>
        /// True from the moment a Patch or Restore takes the gate until its lease is released (planning included).
        /// The window refuses to close meanwhile, so the executor is never cut off in the middle of a plan (P1).
        /// </summary>
        public bool IsOperationRunning
        {
            get { return running; }
        }

        /// <summary>
        /// The selected game and its status, or nulls when nothing is selected.
        /// </summary>
        public void SetSelection(GameEntry entry, GamePatchStatus status)
        {
            selectedEntry = entry;
            selectedStatus = status;
            RefreshButtons();
        }

        private void RefreshButtons()
        {
            bool free = !gate.IsBusy;
            patchButton.Enabled = free && PatchAvailability.CanPatch(selectedEntry);
            restoreButton.Enabled = free && PatchAvailability.CanRestore(selectedStatus);
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

        private async void OnPatchClick(object sender, EventArgs e)
        {
            GameEntry entry = selectedEntry;
            if (entry == null)
            {
                return;
            }
            var options = new PatchOptions(writeIniBox.Checked, (double)ratioBox.Value);
            await RunAsync("Patch", warnings => planner.PlanPatch(entry, options, warnings));
        }

        private async void OnRestoreClick(object sender, EventArgs e)
        {
            GameEntry entry = selectedEntry;
            if (entry == null)
            {
                return;
            }
            await RunAsync("Restore", warnings => planner.PlanRestore(entry, warnings));
        }

        /// <summary>
        /// Plan (inside the gate), ask the plan's questions (default No), apply (or only show, in simulation mode),
        /// show the result, and offer an elevated relaunch when Windows denied a write. The lease is taken and
        /// released here, on the UI thread.
        /// </summary>
        private async Task RunAsync(string operation, Func<List<string>, PatchPlan> makePlan)
        {
            if (IsDisposed)
            {
                return;
            }
            IDisposable lease = gate.TryEnter(operation);
            if (lease == null)
            {
                showNotice("Another operation is running (" + gate.CurrentOperation + "). Try again when it finishes.");
                return;
            }
            running = true;
            bool relaunch = false;
            var warnings = new List<string>();
            try
            {
                // Planned inside the gate, so the cached DLL cannot change between planning and copying (R33).
                PatchPlan plan = await Task.Run(() => makePlan(warnings));
                if (IsDisposed)
                {
                    return;
                }
                bool simulation = simulationBox.Checked;
                bool confirmed = false;
                if (!simulation && plan.CanRun && plan.HasWork && plan.NeedsConfirmation)
                {
                    confirmed = Confirm(plan);
                    if (!confirmed)
                    {
                        showNotice(DisplayText.OperationTitle(plan) + ": cancelled, nothing was changed.");
                        return;
                    }
                }
                ApplyResult result = await Task.Run(() => service.Apply(plan, confirmed, simulation));
                warnings.AddRange(result.Warnings);
                if (IsDisposed)
                {
                    return;
                }
                string title = result.Outcome == ApplyOutcome.Simulated ? "Simulation - nothing was changed" : DisplayText.OperationTitle(plan);
                PlanTextDialog.ShowText(FindForm(), title, result.Message);
                if (result.ChangedSomething)
                {
                    StatusesChanged?.Invoke(this, EventArgs.Empty);
                }
                if (result.NeedsElevation)
                {
                    relaunch = ElevatedRelaunch.Offer(FindForm(), result.Message);
                }
            }
            catch (Exception ex)
            {
                // async void callers must never let an exception escape: that would close the app.
                warnings.Add(operation + ": " + ex.Message);
                // Files may have changed before the failure: read the statuses again.
                StatusesChanged?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                running = false;
                lease.Dispose();
                if (!IsDisposed)
                {
                    foreach (string warning in warnings)
                    {
                        addWarning(warning);
                    }
                }
            }
            if (relaunch)
            {
                RelaunchStarted?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// R17/R18/R29: every question of the plan in one box, including the anti-cheat-not-ruled-out one (R31).
        /// Default No.
        /// </summary>
        private bool Confirm(PatchPlan plan)
        {
            string text = string.Join(Environment.NewLine + Environment.NewLine, plan.Confirmations)
                + Environment.NewLine + Environment.NewLine + "Continue?";
            return MessageBox.Show(
                FindForm(),
                text,
                DisplayText.OperationTitle(plan),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                gate.Changed -= OnGateChanged;
            }
            base.Dispose(disposing);
        }
    }
}
