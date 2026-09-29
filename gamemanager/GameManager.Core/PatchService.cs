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
using System.Runtime.ExceptionServices;
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

    public sealed class BatchResult
    {
        internal BatchResult(
            IReadOnlyList<ApplyResult> results,
            IReadOnlyList<string> skipped,
            bool needsElevation,
            string message,
            IReadOnlyList<string> warnings)
        {
            Results = results;
            Skipped = skipped;
            NeedsElevation = needsElevation;
            Message = message;
            Warnings = warnings;
        }

        /// <summary>
        /// One result per plan that was run (or simulated), in order.
        /// </summary>
        public IReadOnlyList<ApplyResult> Results { get; }

        /// <summary>
        /// Games not run, each with the reason.
        /// </summary>
        public IReadOnlyList<string> Skipped { get; }

        public bool NeedsElevation { get; }
        public string Message { get; }
        public IReadOnlyList<string> Warnings { get; }

        public bool ChangedSomething
        {
            get { return Results.Any(r => r.ChangedSomething); }
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
                    "Simulation: nothing was written into any game folder." + NL + NL + DisplayText.PlanSummary(plan), null);
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
                    title + ": nothing was changed, because the patch records (" + store.FilePath + ") cannot be saved safely (the file could not be read, or could not be set aside), so a change could not be recorded.",
                    warnings);
            }

            ExecutionResult run;
            try
            {
                run = executor.Execute(plan);
            }
            catch (PatchExecutionException e)
            {
                // A bug, not a disk error: what was already written must still be recorded before it goes on.
                Record(plan, e.Partial, state, warnings);
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
            Record(plan, run, state, warnings);

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
                if (plan.Operation != PatchOperation.Restore && run.CompletedActions.Any(a => a.Kind == PatchActionKind.CopyFile))
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

        /// <summary>
        /// R24/R25: runs several games' plans one after another ("Re-patch all unpatched", "Update all"). A batch never
        /// asks questions: a plan that needs a confirmation (anti-cheat not ruled out, an unexpected backup) or has
        /// blockers is skipped with its reason, to be done alone with Patch. One game failing does not stop the others,
        /// except a permission error, which every following game would hit too.
        /// </summary>
        public BatchResult ApplyAll(IReadOnlyList<PatchPlan> plans, bool simulation)
        {
            if (plans == null)
            {
                throw new ArgumentNullException(nameof(plans));
            }
            var results = new List<ApplyResult>();
            var skipped = new List<string>();
            var warnings = new List<string>();
            var lines = new List<string>();
            bool needsElevation = false;
            foreach (PatchPlan plan in plans)
            {
                string title = DisplayText.OperationTitle(plan);
                if (!plan.CanRun)
                {
                    skipped.Add(title + ": skipped. " + string.Join(" ", plan.Blockers));
                    continue;
                }
                if (!plan.HasWork)
                {
                    continue;
                }
                if (plan.NeedsConfirmation)
                {
                    skipped.Add(title + ": skipped, because it needs your confirmation; select the game and click Patch. " + string.Join(" ", plan.Confirmations));
                    continue;
                }
                ApplyResult result = Apply(plan, false, simulation);
                results.Add(result);
                warnings.AddRange(result.Warnings);
                lines.Add(simulation ? DisplayText.PlanSummary(plan) : result.Message);
                if (result.NeedsElevation)
                {
                    needsElevation = true;
                    skipped.Add("Any games after " + plan.Game.Name + " were not changed: Windows denied access, and they would fail the same way.");
                    break;
                }
            }

            var text = new StringBuilder();
            if (simulation)
            {
                text.Append("Simulation - nothing was changed.").Append(NL).Append(NL);
            }
            if (lines.Count > 0)
            {
                text.Append(string.Join(NL + NL, lines));
            }
            else
            {
                text.Append(skipped.Count > 0 ? "Nothing was changed." : "Nothing to do.");
            }
            if (skipped.Count > 0)
            {
                text.Append(NL).Append(NL).Append("Skipped:").Append(NL).Append(Bullets(skipped));
            }
            return new BatchResult(results, skipped, needsElevation, text.ToString(), warnings);
        }

        private void Record(PatchPlan plan, ExecutionResult run, PatchState state, List<string> warnings)
        {
            bool changed = false;
            for (int i = 0; i < run.CompletedChanges; i++)
            {
                changed |= RecordChange(plan.Changes[i], run.CompletedActions, state, warnings);
            }
            // The change that failed may already have replaced or restored its DLL (later steps such as writing the
            // ini or deleting the backup failed): the record must follow the file, not the plan.
            if (run.FailedChangeIndex >= 0 && run.FailedChangeIndex < plan.Changes.Count)
            {
                DllChange partial = plan.Changes[run.FailedChangeIndex];
                bool dllWritten = partial.Actions.Any(a => (a.Kind == PatchActionKind.CopyFile || a.Kind == PatchActionKind.RestoreFile) && run.CompletedActions.Contains(a));
                if (dllWritten)
                {
                    changed |= RecordChange(partial, run.CompletedActions, state, warnings);
                }
            }
            if (!changed)
            {
                return;
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

        private bool RecordChange(DllChange change, IReadOnlyList<PatchAction> completed, PatchState state, List<string> warnings)
        {
            if (change.RemovesRecord)
            {
                state.Remove(change.DllPath);
                return true;
            }
            if (change.RecordToSave == null)
            {
                return false;
            }
            try
            {
                // R30: the hash of the file now in the game folder, never a value from cache.json or the plan.
                PatchRecord record = change.RecordToSave.WithOpenComposite(FileHash.Sha256(change.DllPath), utcNow());
                bool iniPlanned = change.Actions.Any(a => a.Kind == PatchActionKind.WriteIni);
                bool iniWritten = change.Actions.Any(a => a.Kind == PatchActionKind.WriteIni && completed.Contains(a));
                if (record.IniCreated && iniPlanned && !iniWritten)
                {
                    record = new PatchRecord(record.AppId, record.GameName, record.InstallDir, record.DllPath, record.Arch,
                        record.OriginalSha256, record.OpenCompositeSha256, false, record.PatchedUtc);
                }
                state.Put(record);
                return true;
            }
            catch (Exception e) when (OpenCompositeCache.IsDiskError(e))
            {
                warnings.Add(change.DllPath + " was changed, but could not be read back to record it (" + e.Message + "). Patch the game again to record it; its backup is reused.");
                return false;
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
