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
using System.Globalization;
using System.IO;
using System.Linq;

namespace GameManager.Core
{
    /// <summary>
    /// The options of one Patch click (R21).
    /// </summary>
    public sealed class PatchOptions
    {
        public PatchOptions(bool writeIni, double supersampleRatio)
        {
            if (writeIni)
            {
                // Checks the range now, so a bad value never reaches a plan.
                OpenCompositeIni.Render(supersampleRatio);
            }
            WriteIni = writeIni;
            SupersampleRatio = supersampleRatio;
        }

        /// <summary>
        /// No opencomposite.ini: what "Re-patch all unpatched" uses.
        /// </summary>
        public static PatchOptions None { get; } = new PatchOptions(false, OpenCompositeIni.DefaultSupersampleRatio);

        public bool WriteIni { get; }
        public double SupersampleRatio { get; }
    }

    /// <summary>
    /// Builds plans (R16). Reads the game folder, the OpenComposite cache and state.json; never writes. Hashes are
    /// taken when the plan is built (at click time, not at scan time), and every file a step replaces is checked
    /// again by a VerifyHash step when the plan runs.
    /// </summary>
    public sealed class PatchPlanner
    {
        public const string BackupSuffix = ".bak";

        private readonly OpenCompositeCache cache;
        private readonly PatchStateStore store;
        private readonly Func<DateTime> utcNow;

        public PatchPlanner(OpenCompositeCache cache, PatchStateStore store, Func<DateTime> utcNow)
        {
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        /// <summary>
        /// R15/R17/R21/R29: patches every openvr_api.dll of the game with the cached OpenComposite build of the same
        /// architecture, or explains why not. One DLL that cannot be patched blocks the whole game.
        /// </summary>
        public PatchPlan PlanPatch(GameEntry entry, PatchOptions options, IList<string> warnings)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (warnings == null)
            {
                throw new ArgumentNullException(nameof(warnings));
            }
            var plan = new PlanBuilder(PatchOperation.Patch, entry.Game);
            if (entry.Compatibility.Blocked)
            {
                plan.Blockers.Add("This game is blocked: " + entry.Compatibility.Reason + " Games with anti-cheat are never patched.");
                return plan.Build();
            }
            if (entry.Classification.Kind != GameKind.OpenVr || entry.Classification.OpenVrDlls.Count == 0)
            {
                plan.Blockers.Add("No openvr_api.dll was found in this game, so there is nothing to patch.");
                return plan.Build();
            }
            AddAntiCheatQuestion(plan, entry);
            Inputs inputs = LoadInputs(warnings);
            var inisPlanned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (OpenVrDll dll in entry.Classification.OpenVrDlls)
            {
                PlanPatchDll(plan, entry.Game, dll.RelativePath, options, inputs, inisPlanned);
            }
            return plan.Build();
        }

        private void PlanPatchDll(PlanBuilder plan, SteamGame game, string relativePath, PatchOptions options, Inputs inputs, HashSet<string> inisPlanned)
        {
            string dllPath = Path.Combine(game.InstallDir, relativePath);
            string bakPath = dllPath + BackupSuffix;
            if (!File.Exists(dllPath))
            {
                plan.Blockers.Add(relativePath + " is no longer there (the game may have been updated or moved). Click Refresh and try again.");
                return;
            }
            PeMachine machine;
            string current;
            string bakHash;
            try
            {
                // Read now, not taken from the scan: Steam may have updated the game since.
                machine = PeReader.ReadMachine(dllPath);
                current = FileHash.Sha256(dllPath);
                bakHash = File.Exists(bakPath) ? FileHash.Sha256(bakPath) : null;
            }
            catch (Exception e) when (OpenCompositeCache.IsDiskError(e))
            {
                plan.Blockers.Add(relativePath + " could not be read (" + e.Message + "). Close the game and try again.");
                return;
            }

            OpenCompositeArch? found = OpenCompositeCache.ArchFor(machine);
            if (found == null)
            {
                // R15: one DLL no OpenComposite build matches makes the whole game unpatchable.
                string what = machine == PeMachine.Unknown ? "not a DLL Game Manager can read" : "an " + DisplayText.Machine(machine) + " DLL";
                plan.Blockers.Add(relativePath + " is " + what + ". OpenComposite exists only for x64 and x86, so this game cannot be patched.");
                return;
            }
            OpenCompositeArch arch = found.Value;
            string archName = OpenCompositeCache.ArchName(arch);
            string problem;
            if (inputs.CacheProblem.TryGetValue(arch, out problem))
            {
                plan.Blockers.Add(CacheProblemBlocker(archName, problem));
                return;
            }
            string target;
            if (!inputs.CachedHash.TryGetValue(arch, out target))
            {
                plan.Blockers.Add(relativePath + " needs OpenComposite " + archName + ", which is not downloaded. Click \"Download OpenComposite\" first.");
                return;
            }

            string cachedDll = cache.DllPath(arch);
            PatchRecord record = inputs.State.Find(dllPath);
            bool alreadyOurs = record != null && Same(current, record.OpenCompositeSha256);
            var actions = new List<PatchAction>();
            string original;

            if (alreadyOurs)
            {
                // The build Game Manager installed: only a newer cached build changes the DLL. The .bak is the game's
                // original and is never touched here.
                original = record.OriginalSha256;
                if (!Same(current, target))
                {
                    actions.Add(PatchAction.Verify(dllPath, current));
                    actions.Add(PatchAction.Copy(cachedDll, dllPath, target));
                    actions.Add(PatchAction.Verify(dllPath, target));
                }
            }
            else if (inputs.KnownOpenComposite.Contains(current))
            {
                // Already OpenComposite, but not recorded (a manual install, or lost records). It must never become the
                // "original" backup.
                if (bakHash == null)
                {
                    plan.Blockers.Add(relativePath + " is already an OpenComposite DLL, but there is no openvr_api.dll.bak with the game's original. Use Steam's \"Verify integrity of game files\" to get the original back, then patch again.");
                    return;
                }
                if (inputs.KnownOpenComposite.Contains(bakHash))
                {
                    plan.Blockers.Add(relativePath + ": openvr_api.dll.bak is itself an OpenComposite DLL, so the game's original is not there. Use Steam's \"Verify integrity of game files\", delete openvr_api.dll.bak, then patch again.");
                    return;
                }
                original = bakHash;
                plan.Notes.Add(relativePath + ": the existing openvr_api.dll.bak is kept as the game's original.");
                actions.Add(PatchAction.Verify(dllPath, current));
                actions.Add(PatchAction.Verify(bakPath, bakHash));
                if (!Same(current, target))
                {
                    actions.Add(PatchAction.Copy(cachedDll, dllPath, target));
                    actions.Add(PatchAction.Verify(dllPath, target));
                }
            }
            else
            {
                // The current DLL is the game's original.
                original = current;
                actions.Add(PatchAction.Verify(dllPath, current));
                if (bakHash == null)
                {
                    actions.Add(PatchAction.Backup(dllPath, bakPath));
                    actions.Add(PatchAction.Verify(bakPath, current));
                }
                else if (Same(bakHash, current))
                {
                    // R17: the existing backup already holds this exact file; it is reused, never overwritten.
                    actions.Add(PatchAction.Verify(bakPath, bakHash));
                }
                else
                {
                    // R17: a backup that differs from the current DLL (an earlier manual install, an older game
                    // version). Ask first (default No); if the user goes on, it is kept under another name.
                    string aside = bakPath + ".old-" + inputs.Stamp;
                    plan.Confirmations.Add(relativePath + ": an openvr_api.dll.bak already exists and differs from the current DLL (for example from an earlier manual install or an older version of the game). If you continue, it is kept as "
                        + Path.GetFileName(aside) + " and the current DLL becomes the backup.");
                    actions.Add(PatchAction.Verify(bakPath, bakHash));
                    actions.Add(PatchAction.Backup(bakPath, aside));
                    actions.Add(PatchAction.Verify(aside, bakHash));
                    actions.Add(PatchAction.Delete(bakPath, bakHash));
                    actions.Add(PatchAction.Backup(dllPath, bakPath));
                    actions.Add(PatchAction.Verify(bakPath, current));
                }
                actions.Add(PatchAction.Copy(cachedDll, dllPath, target));
                actions.Add(PatchAction.Verify(dllPath, target));
            }

            bool iniCreated = record != null && record.IniCreated;
            PatchAction ini = PlanIni(plan, dllPath, relativePath, options, inputs, inisPlanned);
            if (ini != null)
            {
                actions.Add(ini);
                iniCreated = true;
            }
            if (alreadyOurs && actions.Count == 0)
            {
                plan.Notes.Add(relativePath + ": already patched with the current OpenComposite build.");
                return;
            }
            // Its OpenCompositeSha256 is replaced by the hash of the written file when the plan runs (R30).
            var recordToSave = new PatchRecord(game.AppId, game.Name, game.InstallDir, dllPath, arch, original, target, iniCreated, utcNow());
            plan.Changes.Add(new DllChange(dllPath, actions, recordToSave, false));
        }

        /// <summary>
        /// The one blocker text for a cached OpenComposite DLL that failed its check (Task 7 and 9 plans reuse it).
        /// </summary>
        private static string CacheProblemBlocker(string archName, string problem)
        {
            return "The downloaded OpenComposite " + archName + " DLL failed its check: " + problem + " Click \"Download OpenComposite\" to download it again.";
        }

        private static PatchAction PlanIni(PlanBuilder plan, string dllPath, string relativePath, PatchOptions options, Inputs inputs, HashSet<string> inisPlanned)
        {
            if (!options.WriteIni)
            {
                return null;
            }
            string folder = Path.GetDirectoryName(dllPath);
            string iniPath = Path.Combine(folder, OpenCompositeIni.FileName);
            if (!inisPlanned.Add(iniPath))
            {
                return null;
            }
            bool ours = inputs.State.Records.Any(r => r.IniCreated
                && string.Equals(Path.GetDirectoryName(r.DllPath), folder, StringComparison.OrdinalIgnoreCase));
            if (File.Exists(iniPath) && !ours)
            {
                // R21: an ini Game Manager did not create is never overwritten.
                plan.Notes.Add(Path.Combine(Path.GetDirectoryName(relativePath), OpenCompositeIni.FileName)
                    + " already exists and was not created by Game Manager; it was left unchanged.");
                return null;
            }
            return PatchAction.WriteIni(iniPath, OpenCompositeIni.Render(options.SupersampleRatio), ours);
        }

        private static void AddAntiCheatQuestion(PlanBuilder plan, GameEntry entry)
        {
            if (entry.Compatibility.AntiCheatNotRuledOut)
            {
                // R29/R31: read from the verdict, never from warning text.
                plan.Confirmations.Add("Anti-cheat could not be ruled out: these folders could not be checked for anti-cheat files: "
                    + string.Join(", ", entry.Compatibility.UncheckedFolders)
                    + ". Patching a game that has anti-cheat can get your account banned. Continue only if you are sure this game has none.");
            }
        }

        /// <summary>
        /// Everything a plan compares against, read once per plan.
        /// </summary>
        private Inputs LoadInputs(IList<string> warnings)
        {
            var inputs = new Inputs(store.Load(warnings), utcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            foreach (CachedBuild build in cache.GetBuilds(warnings))
            {
                inputs.KnownOpenComposite.Add(build.Sha256);
                if (build.PendingSha256 != null)
                {
                    inputs.KnownOpenComposite.Add(build.PendingSha256);
                }
                // R32: the cached DLL is validated again right before use (size, PE header, architecture), and its
                // hash is read from the file itself, never from cache.json (R34).
                string problem = OpenCompositeCache.Validate(build.DllPath, build.Arch);
                if (problem == null)
                {
                    try
                    {
                        string hash = FileHash.Sha256(build.DllPath);
                        inputs.CachedHash[build.Arch] = hash;
                        inputs.KnownOpenComposite.Add(hash);
                    }
                    catch (Exception e) when (OpenCompositeCache.IsDiskError(e))
                    {
                        problem = "the file could not be read (" + e.Message + ").";
                    }
                }
                if (problem != null)
                {
                    inputs.CacheProblem[build.Arch] = problem;
                }
            }
            foreach (PatchRecord record in inputs.State.Records)
            {
                inputs.KnownOpenComposite.Add(record.OpenCompositeSha256);
            }
            return inputs;
        }

        private static bool Same(string a, string b)
        {
            return a != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class Inputs
        {
            public Inputs(PatchState state, string stamp)
            {
                State = state;
                Stamp = stamp;
            }

            public PatchState State { get; }

            /// <summary>
            /// UTC "yyyyMMdd-HHmmss" for the names of files kept aside.
            /// </summary>
            public string Stamp { get; }

            public Dictionary<OpenCompositeArch, string> CachedHash { get; } = new Dictionary<OpenCompositeArch, string>();
            public Dictionary<OpenCompositeArch, string> CacheProblem { get; } = new Dictionary<OpenCompositeArch, string>();

            /// <summary>
            /// Hashes known to be OpenComposite: the cached files, cache.json's current and pending hashes, and every
            /// recorded OpenComposite hash.
            /// </summary>
            public HashSet<string> KnownOpenComposite { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class PlanBuilder
        {
            public PlanBuilder(PatchOperation operation, SteamGame game)
            {
                Operation = operation;
                Game = game;
            }

            public PatchOperation Operation { get; }
            public SteamGame Game { get; }
            public List<DllChange> Changes { get; } = new List<DllChange>();
            public List<string> Blockers { get; } = new List<string>();
            public List<string> Confirmations { get; } = new List<string>();
            public List<string> Notes { get; } = new List<string>();

            public PatchPlan Build()
            {
                return new PatchPlan(Operation, Game, Changes, Blockers, Confirmations, Notes);
            }
        }
    }
}
