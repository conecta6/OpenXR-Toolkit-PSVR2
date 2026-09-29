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
using System.Text.RegularExpressions;
using System.Threading;
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    /// <summary>
    /// The final fix wave of phases 5-6: known OpenComposite hashes, opencomposite.ini ownership by hash, capped
    /// folder lists, and a batch game that fails after its DLL was copied.
    /// </summary>
    [TestClass]
    public class FinalFixWaveTests
    {
        private PatchFixture fx;
        private byte[] original;

        [TestInitialize]
        public void SetUp()
        {
            fx = new PatchFixture();
            original = PatchFixture.OriginalDll(PeFixture.MachineX64, 1);
        }

        [TestCleanup]
        public void TearDown()
        {
            fx.Dispose();
        }

        private List<string> NoWarnings()
        {
            return new List<string>();
        }

        private PatchPlan PlanPatch(PatchOptions options)
        {
            return fx.Planner.PlanPatch(fx.Scan(), options, NoWarnings());
        }

        private PatchPlan PlanRestore()
        {
            return fx.Planner.PlanRestore(fx.Scan(), NoWarnings());
        }

        private void Apply(PatchPlan plan)
        {
            ApplyResult result = fx.Service.Apply(plan, false, false);
            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
        }

        private string IniPath
        {
            get { return Path.Combine(fx.InstallDir, "opencomposite.ini"); }
        }

        /// <summary>
        /// The game patched with OpenComposite build 1 and an opencomposite.ini of ratio 1.25 written by Game Manager.
        /// </summary>
        private string PatchWithIni()
        {
            string dll = fx.WriteGameFile("openvr_api.dll", original);
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            Apply(PlanPatch(new PatchOptions(true, 1.25)));
            return dll;
        }

        private void ForgetTheIniHash(string dll)
        {
            PatchState state = fx.StateStore.Load(NoWarnings());
            state.Put(state.Find(dll).WithIni(true, null));
            fx.StateStore.Save(state);
        }

        // ---- 2. known OpenComposite hashes ----

        [TestMethod]
        public void KnownHashes_KeepEveryBuildEverDownloaded()
        {
            string one = fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            string two = fx.CacheOpenComposite(OpenCompositeArch.X64, 2);

            List<string> known = fx.Cache.GetKnownHashes(NoWarnings()).ToList();

            CollectionAssert.Contains(known, one);
            CollectionAssert.Contains(known, two);
        }

        [TestMethod]
        public void KnownHashes_SurviveAnUpdateCheckAndAccept()
        {
            string one = fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            fx.Http.Respond = url => OpenCompositeFixture.Dll(PeFixture.MachineX64, 2);
            var warnings = NoWarnings();
            IReadOnlyList<DownloadOutcome> checkedOutcomes = fx.Cache.CheckForUpdatesAsync(warnings, CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual(DownloadStatus.UpdateFound, checkedOutcomes[0].Status);
            string two = FileHash.Sha256(fx.Cache.PendingDllPath(OpenCompositeArch.X64));
            CollectionAssert.Contains(fx.Cache.GetKnownHashes(warnings).ToList(), two, "a waiting build is known");

            Assert.AreEqual(DownloadStatus.UpdateAccepted, fx.Cache.AcceptPendingUpdate(OpenCompositeArch.X64, warnings).Status);
            fx.CacheOpenComposite(OpenCompositeArch.X64, 3);

            List<string> known = fx.Cache.GetKnownHashes(warnings).ToList();
            CollectionAssert.Contains(known, one);
            CollectionAssert.Contains(known, two);
            Assert.AreEqual(3, known.Count);
        }

        [TestMethod]
        public void KnownHashes_CacheJsonFromBeforeTheList_StillKnowsItsCurrentBuild()
        {
            string one = fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            string text = File.ReadAllText(fx.Cache.CacheFilePath);
            string legacy = Regex.Replace(text, @",\s*""knownSha256"":\s*\[[^\]]*\]", "");
            Assert.AreNotEqual(text, legacy, "the test must really remove the list");
            File.WriteAllText(fx.Cache.CacheFilePath, legacy);

            CollectionAssert.Contains(fx.Cache.GetKnownHashes(NoWarnings()).ToList(), one);
        }

        [TestMethod]
        public void PlanPatch_OldOpenComposite_NotCachedAndNotRecorded_IsNotTakenForTheOriginal()
        {
            // Build 1 was downloaded once and installed by hand; the cache has moved on and the records are gone.
            string dll = fx.WriteGameFile("openvr_api.dll", OpenCompositeFixture.Dll(PeFixture.MachineX64, 1));
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            fx.CacheOpenComposite(OpenCompositeArch.X64, 2);

            PatchPlan noBackup = PlanPatch(PatchOptions.None);

            Assert.IsFalse(noBackup.CanRun);
            StringAssert.Contains(noBackup.Blockers[0], "already an OpenComposite DLL");

            File.WriteAllBytes(dll + ".bak", original);
            PatchPlan withBackup = PlanPatch(PatchOptions.None);

            Assert.IsTrue(withBackup.CanRun, string.Join("\n", withBackup.Blockers));
            Assert.IsFalse(withBackup.AllActions.Any(a => a.Kind == PatchActionKind.BackupFile), "OpenComposite is never backed up as the original");
            Assert.AreEqual(FileHash.Sha256(dll + ".bak"), withBackup.Changes[0].RecordToSave.OriginalSha256);
        }

        [TestMethod]
        public void PlanRestore_CurrentDllIsAnOldOpenComposite_DoesNotAsk()
        {
            string dll = fx.WriteGameFile("openvr_api.dll", original);
            string one = fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            Apply(PlanPatch(PatchOptions.None));
            fx.CacheOpenComposite(OpenCompositeArch.X64, 2);
            Apply(fx.Planner.PlanUpdate(fx.Scan(), NoWarnings()));
            File.WriteAllBytes(dll, OpenCompositeFixture.Dll(PeFixture.MachineX64, 1));
            Assert.AreEqual(one, FileHash.Sha256(dll));

            PatchPlan plan = PlanRestore();

            Assert.IsTrue(plan.CanRun, string.Join("\n", plan.Blockers));
            Assert.IsFalse(plan.NeedsConfirmation, "an old OpenComposite build restores without a question");
        }

        // ---- 3. opencomposite.ini ownership ----

        [TestMethod]
        public void Patch_WritesTheIni_AndRecordsItsHash()
        {
            string dll = PatchWithIni();

            PatchRecord record = fx.FindRecord(dll);

            Assert.IsTrue(record.IniCreated);
            Assert.AreEqual(FileHash.Sha256(IniPath), record.IniSha256);
        }

        [TestMethod]
        public void Patch_OwnUnchangedIni_IsOverwrittenAndItsHashUpdated()
        {
            string dll = PatchWithIni();

            Apply(PlanPatch(new PatchOptions(true, 1.5)));

            Assert.AreEqual("supersampleRatio=1.5\r\n", File.ReadAllText(IniPath));
            Assert.AreEqual(FileHash.Sha256(IniPath), fx.FindRecord(dll).IniSha256);
        }

        [TestMethod]
        public void Patch_OwnIniThatWasEdited_IsLeftAloneWithANote()
        {
            string dll = PatchWithIni();
            File.WriteAllText(IniPath, "supersampleRatio=2.0\r\n");
            string oldHash = fx.FindRecord(dll).IniSha256;

            PatchPlan plan = PlanPatch(new PatchOptions(true, 1.5));
            Assert.IsFalse(plan.HasWork, "the DLL is current and the ini is not ours to change");
            Assert.AreEqual(ApplyOutcome.NotRun, fx.Service.Apply(plan, false, false).Outcome);

            Assert.IsTrue(plan.Notes.Any(n => n.Contains("has changed since")), string.Join("\n", plan.Notes));
            Assert.AreEqual("supersampleRatio=2.0\r\n", File.ReadAllText(IniPath));
            Assert.IsTrue(fx.FindRecord(dll).IniCreated);
            Assert.AreEqual(oldHash, fx.FindRecord(dll).IniSha256);
        }

        [TestMethod]
        public void Patch_OwnIniFromARecordWithoutAHash_IsLeftAloneWithANote()
        {
            string dll = PatchWithIni();
            ForgetTheIniHash(dll);

            PatchPlan plan = PlanPatch(new PatchOptions(true, 1.5));
            Assert.IsFalse(plan.HasWork, "the DLL is current and the ini is not ours to change");
            Assert.AreEqual(ApplyOutcome.NotRun, fx.Service.Apply(plan, false, false).Outcome);

            Assert.IsTrue(plan.Notes.Any(n => n.Contains("left unchanged")), string.Join("\n", plan.Notes));
            Assert.AreEqual("supersampleRatio=1.25\r\n", File.ReadAllText(IniPath));
        }

        [TestMethod]
        public void Restore_IniStillAsWritten_IsDeleted()
        {
            PatchWithIni();

            PatchPlan plan = PlanRestore();
            Apply(plan);

            Assert.IsFalse(File.Exists(IniPath));
        }

        [TestMethod]
        public void Restore_IniEditedSinceItWasWritten_IsLeftInPlaceWithANote()
        {
            string dll = PatchWithIni();
            File.WriteAllText(IniPath, "supersampleRatio=2.0\r\n");

            PatchPlan plan = PlanRestore();
            Apply(plan);

            Assert.IsTrue(File.Exists(IniPath));
            Assert.AreEqual("supersampleRatio=2.0\r\n", File.ReadAllText(IniPath));
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("left in place")), string.Join("\n", plan.Notes));
            Assert.IsNull(fx.FindRecord(dll), "the DLL is still restored and the record removed");
            CollectionAssert.AreEqual(original, File.ReadAllBytes(dll));
        }

        [TestMethod]
        public void Restore_RecordWithoutAnIniHash_LeavesTheIniInPlaceWithANote()
        {
            string dll = PatchWithIni();
            ForgetTheIniHash(dll);

            PatchPlan plan = PlanRestore();
            Apply(plan);

            Assert.IsTrue(File.Exists(IniPath));
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("left in place")), string.Join("\n", plan.Notes));
        }

        [TestMethod]
        public void Apply_FailedOverwriteOfOwnIni_KeepsIniCreatedAndTheOldHash()
        {
            string dll = PatchWithIni();
            PatchRecord before = fx.FindRecord(dll);
            string current = FileHash.Sha256(dll);
            // The DLL is copied again, then the ini step fails (an existing ini without permission to overwrite).
            var change = new DllChange(
                dll,
                new[]
                {
                    PatchAction.Verify(dll, current),
                    PatchAction.Copy(fx.Cache.DllPath(OpenCompositeArch.X64), dll, current),
                    PatchAction.WriteIni(IniPath, "supersampleRatio=1.0\r\n", false),
                },
                before,
                false);
            var plan = new PatchPlan(PatchOperation.Patch, fx.Game, new[] { change }, null, null, null);

            ApplyResult result = fx.Service.Apply(plan, false, false);

            Assert.AreEqual(ApplyOutcome.Failed, result.Outcome);
            PatchRecord after = fx.FindRecord(dll);
            Assert.IsTrue(after.IniCreated);
            Assert.AreEqual(before.IniSha256, after.IniSha256);
            Assert.AreEqual("supersampleRatio=1.25\r\n", File.ReadAllText(IniPath));
        }

        // ---- 4. capped folder lists ----

        [TestMethod]
        public void PlanPatch_ManyUncheckedFolders_ListsTenAndCountsTheRest()
        {
            fx.WriteGameFile("openvr_api.dll", original);
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            string[] folders = Enumerable.Range(1, 12).Select(i => "Folder" + i.ToString("00")).ToArray();

            PatchPlan plan = fx.Planner.PlanPatch(
                fx.ScanWith(CompatibilityVerdict.For(null, new string[0], folders)), PatchOptions.None, NoWarnings());

            string question = plan.Confirmations.Single();
            StringAssert.Contains(question, "Folder10");
            StringAssert.Contains(question, "and 2 more");
            Assert.IsFalse(question.Contains("Folder11"));
        }

        // ---- 7. a batch game that fails after its DLL was copied ----

        [TestMethod]
        public void ApplyAll_GameFailsAfterItsDllWasCopied_IsRecordedAndNamedInTheReport()
        {
            string oc = fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            SteamGame second = fx.AddGame(1001, "Second Game");
            SteamGame third = fx.AddGame(1002, "Third Game");
            string secondDll = PatchFixture.WriteFileIn(second, "openvr_api.dll", original);
            string thirdDll = PatchFixture.WriteFileIn(third, "openvr_api.dll", original);
            // A folder where the ini should go: the DLL is copied, then writing the ini fails.
            Directory.CreateDirectory(Path.Combine(second.InstallDir, "opencomposite.ini"));
            var options = new PatchOptions(true, 1.25);
            var plans = new[]
            {
                fx.Planner.PlanPatch(fx.ScanOf(second, null), options, NoWarnings()),
                fx.Planner.PlanPatch(fx.ScanOf(third, null), options, NoWarnings()),
            };

            BatchResult result = fx.Service.ApplyAll(plans, false);

            Assert.AreEqual(2, result.Results.Count);
            Assert.AreEqual(ApplyOutcome.Failed, result.Results[0].Outcome, result.Message);
            Assert.AreEqual(ApplyOutcome.Succeeded, result.Results[1].Outcome, result.Message);
            Assert.IsTrue(result.ChangedSomething);
            Assert.AreEqual(oc, FileHash.Sha256(secondDll), "the DLL was copied");
            PatchRecord partial = fx.FindRecord(secondDll);
            Assert.IsNotNull(partial, "the partial work is recorded, so Restore can undo it");
            Assert.AreEqual(oc, partial.OpenCompositeSha256);
            Assert.IsFalse(partial.IniCreated);
            Assert.IsTrue(File.Exists(secondDll + ".bak"));
            StringAssert.Contains(result.Message, "Patch Second Game (1001) stopped at this step");
            StringAssert.Contains(result.Message, "use Restore to undo them");
            Assert.AreEqual(oc, FileHash.Sha256(thirdDll));
        }
    }
}
