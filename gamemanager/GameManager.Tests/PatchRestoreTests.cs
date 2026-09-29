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
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    [TestClass]
    public class PatchRestoreTests
    {
        private PatchFixture fx;
        private byte[] original;

        [TestInitialize]
        public void SetUp()
        {
            fx = new PatchFixture();
            original = PatchFixture.OriginalDll(PeFixture.MachineX64, 1);
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
        }

        [TestCleanup]
        public void TearDown()
        {
            fx.Dispose();
        }

        private void PatchScannedGame(PatchOptions options)
        {
            ApplyResult result = fx.Service.Apply(fx.Planner.PlanPatch(fx.Scan(), options, new List<string>()), false, false);
            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
        }

        private string PatchGame(PatchOptions options)
        {
            string dll = fx.WriteGameFile("openvr_api.dll", original);
            PatchScannedGame(options);
            return dll;
        }

        private PatchPlan PlanRestore()
        {
            return fx.Planner.PlanRestore(fx.Scan(), new List<string>());
        }

        [TestMethod]
        public void PlanRestore_PatchedGame_PutsTheOriginalBackAndRemovesBackupAndRecord()
        {
            string dll = PatchGame(PatchOptions.None);

            PatchPlan plan = PlanRestore();
            ApplyResult result = fx.Service.Apply(plan, false, false);

            Assert.IsTrue(plan.CanRun, string.Join("\n", plan.Blockers));
            Assert.IsFalse(plan.NeedsConfirmation);
            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(dll));
            Assert.IsFalse(File.Exists(dll + ".bak"));
            Assert.IsNull(fx.FindRecord(dll));
        }

        [TestMethod]
        public void PlanRestore_IniCreatedByGameManager_IsDeleted()
        {
            PatchGame(new PatchOptions(true, 1.25));
            string ini = Path.Combine(fx.InstallDir, "opencomposite.ini");
            Assert.IsTrue(File.Exists(ini));

            ApplyResult result = fx.Service.Apply(PlanRestore(), false, false);

            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
            Assert.IsFalse(File.Exists(ini));
        }

        [TestMethod]
        public void PlanRestore_IniThatWasThereBefore_IsNeverDeleted()
        {
            string ini = fx.WriteGameFile("opencomposite.ini", Encoding.ASCII.GetBytes("supersampleRatio=1.7\r\n"));
            PatchGame(new PatchOptions(true, 1.25));

            ApplyResult result = fx.Service.Apply(PlanRestore(), false, false);

            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
            Assert.AreEqual("supersampleRatio=1.7\r\n", File.ReadAllText(ini));
        }

        [TestMethod]
        public void PlanRestore_CurrentDllUnknown_AsksAndKeepsItAside()
        {
            string dll = PatchGame(PatchOptions.None);
            byte[] third = PatchFixture.OriginalDll(PeFixture.MachineX64, 3);
            File.WriteAllBytes(dll, third);
            string aside = dll + ".replaced-20260929-101500";

            PatchPlan plan = PlanRestore();

            Assert.IsTrue(plan.CanRun, string.Join("\n", plan.Blockers));
            Assert.AreEqual(1, plan.Confirmations.Count);
            StringAssert.Contains(plan.Confirmations[0], "openvr_api.dll.replaced-20260929-101500");
            string before = fx.Snapshot();
            Assert.AreEqual(ApplyOutcome.NotRun, fx.Service.Apply(plan, false, false).Outcome);
            Assert.AreEqual(before, fx.Snapshot(), "No (the default) must change nothing");

            ApplyResult result = fx.Service.Apply(plan, true, false);

            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
            CollectionAssert.AreEqual(third, File.ReadAllBytes(aside));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(dll));
            Assert.IsFalse(File.Exists(dll + ".bak"));
            Assert.IsNull(fx.FindRecord(dll));
        }

        [TestMethod]
        public void PlanRestore_BackupChangedSincePatching_IsBlocked()
        {
            string dll = PatchGame(PatchOptions.None);
            File.WriteAllBytes(dll + ".bak", PatchFixture.OriginalDll(PeFixture.MachineX64, 5));

            PatchPlan plan = PlanRestore();

            Assert.IsFalse(plan.CanRun);
            StringAssert.Contains(plan.Blockers[0], "has changed since");
        }

        [TestMethod]
        public void PlanRestore_BackupMissingAndDllStillOpenComposite_IsBlocked()
        {
            string dll = PatchGame(PatchOptions.None);
            File.Delete(dll + ".bak");

            PatchPlan plan = PlanRestore();

            Assert.IsFalse(plan.CanRun);
            StringAssert.Contains(plan.Blockers[0], "openvr_api.dll.bak is missing");
        }

        [TestMethod]
        public void PlanRestore_SteamAlreadyPutTheOriginalBack_OnlyRemovesTheBackup()
        {
            string dll = PatchGame(PatchOptions.None);
            File.WriteAllBytes(dll, original);

            PatchPlan plan = PlanRestore();
            ApplyResult result = fx.Service.Apply(plan, false, false);

            Assert.IsFalse(plan.AllActions.Any(a => a.Kind == PatchActionKind.RestoreFile));
            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(dll));
            Assert.IsFalse(File.Exists(dll + ".bak"));
            Assert.IsNull(fx.FindRecord(dll));
        }

        [TestMethod]
        public void PlanRestore_DllAndBackupGone_RemovesTheRecordOnly()
        {
            string dll = PatchGame(PatchOptions.None);
            File.Delete(dll);
            File.Delete(dll + ".bak");

            PatchPlan plan = PlanRestore();
            ApplyResult result = fx.Service.Apply(plan, false, false);

            Assert.IsTrue(plan.CanRun);
            Assert.IsTrue(plan.HasWork);
            Assert.AreEqual(0, plan.AllActions.Count);
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("only the patch record is removed")));
            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
            Assert.IsNull(fx.FindRecord(dll));
        }

        [TestMethod]
        public void PlanRestore_DllMissingButBackupThere_PutsTheOriginalBack()
        {
            string dll = PatchGame(PatchOptions.None);
            File.Delete(dll);

            ApplyResult result = fx.Service.Apply(PlanRestore(), false, false);

            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(dll));
            Assert.IsFalse(File.Exists(dll + ".bak"));
        }

        [TestMethod]
        public void PlanRestore_NothingRecorded_IsBlocked()
        {
            fx.WriteGameFile("openvr_api.dll", original);

            PatchPlan plan = PlanRestore();

            Assert.IsFalse(plan.CanRun);
            StringAssert.Contains(plan.Blockers[0], "no record");
        }

        [TestMethod]
        public void PlanRestore_UnityGame_RestoresBothDlls()
        {
            fx.CacheOpenComposite(OpenCompositeArch.X86, 1);
            byte[] original86 = PatchFixture.OriginalDll(PeFixture.MachineX86, 1);
            string x86 = fx.WriteGameFile(@"Game_Data\Plugins\x86\openvr_api.dll", original86);
            string x64 = fx.WriteGameFile(@"Game_Data\Plugins\x86_64\openvr_api.dll", original);
            PatchScannedGame(PatchOptions.None);

            ApplyResult result = fx.Service.Apply(PlanRestore(), false, false);

            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
            CollectionAssert.AreEqual(original86, File.ReadAllBytes(x86));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(x64));
            Assert.AreEqual(0, fx.StateStore.Load(new List<string>()).Records.Count);
        }

        [TestMethod]
        public void PlanRestore_BlockedGame_IsStillAllowed()
        {
            string dll = PatchGame(PatchOptions.None);

            PatchPlan plan = fx.Planner.PlanRestore(fx.ScanWith(CompatibilityVerdict.For(null, new[] { "EasyAntiCheat" }, new string[0])), new List<string>());
            ApplyResult result = fx.Service.Apply(plan, false, false);

            Assert.IsTrue(plan.CanRun, string.Join("\n", plan.Blockers));
            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(dll));
        }
    

        /// <summary>
        /// The executor's pre-flight only checks the leading VerifyHash steps, so each restore change must open with a
        /// VerifyHash of every existing file it reads or replaces (the DLL, then the .bak when it exists).
        /// </summary>
        private static void AssertLeadingVerifies(DllChange change, params string[] expectedTargets)
        {
            string[] leading = change.Actions.TakeWhile(a => a.Kind == PatchActionKind.VerifyHash).Select(a => a.Target).ToArray();
            CollectionAssert.AreEqual(expectedTargets, leading);
        }

        /// <summary>
        /// The backup is deleted only after the DLL is proven to be the recorded original: the last VerifyHash of the DLL
        /// before the Delete of the .bak expects the original hash.
        /// </summary>
        private static void AssertBakDeletedAfterOriginalVerified(DllChange change, string dll, string original)
        {
            PatchAction[] a = change.Actions.ToArray();
            int delete = Array.FindIndex(a, x => x.Kind == PatchActionKind.DeleteFile && x.Target == dll + ".bak");
            Assert.IsTrue(delete > 0, "no Delete of the .bak");
            PatchAction lastVerify = a.Take(delete).Last(x => x.Kind == PatchActionKind.VerifyHash && x.Target == dll);
            Assert.AreEqual(original, lastVerify.ExpectedSha256);
            int restore = Array.FindIndex(a, x => x.Kind == PatchActionKind.RestoreFile);
            if (restore >= 0)
            {
                Assert.IsTrue(restore < Array.LastIndexOf(a.Take(delete).ToArray(), lastVerify));
            }
        }

        [TestMethod]
        public void PlanRestore_Steps_VerifyDllAndBakFirst()
        {
            string dll = PatchGame(new PatchOptions(true, 1.25));
            string originalHash = FileHash.Sha256(dll + ".bak");

            DllChange change = PlanRestore().Changes.Single();

            AssertLeadingVerifies(change, dll, dll + ".bak");
            AssertBakDeletedAfterOriginalVerified(change, dll, originalHash);
            Assert.AreEqual(PatchActionKind.DeleteFile, change.Actions.Last().Kind);
        }

        [TestMethod]
        public void PlanRestore_DllMissing_VerifiesTheBakFirst()
        {
            string dll = PatchGame(PatchOptions.None);
            File.Delete(dll);

            DllChange change = PlanRestore().Changes.Single();

            AssertLeadingVerifies(change, dll + ".bak");
            AssertBakDeletedAfterOriginalVerified(change, dll, FileHash.Sha256(dll + ".bak"));
        }

        [TestMethod]
        public void PlanRestore_UnknownDll_ChecksItBeforeKeepingItAside()
        {
            string dll = PatchGame(PatchOptions.None);
            File.WriteAllBytes(dll, PatchFixture.OriginalDll(PeFixture.MachineX64, 3));

            DllChange change = PlanRestore().Changes.Single();

            AssertLeadingVerifies(change, dll, dll + ".bak");
            PatchAction[] a = change.Actions.ToArray();
            int backup = Array.FindIndex(a, x => x.Kind == PatchActionKind.BackupFile);
            int restore = Array.FindIndex(a, x => x.Kind == PatchActionKind.RestoreFile);
            Assert.IsTrue(backup >= 2 && backup < restore, "the unknown DLL must be kept aside before it is replaced");
            Assert.AreEqual(PatchActionKind.VerifyHash, a[backup + 1].Kind);
            Assert.AreEqual(a[backup].Target, a[backup + 1].Target);
        }

        [TestMethod]
        public void PlanRestore_IniOnlyChange_StillVerifiesTheDllFirst()
        {
            string dll = PatchGame(new PatchOptions(true, 1.25));
            File.WriteAllBytes(dll, original);
            File.Delete(dll + ".bak");

            DllChange change = PlanRestore().Changes.Single();

            AssertLeadingVerifies(change, dll);
            Assert.IsTrue(change.Actions.Any(a => a.Kind == PatchActionKind.DeleteFile && a.Target.EndsWith("opencomposite.ini")));
        }

        [TestMethod]
        public void PlanRestore_DllChangedAfterPlanning_NothingIsWritten()
        {
            string dll = PatchGame(PatchOptions.None);
            PatchPlan plan = PlanRestore();
            File.WriteAllBytes(dll, PatchFixture.OriginalDll(PeFixture.MachineX64, 9));
            string before = fx.Snapshot();

            ApplyResult result = fx.Service.Apply(plan, true, false);

            Assert.AreNotEqual(ApplyOutcome.Succeeded, result.Outcome);
            Assert.AreEqual(before, fx.Snapshot());
            Assert.IsNotNull(fx.FindRecord(dll));
        }

        [TestMethod]
        public void PlanRestore_CurrentDllIsAnotherKnownOpenComposite_RestoresWithoutAQuestion()
        {
            // R36: any known OpenComposite build (here the cached one, not the recorded one) restores silently.
            string dll = PatchGame(PatchOptions.None);
            byte[] build2 = OpenCompositeFixture.Dll(PeFixture.MachineX64, 2);
            File.WriteAllBytes(fx.Cache.DllPath(OpenCompositeArch.X64), build2);
            File.WriteAllBytes(dll, build2);

            PatchPlan plan = PlanRestore();
            ApplyResult result = fx.Service.Apply(plan, false, false);

            Assert.IsFalse(plan.NeedsConfirmation);
            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(dll));
        }

        [TestMethod]
        public void PlanRestore_AsideNameTaken_UsesAnotherName()
        {
            string dll = PatchGame(PatchOptions.None);
            byte[] third = PatchFixture.OriginalDll(PeFixture.MachineX64, 3);
            File.WriteAllBytes(dll, third);
            byte[] earlier = Encoding.ASCII.GetBytes("kept earlier");
            string taken = fx.WriteGameFile("openvr_api.dll.replaced-20260929-101500", earlier);

            PatchPlan plan = PlanRestore();
            ApplyResult result = fx.Service.Apply(plan, true, false);

            StringAssert.Contains(plan.Confirmations[0], "openvr_api.dll.replaced-20260929-101500-2");
            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
            CollectionAssert.AreEqual(earlier, File.ReadAllBytes(taken));
            CollectionAssert.AreEqual(third, File.ReadAllBytes(taken + "-2"));
        }
    }
}
