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
using System.Text;

namespace GameManager.Core
{
    /// <summary>
    /// What running a plan did. CompletedChanges counts the DllChanges whose every step succeeded, in plan order.
    /// </summary>
    internal sealed class ExecutionResult
    {
        public ExecutionResult(bool refused, IReadOnlyList<PatchAction> completedActions, int completedChanges, PatchAction failedAction, string error, bool needsElevation)
        {
            Refused = refused;
            CompletedActions = completedActions;
            CompletedChanges = completedChanges;
            FailedAction = failedAction;
            Error = error;
            NeedsElevation = needsElevation;
        }

        /// <summary>
        /// The running-game guard refused; nothing was written.
        /// </summary>
        public bool Refused { get; }

        public IReadOnlyList<PatchAction> CompletedActions { get; }
        public int CompletedChanges { get; }

        /// <summary>
        /// The step that failed, or null.
        /// </summary>
        public PatchAction FailedAction { get; }

        public string Error { get; }

        /// <summary>
        /// R22: Windows denied a write; an elevated relaunch may help.
        /// </summary>
        public bool NeedsElevation { get; }

        public bool Succeeded
        {
            get { return !Refused && FailedAction == null; }
        }
    }

    /// <summary>
    /// R16: runs a plan's steps in order and stops at the first failure. The only code that writes into a game
    /// folder. The running-game guard (R19) runs first, and nothing is written when it refuses.
    /// </summary>
    internal sealed class PatchExecutor
    {
        private readonly RunningGameGuard guard;

        public PatchExecutor(RunningGameGuard guard)
        {
            this.guard = guard ?? throw new ArgumentNullException(nameof(guard));
        }

        public ExecutionResult Execute(PatchPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }
            if (!plan.CanRun)
            {
                throw new InvalidOperationException("A plan with blockers cannot run.");
            }
            var completed = new List<PatchAction>();
            GuardResult check = guard.Check(plan.Game.InstallDir, plan.TargetDlls);
            if (!check.IsClear)
            {
                return new ExecutionResult(true, completed, 0, null, check.Message, check.NeedsElevation);
            }

            // The guard lets a target that does not exist yet through, and Steam may have updated or removed a file
            // since the plan was made. Before the first write, check every file the plan expects to find as it was
            // planned (its leading VerifyHash steps), for all DLLs, so a stale plan writes nothing at all.
            foreach (DllChange change in plan.Changes)
            {
                foreach (PatchAction action in change.Actions)
                {
                    if (action.Kind != PatchActionKind.VerifyHash)
                    {
                        break;
                    }
                    string problem;
                    try
                    {
                        problem = CheckHash(action.Target, action.ExpectedSha256);
                    }
                    catch (UnauthorizedAccessException e)
                    {
                        return new ExecutionResult(false, completed, 0, action, e.Message, true);
                    }
                    catch (Exception e) when (OpenCompositeCache.IsDiskError(e))
                    {
                        return new ExecutionResult(false, completed, 0, action, e.Message, false);
                    }
                    if (problem != null)
                    {
                        return new ExecutionResult(false, completed, 0, action, problem, false);
                    }
                }
            }

            int completedChanges = 0;
            foreach (DllChange change in plan.Changes)
            {
                foreach (PatchAction action in change.Actions)
                {
                    try
                    {
                        string problem = Run(action);
                        if (problem != null)
                        {
                            return new ExecutionResult(false, completed, completedChanges, action, problem, false);
                        }
                    }
                    catch (UnauthorizedAccessException e)
                    {
                        return new ExecutionResult(false, completed, completedChanges, action, e.Message, true);
                    }
                    catch (Exception e) when (OpenCompositeCache.IsDiskError(e))
                    {
                        return new ExecutionResult(false, completed, completedChanges, action, e.Message, false);
                    }
                    completed.Add(action);
                }
                completedChanges++;
            }
            return new ExecutionResult(false, completed, completedChanges, null, null, false);
        }

        /// <summary>
        /// Null when the step succeeded, else why it did not. I/O and permission errors throw.
        /// </summary>
        private static string Run(PatchAction action)
        {
            switch (action.Kind)
            {
                case PatchActionKind.BackupFile:
                    if (File.Exists(action.Target))
                    {
                        return action.Target + " already exists; a backup never overwrites a file.";
                    }
                    // overwrite: false, so a file that appeared since the check above is not replaced either.
                    File.Copy(action.Source, action.Target, false);
                    return null;

                case PatchActionKind.VerifyHash:
                    return CheckHash(action.Target, action.ExpectedSha256);

                case PatchActionKind.CopyFile:
                case PatchActionKind.RestoreFile:
                    return CopyVerified(action.Source, action.Target, action.ExpectedSha256);

                case PatchActionKind.WriteIni:
                    if (File.Exists(action.Target) && !action.AllowOverwrite)
                    {
                        return action.Target + " already exists and was not created by Game Manager; it is left unchanged.";
                    }
                    return WriteText(action.Target, action.Content);

                case PatchActionKind.DeleteFile:
                    if (!File.Exists(action.Target))
                    {
                        return null;
                    }
                    if (action.ExpectedSha256 != null)
                    {
                        string problem = CheckHash(action.Target, action.ExpectedSha256);
                        if (problem != null)
                        {
                            return problem;
                        }
                    }
                    File.Delete(action.Target);
                    return null;

                default:
                    throw new InvalidOperationException("Unknown step " + action.Kind + ".");
            }
        }

        private static string CheckHash(string path, string expected)
        {
            if (!File.Exists(path))
            {
                return path + " is missing.";
            }
            string actual = FileHash.Sha256(path);
            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return path + " has changed (SHA-256 " + actual + ", expected " + expected + "). Steam or another program may have updated it since the plan was made.";
        }

        /// <summary>
        /// Copies to a temporary file next to target (same volume), checks the copy's hash, then swaps it in with
        /// AtomicFile.Replace. On any failure the temporary file is removed and target is unchanged.
        /// </summary>
        private static string CopyVerified(string source, string target, string expected)
        {
            string temp = TempPathFor(target);
            try
            {
                File.Copy(source, temp, false);
                string actual = FileHash.Sha256(temp);
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return source + " is not the file the plan was made with (SHA-256 " + actual + ", expected " + expected + ").";
                }
                AtomicFile.Replace(temp, target);
                return null;
            }
            finally
            {
                AtomicFile.TryDelete(temp);
            }
        }

        private static string WriteText(string target, string content)
        {
            string temp = TempPathFor(target);
            try
            {
                File.WriteAllText(temp, content, new UTF8Encoding(false));
                AtomicFile.Replace(temp, target);
                return null;
            }
            finally
            {
                AtomicFile.TryDelete(temp);
            }
        }

        private static string TempPathFor(string target)
        {
            return target + ".gamemanager-" + Guid.NewGuid().ToString("N") + ".tmp";
        }
    }
}
