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

namespace GameManager.Core
{
    /// <summary>
    /// R16: the only kinds of step a plan can hold.
    /// </summary>
    public enum PatchActionKind
    {
        /// <summary>Copy Source to Target; Target must not exist (a backup never overwrites anything).</summary>
        BackupFile,

        /// <summary>Check that Target's SHA-256 is ExpectedSha256; the plan stops if not.</summary>
        VerifyHash,

        /// <summary>Copy Source (a cached OpenComposite DLL) over Target through a hash-checked temporary copy.</summary>
        CopyFile,

        /// <summary>Write Content to Target (opencomposite.ini); an existing Target only when AllowOverwrite.</summary>
        WriteIni,

        /// <summary>Copy Source (openvr_api.dll.bak) over Target through a hash-checked temporary copy.</summary>
        RestoreFile,

        /// <summary>Delete Target, after checking its SHA-256 when ExpectedSha256 is set.</summary>
        DeleteFile,
    }

    public sealed class PatchAction
    {
        private PatchAction(PatchActionKind kind, string source, string target, string expectedSha256, string content, bool allowOverwrite, string description)
        {
            Kind = kind;
            Source = source;
            Target = target;
            ExpectedSha256 = expectedSha256;
            Content = content;
            AllowOverwrite = allowOverwrite;
            Description = description;
        }

        public PatchActionKind Kind { get; }

        /// <summary>
        /// The file copied from; null for VerifyHash, WriteIni and DeleteFile.
        /// </summary>
        public string Source { get; }

        public string Target { get; }

        /// <summary>
        /// For VerifyHash, CopyFile, RestoreFile and (optionally) DeleteFile; null otherwise.
        /// </summary>
        public string ExpectedSha256 { get; }

        /// <summary>
        /// The text WriteIni writes; null otherwise.
        /// </summary>
        public string Content { get; }

        public bool AllowOverwrite { get; }

        /// <summary>
        /// Human-readable, shown in simulation mode and in results.
        /// </summary>
        public string Description { get; }

        internal static PatchAction Backup(string source, string target)
        {
            return new PatchAction(PatchActionKind.BackupFile, source, target, null, null, false,
                "Back up " + source + " as " + Path.GetFileName(target));
        }

        internal static PatchAction Verify(string target, string expectedSha256)
        {
            return new PatchAction(PatchActionKind.VerifyHash, null, target, expectedSha256, null, false,
                "Check " + target + " (SHA-256 " + Short(expectedSha256) + ")");
        }

        internal static PatchAction Copy(string source, string target, string expectedSha256)
        {
            return new PatchAction(PatchActionKind.CopyFile, source, target, expectedSha256, null, true,
                "Copy OpenComposite (SHA-256 " + Short(expectedSha256) + ") from " + source + " to " + target);
        }

        internal static PatchAction WriteIni(string target, string content, bool allowOverwrite)
        {
            return new PatchAction(PatchActionKind.WriteIni, null, target, null, content, allowOverwrite,
                "Write " + target + " (" + content.Trim() + ")");
        }

        internal static PatchAction Restore(string backup, string target, string expectedSha256)
        {
            return new PatchAction(PatchActionKind.RestoreFile, backup, target, expectedSha256, null, true,
                "Put the original back: copy " + Path.GetFileName(backup) + " over " + target);
        }

        internal static PatchAction Delete(string target, string expectedSha256)
        {
            return new PatchAction(PatchActionKind.DeleteFile, null, target, expectedSha256, null, false,
                "Delete " + target);
        }

        private static string Short(string sha256)
        {
            return sha256 != null && sha256.Length > 12 ? sha256.Substring(0, 12) + "..." : sha256;
        }
    }

    public enum PatchOperation
    {
        Patch,
        Restore,
        Update,
    }

    /// <summary>
    /// The steps for one openvr_api.dll, and the record change to make once every step succeeded.
    /// </summary>
    public sealed class DllChange
    {
        internal DllChange(string dllPath, IReadOnlyList<PatchAction> actions, PatchRecord recordToSave, bool removesRecord)
        {
            DllPath = dllPath ?? throw new ArgumentNullException(nameof(dllPath));
            Actions = actions ?? new PatchAction[0];
            RecordToSave = recordToSave;
            RemovesRecord = removesRecord;
        }

        public string DllPath { get; }
        public IReadOnlyList<PatchAction> Actions { get; }

        /// <summary>
        /// Written to state.json once the steps succeeded, with OpenCompositeSha256 replaced by the hash of the DLL
        /// then in the game folder (R30). Null for a restore.
        /// </summary>
        public PatchRecord RecordToSave { get; }

        /// <summary>
        /// True for a restore: the record is removed once the steps succeeded.
        /// </summary>
        public bool RemovesRecord { get; }
    }

    /// <summary>
    /// R16: everything one operation on one game would do. Built only by PatchPlanner (the constructor is
    /// internal) and run only by PatchService. A plan with blockers keeps no changes, so it can never run.
    /// </summary>
    public sealed class PatchPlan
    {
        internal PatchPlan(
            PatchOperation operation,
            SteamGame game,
            IReadOnlyList<DllChange> changes,
            IReadOnlyList<string> blockers,
            IReadOnlyList<string> confirmations,
            IReadOnlyList<string> notes)
        {
            Operation = operation;
            Game = game ?? throw new ArgumentNullException(nameof(game));
            Blockers = blockers ?? new string[0];
            Changes = Blockers.Count > 0 ? new DllChange[0] : (changes ?? new DllChange[0]);
            Confirmations = confirmations ?? new string[0];
            Notes = notes ?? new string[0];
        }

        public PatchOperation Operation { get; }
        public SteamGame Game { get; }
        public IReadOnlyList<DllChange> Changes { get; }

        /// <summary>
        /// Why the operation is not possible; the plan never runs when this is not empty.
        /// </summary>
        public IReadOnlyList<string> Blockers { get; }

        /// <summary>
        /// Questions the user must answer Yes to (default No) before the plan runs.
        /// </summary>
        public IReadOnlyList<string> Confirmations { get; }

        /// <summary>
        /// Things the user should know (a file left unchanged, a DLL already up to date).
        /// </summary>
        public IReadOnlyList<string> Notes { get; }

        public bool CanRun
        {
            get { return Blockers.Count == 0; }
        }

        public bool NeedsConfirmation
        {
            get { return Confirmations.Count > 0; }
        }

        /// <summary>
        /// True when running the plan would change a file or a record.
        /// </summary>
        public bool HasWork
        {
            get { return Changes.Count > 0; }
        }

        public IReadOnlyList<PatchAction> AllActions
        {
            get
            {
                var actions = new List<PatchAction>();
                foreach (DllChange change in Changes)
                {
                    actions.AddRange(change.Actions);
                }
                return actions;
            }
        }

        /// <summary>
        /// The DLLs the running-game guard must be able to open for exclusive write.
        /// </summary>
        public IReadOnlyList<string> TargetDlls
        {
            get
            {
                var paths = new List<string>();
                foreach (DllChange change in Changes)
                {
                    paths.Add(change.DllPath);
                }
                return paths;
            }
        }
    }
}
