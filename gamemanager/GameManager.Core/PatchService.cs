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
using System.Text;

namespace GameManager.Core
{
    public enum ApplyOutcome
    {
        /// <summary>Simulation mode: the plan was shown, nothing ran.</summary>
        Simulated,

        /// <summary>Nothing ran: blocked, nothing to do, not confirmed, or the change could not be recorded.</summary>
        NotRun,

        /// <summary>The running-game guard refused; nothing was written.</summary>
        Refused,

        /// <summary>A step failed; the steps before it stay done and are listed.</summary>
        Failed,

        Succeeded,
    }

    public sealed class ApplyResult
    {
        internal ApplyResult(
            PatchPlan plan,
            ApplyOutcome outcome,
            IReadOnlyList<PatchAction> completedActions,
            bool needsElevation,
            string message,
            IReadOnlyList<string> warnings)
        {
            Plan = plan;
            Outcome = outcome;
            CompletedActions = completedActions ?? new PatchAction[0];
            NeedsElevation = needsElevation;
            Message = message;
            Warnings = warnings ?? new string[0];
        }

        public PatchPlan Plan { get; }
        public ApplyOutcome Outcome { get; }
        public IReadOnlyList<PatchAction> CompletedActions { get; }

        /// <summary>
        /// R22: Windows denied a write; the window offers an elevated relaunch.
        /// </summary>
        public bool NeedsElevation { get; }

        /// <summary>
        /// Multi-line text for the result window.
        /// </summary>
        public string Message { get; }

        public IReadOnlyList<string> Warnings { get; }

        /// <summary>
        /// True when files or records may have changed, so statuses must be read again.
        /// </summary>
        public bool ChangedSomething
        {
            get { return Outcome == ApplyOutcome.Succeeded || Outcome == ApplyOutcome.Failed; }
        }
    }

    /// <summary>
    /// Runs plans (R16). Simulation mode returns before anything runs. Otherwise a blocked or unconfirmed plan is
    /// refused, the executor runs the steps behind the running-game guard, and every DllChange that completed is
    /// recorded in state.json, with the hash of the DLL actually written (R30).
    /// </summary>
    public sealed class PatchService
    {
        private static readonly string NL = Environment.NewLine;

        private readonly PatchStateStore store;
        private readonly PatchExecutor executor;
        private readonly Func<DateTime> utcNow;

        public PatchService(PatchStateStore store, RunningGameGuard guard, Func<DateTime> utcNow)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            executor = new PatchExecutor(guard ?? throw new ArgumentNullException(nameof(guard)));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public ApplyResult Apply(PatchPlan plan, bool confirmed, bool simulation)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }
            string title = DisplayText.OperationTitle(plan);
            if (simulation)
            {
                // Checked first: in simulation mode nothing below runs, whatever the plan holds (R16). No game folder is written to.
                return new ApplyResult(plan, ApplyOutcome.Simulated, null, false,
                    "Simulation — nothing was changed." + NL + NL + DisplayText.PlanSummary(plan), null);
            }
            if (!plan.CanRun)
            {
                return new ApplyResult(plan, ApplyOutcome.NotRun, null, false,
                    title + " is not possible:" + NL + Bullets(plan.Blockers) + NL + "Nothing was changed.", null);
            }
            if (!plan.HasWork)
            {
                return new ApplyResult(plan, ApplyOutcome.NotRun, null, false, title + ": nothing to do." + NotesText(plan), null);
            }
            if (plan.NeedsConfirmation && !confirmed)
            {
                return new ApplyResult(plan, ApplyOutcome.NotRun, null, false, title + ": cancelled. Nothing was changed.", null);
            }

            var warnings = new List<string>();
            PatchState state = store.Load(warnings);
            if (!state.CanSave)
            {
                return new ApplyResult(plan, ApplyOutcome.NotRun, null, false,
                    title + ": nothing was changed, because the patch records (" + store.FilePath + ") could not be read, so the change could not be recorded.",
                    warnings);
            }

            ExecutionResult run = executor.Execute(plan);
            Record(plan, run.CompletedChanges, state, warnings);

            if (run.Refused)
            {
                return new ApplyResult(plan, ApplyOutcome.Refused, run.CompletedActions, run.NeedsElevation,
                    title + ": " + run.Error + " Nothing was changed.", warnings);
            }
            if (run.Succeeded)
            {
                return new ApplyResult(plan, ApplyOutcome.Succeeded, run.CompletedActions, false, title + ": done." + NotesText(plan), warnings);
            }

            var text = new StringBuilder();
            text.Append(title).Append(" stopped at this step: ").Append(run.FailedAction.Description).Append(NL).Append(run.Error);
            if (run.CompletedActions.Any(a => a.Kind != PatchActionKind.VerifyHash))
            {
                text.Append(NL).Append(NL).Append("Done before the stop:").Append(NL)
                    .Append(Bullets(run.CompletedActions.Select(a => a.Description).ToList()));
                if (run.CompletedChanges > 0 && plan.Operation != PatchOperation.Restore)
                {
                    text.Append(NL).Append(NL).Append("Some of the game's DLLs were changed; use Restore to undo them.");
                }
            }
            else
            {
                text.Append(NL).Append("Nothing was changed.");
            }
            text.Append(NotesText(plan));
            return new ApplyResult(plan, ApplyOutcome.Failed, run.CompletedActions, run.NeedsElevation, text.ToString(), warnings);
        }

        private void Record(PatchPlan plan, int completedChanges, PatchState state, List<string> warnings)
        {
            if (completedChanges == 0)
            {
                return;
            }
            for (int i = 0; i < completedChanges; i++)
            {
                DllChange change = plan.Changes[i];
                if (change.RemovesRecord)
                {
                    state.Remove(change.DllPath);
                }
                else if (change.RecordToSave != null)
                {
                    try
                    {
                        // R30: the hash of the file now in the game folder, never a value from cache.json or the plan.
                        state.Put(change.RecordToSave.WithOpenComposite(FileHash.Sha256(change.DllPath), utcNow()));
                    }
                    catch (Exception e) when (OpenCompositeCache.IsDiskError(e))
                    {
                        warnings.Add(change.DllPath + " was changed, but could not be read back to record it (" + e.Message + "). Patch the game again to record it; its backup is reused.");
                    }
                }
            }
            try
            {
                store.Save(state);
            }
            catch (Exception e) when (OpenCompositeCache.IsDiskError(e))
            {
                warnings.Add("The game files were changed, but the patch records could not be saved (" + store.FilePath + ": " + e.Message + "). The Status column may be out of date; patching the game again records it and reuses its backup.");
            }
        }

        internal static string Bullets(IReadOnlyList<string> lines)
        {
            return string.Join(NL, lines.Select(line => "- " + line));
        }

        private static string NotesText(PatchPlan plan)
        {
            return plan.Notes.Count == 0 ? "" : NL + NL + "Notes:" + NL + Bullets(plan.Notes);
        }
    }
}
