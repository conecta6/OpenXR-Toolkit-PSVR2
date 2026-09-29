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
    public class PatchPlannerTests
    {
        private PatchFixture fx;

        [TestInitialize]
        public void SetUp()
        {
            fx = new PatchFixture();
        }

        [TestCleanup]
        public void TearDown()
        {
            fx.Dispose();
        }

        private PatchPlan PlanPatch(GameEntry entry, PatchOptions options)
        {
            return fx.Planner.PlanPatch(entry, options, new List<string>());
        }

        private static PatchActionKind[] Kinds(PatchPlan plan)
        {
            return plan.AllActions.Select(a => a.Kind).ToArray();
        }

        private string WriteOriginalX64()
        {
            return fx.WriteGameFile("openvr_api.dll", PatchFixture.OriginalDll(PeFixture.MachineX64, 1));
        }

        [TestMethod]
        public void PlanPatch_OriginalX64Dll_BacksUpCopiesAndRecords()
        {
            byte[] original = PatchFixture.OriginalDll(PeFixture.MachineX64, 1);
            string dll = fx.WriteGameFile("openvr_api.dll", original);
            string oc = fx.CacheOpenComposite(OpenCompositeArch.X64, 1);

            PatchPlan plan = PlanPatch(fx.Scan(), PatchOptions.None);

            Assert.IsTrue(plan.CanRun, string.Join("\n", plan.Blockers));
            Assert.IsFalse(plan.NeedsConfirmation);
            CollectionAssert.AreEqual(
                new[] { PatchActionKind.VerifyHash, PatchActionKind.BackupFile, PatchActionKind.VerifyHash, PatchActionKind.CopyFile, PatchActionKind.VerifyHash },
                Kinds(plan));
            ApplyResult result = fx.Service.Apply(plan, false, false);
            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
            Assert.AreEqual(oc, FileHash.Sha256(dll));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(dll + ".bak"));
            PatchRecord record = fx.FindRecord(dll);
            Assert.AreEqual(620980, record.AppId);
            Assert.AreEqual(OpenCompositeArch.X64, record.Arch);
            Assert.AreEqual(FileHash.Sha256(dll + ".bak"), record.OriginalSha256);
            Assert.AreEqual(oc, record.OpenCompositeSha256);
            Assert.IsFalse(record.IniCreated);
        }

        [TestMethod]
        public void PlanPatch_UnityGameWithX86AndX64_PatchesEachWithItsArchitecture()
        {
            string x86 = fx.WriteGameFile(@"Game_Data\Plugins\x86\openvr_api.dll", PatchFixture.OriginalDll(PeFixture.MachineX86, 1));
            string x64 = fx.WriteGameFile(@"Game_Data\Plugins\x86_64\openvr_api.dll", PatchFixture.OriginalDll(PeFixture.MachineX64, 1));
            string oc64 = fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            string oc86 = fx.CacheOpenComposite(OpenCompositeArch.X86, 1);

            ApplyResult result = fx.Service.Apply(PlanPatch(fx.Scan(), PatchOptions.None), false, false);

            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
            Assert.AreEqual(oc86, FileHash.Sha256(x86));
            Assert.AreEqual(oc64, FileHash.Sha256(x64));
            Assert.AreEqual(OpenCompositeArch.X86, fx.FindRecord(x86).Arch);
            Assert.AreEqual(OpenCompositeArch.X64, fx.FindRecord(x64).Arch);
        }

        [TestMethod]
        public void PlanPatch_OneArchitectureNotCached_BlocksWholeGameAndWritesNothing()
        {
            fx.WriteGameFile(@"Game_Data\Plugins\x86\openvr_api.dll", PatchFixture.OriginalDll(PeFixture.MachineX86, 1));
            fx.WriteGameFile(@"Game_Data\Plugins\x86_64\openvr_api.dll", PatchFixture.OriginalDll(PeFixture.MachineX64, 1));
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            string before = fx.Snapshot();

            PatchPlan plan = PlanPatch(fx.Scan(), PatchOptions.None);
            ApplyResult result = fx.Service.Apply(plan, true, false);

            Assert.IsFalse(plan.CanRun);
            Assert.AreEqual(1, plan.Blockers.Count);
            StringAssert.Contains(plan.Blockers[0], "needs OpenComposite x86, which is not downloaded");
            Assert.AreEqual(0, plan.Changes.Count);
            Assert.AreEqual(ApplyOutcome.NotRun, result.Outcome);
            Assert.AreEqual(before, fx.Snapshot());
        }

        [TestMethod]
        [DataRow(0xAA64, "an ARM64 DLL")]
        [DataRow(0x1234, "not a DLL Game Manager can read")]
        public void PlanPatch_UnsupportedArchitecture_IsBlocked(int machine, string expected)
        {
            fx.WriteGameFile("openvr_api.dll", PatchFixture.OriginalDll((ushort)machine, 1));
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);

            PatchPlan plan = PlanPatch(fx.Scan(), PatchOptions.None);

            Assert.IsFalse(plan.CanRun);
            StringAssert.Contains(plan.Blockers[0], expected);
            StringAssert.Contains(plan.Blockers[0], "cannot be patched");
        }

        [TestMethod]
        public void PlanPatch_BackupEqualToCurrentDll_IsReusedNotRewritten()
        {
            string dll = WriteOriginalX64();
            File.Copy(dll, dll + ".bak");
            var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(dll + ".bak", old);
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);

            PatchPlan plan = PlanPatch(fx.Scan(), PatchOptions.None);
            ApplyResult result = fx.Service.Apply(plan, false, false);

            Assert.IsFalse(plan.NeedsConfirmation);
            Assert.IsFalse(plan.AllActions.Any(a => a.Kind == PatchActionKind.BackupFile));
            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
            Assert.AreEqual(old, File.GetLastWriteTimeUtc(dll + ".bak"));
            Assert.AreEqual(FileHash.Sha256(dll + ".bak"), fx.FindRecord(dll).OriginalSha256);
        }

        [TestMethod]
        public void PlanPatch_BakFromManualInstallDiffers_AsksAndKeepsOldBackup()
        {
            byte[] current = PatchFixture.OriginalDll(PeFixture.MachineX64, 1);
            byte[] olderBackup = PatchFixture.OriginalDll(PeFixture.MachineX64, 7);
            string dll = fx.WriteGameFile("openvr_api.dll", current);
            fx.WriteGameFile("openvr_api.dll.bak", olderBackup);
            string oc = fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            string aside = dll + ".bak.old-20260929-101500";

            PatchPlan plan = PlanPatch(fx.Scan(), PatchOptions.None);

            Assert.IsTrue(plan.CanRun, string.Join("\n", plan.Blockers));
            Assert.AreEqual(1, plan.Confirmations.Count);
            StringAssert.Contains(plan.Confirmations[0], "openvr_api.dll.bak.old-20260929-101500");
            string before = fx.Snapshot();
            Assert.AreEqual(ApplyOutcome.NotRun, fx.Service.Apply(plan, false, false).Outcome);
            Assert.AreEqual(before, fx.Snapshot(), "No (the default) must change nothing");

            ApplyResult result = fx.Service.Apply(plan, true, false);

            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
            CollectionAssert.AreEqual(olderBackup, File.ReadAllBytes(aside));
            CollectionAssert.AreEqual(current, File.ReadAllBytes(dll + ".bak"));
            Assert.AreEqual(oc, FileHash.Sha256(dll));
        }

        [TestMethod]
        public void PlanPatch_ManualInstallAlreadyOnCachedBuild_AdoptsExistingBackup()
        {
            byte[] original = PatchFixture.OriginalDll(PeFixture.MachineX64, 1);
            string oc = fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            string dll = fx.WriteGameFile("openvr_api.dll", File.ReadAllBytes(fx.Cache.DllPath(OpenCompositeArch.X64)));
            fx.WriteGameFile("openvr_api.dll.bak", original);

            PatchPlan plan = PlanPatch(fx.Scan(), PatchOptions.None);
            ApplyResult result = fx.Service.Apply(plan, false, false);

            Assert.IsFalse(plan.NeedsConfirmation);
            Assert.IsTrue(plan.AllActions.All(a => a.Kind == PatchActionKind.VerifyHash));
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("kept as the game's original")));
            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
            PatchRecord record = fx.FindRecord(dll);
            Assert.AreEqual(FileHash.Sha256(dll + ".bak"), record.OriginalSha256);
            Assert.AreEqual(oc, record.OpenCompositeSha256);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(dll + ".bak"));
        }

        [TestMethod]
        public void PlanPatch_OpenCompositeWithoutBackup_IsBlocked()
        {
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            fx.WriteGameFile("openvr_api.dll", File.ReadAllBytes(fx.Cache.DllPath(OpenCompositeArch.X64)));

            PatchPlan plan = PlanPatch(fx.Scan(), PatchOptions.None);

            Assert.IsFalse(plan.CanRun);
            StringAssert.Contains(plan.Blockers[0], "no openvr_api.dll.bak");
        }

        [TestMethod]
        public void PlanPatch_BackupIsItselfOpenComposite_IsBlocked()
        {
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            byte[] oc = File.ReadAllBytes(fx.Cache.DllPath(OpenCompositeArch.X64));
            fx.WriteGameFile("openvr_api.dll", oc);
            fx.WriteGameFile("openvr_api.dll.bak", oc);

            PatchPlan plan = PlanPatch(fx.Scan(), PatchOptions.None);

            Assert.IsFalse(plan.CanRun);
            StringAssert.Contains(plan.Blockers[0], "is itself an OpenComposite DLL");
        }

        [TestMethod]
        public void PlanPatch_BlockedGame_IsBlocked()
        {
            WriteOriginalX64();
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);

            PatchPlan plan = PlanPatch(fx.ScanWith(CompatibilityVerdict.For(null, new[] { "EasyAntiCheat" }, new string[0])), PatchOptions.None);

            Assert.IsFalse(plan.CanRun);
            StringAssert.Contains(plan.Blockers[0], "never patched");
        }

        [TestMethod]
        public void PlanPatch_NotAnOpenVrGame_IsBlocked()
        {
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);

            PatchPlan plan = PlanPatch(fx.Scan(), PatchOptions.None);

            Assert.IsFalse(plan.CanRun);
            StringAssert.Contains(plan.Blockers[0], "No openvr_api.dll");
        }

        [TestMethod]
        public void PlanPatch_AntiCheatNotRuledOut_AsksNamingTheFolders()
        {
            WriteOriginalX64();
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            string link = Path.Combine(fx.InstallDir, "Link");

            PatchPlan plan = PlanPatch(fx.ScanWith(CompatibilityVerdict.For(null, new string[0], new[] { link })), PatchOptions.None);

            Assert.IsTrue(plan.CanRun);
            Assert.AreEqual(1, plan.Confirmations.Count);
            StringAssert.Contains(plan.Confirmations[0], link);
            StringAssert.Contains(plan.Confirmations[0], "banned");
            string before = fx.Snapshot();
            Assert.AreEqual(ApplyOutcome.NotRun, fx.Service.Apply(plan, false, false).Outcome);
            Assert.AreEqual(before, fx.Snapshot());
        }

        [TestMethod]
        public void PlanPatch_DllGoneSinceScan_IsBlocked()
        {
            string dll = WriteOriginalX64();
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            GameEntry entry = fx.Scan();
            File.Delete(dll);

            PatchPlan plan = PlanPatch(entry, PatchOptions.None);

            Assert.IsFalse(plan.CanRun);
            StringAssert.Contains(plan.Blockers[0], "Click Refresh");
        }

        [TestMethod]
        public void Apply_SteamUpdatedDllAfterPlanning_NothingIsWritten()
        {
            string dll = WriteOriginalX64();
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            PatchPlan plan = PlanPatch(fx.Scan(), PatchOptions.None);
            byte[] updated = PatchFixture.OriginalDll(PeFixture.MachineX64, 2);
            File.WriteAllBytes(dll, updated);

            ApplyResult result = fx.Service.Apply(plan, false, false);

            Assert.AreEqual(ApplyOutcome.Failed, result.Outcome);
            StringAssert.Contains(result.Message, "has changed");
            StringAssert.Contains(result.Message, "Nothing was changed");
            Assert.IsFalse(File.Exists(dll + ".bak"));
            CollectionAssert.AreEqual(updated, File.ReadAllBytes(dll));
            Assert.IsNull(fx.FindRecord(dll));
        }

        [TestMethod]
        public void PlanPatch_CachedDllDamaged_IsBlocked()
        {
            WriteOriginalX64();
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            File.WriteAllBytes(fx.Cache.DllPath(OpenCompositeArch.X64), OpenCompositeFixture.HtmlPage());

            PatchPlan plan = PlanPatch(fx.Scan(), PatchOptions.None);

            Assert.IsFalse(plan.CanRun);
            StringAssert.Contains(plan.Blockers[0], "failed its check");
            StringAssert.Contains(plan.Blockers[0], "Download OpenComposite");
        }

        [TestMethod]
        public void PlanPatch_AlreadyPatchedWithCurrentBuild_HasNothingToDo()
        {
            WriteOriginalX64();
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            Assert.AreEqual(ApplyOutcome.Succeeded, fx.Service.Apply(PlanPatch(fx.Scan(), PatchOptions.None), false, false).Outcome);

            PatchPlan again = PlanPatch(fx.Scan(), PatchOptions.None);

            Assert.IsTrue(again.CanRun);
            Assert.IsFalse(again.HasWork);
            StringAssert.Contains(again.Notes[0], "already patched with the current OpenComposite build");
        }

        [TestMethod]
        public void PlanPatch_WriteIni_CreatesIniNextToTheDllAndRecordsIt()
        {
            string dll = fx.WriteGameFile(@"bin\openvr_api.dll", PatchFixture.OriginalDll(PeFixture.MachineX64, 1));
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);

            ApplyResult result = fx.Service.Apply(PlanPatch(fx.Scan(), new PatchOptions(true, 1.25)), false, false);

            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
            Assert.AreEqual("supersampleRatio=1.25\r\n", File.ReadAllText(Path.Combine(fx.InstallDir, @"bin\opencomposite.ini")));
            Assert.IsTrue(fx.FindRecord(dll).IniCreated);
        }

        [TestMethod]
        public void PlanPatch_IniNotCreatedByGameManager_IsLeftUnchanged()
        {
            string dll = WriteOriginalX64();
            string ini = fx.WriteGameFile("opencomposite.ini", Encoding.ASCII.GetBytes("supersampleRatio=1.7\r\n"));
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);

            PatchPlan plan = PlanPatch(fx.Scan(), new PatchOptions(true, 1.25));
            ApplyResult result = fx.Service.Apply(plan, false, false);

            Assert.IsTrue(plan.Notes.Any(n => n.Contains("left unchanged")));
            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
            Assert.AreEqual("supersampleRatio=1.7\r\n", File.ReadAllText(ini));
            Assert.IsFalse(fx.FindRecord(dll).IniCreated);
        }

        [TestMethod]
        public void Apply_GameDllHeldOpenByRunningGame_RefusedAndNothingWritten()
        {
            string dll = WriteOriginalX64();
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            PatchPlan plan = PlanPatch(fx.Scan(), PatchOptions.None);
            string before = fx.Snapshot();

            using (new FileStream(dll, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                ApplyResult result = fx.Service.Apply(plan, false, false);

                Assert.AreEqual(ApplyOutcome.Refused, result.Outcome);
                StringAssert.Contains(result.Message, "in use");
                Assert.IsFalse(result.NeedsElevation);
            }

            Assert.AreEqual(before, fx.Snapshot());
        }

        [TestMethod]
        public void Apply_ProcessRunningFromGameFolder_RefusedAndNothingWritten()
        {
            WriteOriginalX64();
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            PatchPlan plan = PlanPatch(fx.Scan(), PatchOptions.None);
            fx.Processes.Paths.Add(Path.Combine(fx.InstallDir, "Beat Saber.exe"));
            string before = fx.Snapshot();

            ApplyResult result = fx.Service.Apply(plan, false, false);

            Assert.AreEqual(ApplyOutcome.Refused, result.Outcome);
            StringAssert.Contains(result.Message, "running");
            Assert.AreEqual(before, fx.Snapshot());
        }

        /// <summary>
        /// The executor's pre-flight only checks the leading VerifyHash steps, so each change must open with a
        /// VerifyHash of every existing file it reads or replaces (expected: the DLL, then the .bak when it exists).
        /// </summary>
        private static void AssertLeadingVerifies(DllChange change, params string[] expectedTargets)
        {
            string[] leading = change.Actions.TakeWhile(a => a.Kind == PatchActionKind.VerifyHash).Select(a => a.Target).ToArray();
            CollectionAssert.AreEqual(expectedTargets, leading);
        }

        /// <summary>
        /// R17: an existing .bak is never a Backup, Copy, Restore or WriteIni target, unless the same change deleted
        /// it (after keeping it under another name) before that step.
        /// </summary>
        private static void AssertBakNeverOverwritten(DllChange change, string bak)
        {
            bool deleted = false;
            foreach (PatchAction action in change.Actions)
            {
                if (action.Kind == PatchActionKind.DeleteFile && action.Target == bak)
                {
                    deleted = true;
                }
                bool writes = action.Kind == PatchActionKind.BackupFile || action.Kind == PatchActionKind.CopyFile
                    || action.Kind == PatchActionKind.RestoreFile || action.Kind == PatchActionKind.WriteIni;
                if (writes && action.Target == bak)
                {
                    Assert.IsTrue(deleted, "step writes over an existing .bak: " + action.Description);
                }
            }
        }

        [TestMethod]
        public void PlanPatch_FreshPatch_StepsAreVerifyBackupVerifyBakCopyVerify()
        {
            string dll = WriteOriginalX64();
            string oc = fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            string hash = FileHash.Sha256(dll);

            PatchPlan plan = PlanPatch(fx.Scan(), new PatchOptions(true, 1.25));

            DllChange change = plan.Changes.Single();
            AssertLeadingVerifies(change, dll);
            PatchAction[] a = change.Actions.ToArray();
            Assert.AreEqual(6, a.Length);
            Assert.AreEqual(PatchActionKind.BackupFile, a[1].Kind);
            Assert.AreEqual(dll, a[1].Source);
            Assert.AreEqual(dll + ".bak", a[1].Target);
            Assert.AreEqual(PatchActionKind.VerifyHash, a[2].Kind);
            Assert.AreEqual(dll + ".bak", a[2].Target);
            Assert.AreEqual(hash, a[2].ExpectedSha256);
            Assert.AreEqual(PatchActionKind.CopyFile, a[3].Kind);
            Assert.AreEqual(dll, a[3].Target);
            Assert.AreEqual(oc, a[3].ExpectedSha256);
            Assert.AreEqual(PatchActionKind.VerifyHash, a[4].Kind);
            Assert.AreEqual(dll, a[4].Target);
            Assert.AreEqual(PatchActionKind.WriteIni, a[5].Kind);
        }

        [TestMethod]
        public void PlanPatch_ExistingBackupCases_VerifyDllAndBakFirstAndNeverOverwriteTheBak()
        {
            // Same backup, differing backup and adopted backup, each in its own game folder state.
            string dll = WriteOriginalX64();
            File.Copy(dll, dll + ".bak");
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            DllChange reused = PlanPatch(fx.Scan(), PatchOptions.None).Changes.Single();
            AssertLeadingVerifies(reused, dll, dll + ".bak");
            AssertBakNeverOverwritten(reused, dll + ".bak");

            File.WriteAllBytes(dll + ".bak", PatchFixture.OriginalDll(PeFixture.MachineX64, 7));
            DllChange differing = PlanPatch(fx.Scan(), PatchOptions.None).Changes.Single();
            AssertLeadingVerifies(differing, dll, dll + ".bak");
            AssertBakNeverOverwritten(differing, dll + ".bak");

            File.WriteAllBytes(dll, File.ReadAllBytes(fx.Cache.DllPath(OpenCompositeArch.X64)));
            File.WriteAllBytes(dll + ".bak", PatchFixture.OriginalDll(PeFixture.MachineX64, 1));
            PatchPlan adopted = PlanPatch(fx.Scan(), new PatchOptions(true, 1.0));
            AssertLeadingVerifies(adopted.Changes.Single(), dll, dll + ".bak");
            AssertBakNeverOverwritten(adopted.Changes.Single(), dll + ".bak");
        }

        [TestMethod]
        public void PlanPatch_DifferingBakChanged_AfterPlanning_NothingIsWritten()
        {
            string dll = WriteOriginalX64();
            string bak = fx.WriteGameFile("openvr_api.dll.bak", PatchFixture.OriginalDll(PeFixture.MachineX64, 7));
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            PatchPlan plan = PlanPatch(fx.Scan(), PatchOptions.None);
            File.WriteAllBytes(bak, PatchFixture.OriginalDll(PeFixture.MachineX64, 8));
            string before = fx.Snapshot();

            ApplyResult result = fx.Service.Apply(plan, true, false);

            Assert.AreEqual(ApplyOutcome.Failed, result.Outcome);
            Assert.AreEqual(before, fx.Snapshot());
        }

        [TestMethod]
        public void PlanPatch_BlockedByAnyOfSeveralDlls_KeepsNoChange()
        {
            fx.WriteGameFile(@"x\openvr_api.dll", PatchFixture.OriginalDll(PeFixture.MachineX64, 1));
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            fx.WriteGameFile(@"y\openvr_api.dll", PatchFixture.OriginalDll(0xAA64, 1));

            PatchPlan plan = PlanPatch(fx.Scan(), PatchOptions.None);

            Assert.IsFalse(plan.CanRun);
            Assert.AreEqual(1, plan.Blockers.Count);
            StringAssert.Contains(plan.Blockers[0], "an ARM64 DLL");
            StringAssert.Contains(plan.Blockers[0], "cannot be patched");
            Assert.AreEqual(0, plan.Changes.Count);
        }

        [TestMethod]
        public void PlanPatch_AlreadyOnCurrentBuildWithIni_VerifiesTheDllBeforeWritingTheIni()
        {
            string dll = WriteOriginalX64();
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            Assert.AreEqual(ApplyOutcome.Succeeded, fx.Service.Apply(PlanPatch(fx.Scan(), PatchOptions.None), false, false).Outcome);

            PatchPlan again = PlanPatch(fx.Scan(), new PatchOptions(true, 1.25));

            DllChange change = again.Changes.Single();
            AssertLeadingVerifies(change, dll);
            Assert.AreEqual(PatchActionKind.WriteIni, change.Actions.Last().Kind);
            Assert.AreEqual(2, change.Actions.Count);
            // Steam puts a different game DLL back: nothing is written and nothing is recorded as OpenComposite.
            File.WriteAllBytes(dll, PatchFixture.OriginalDll(PeFixture.MachineX64, 5));
            string before = fx.Snapshot();
            Assert.AreEqual(ApplyOutcome.Failed, fx.Service.Apply(again, false, false).Outcome);
            Assert.AreEqual(before, fx.Snapshot());
        }

        [TestMethod]
        public void PlanPatch_AsideNameAlreadyTaken_UsesAnotherName()
        {
            string dll = WriteOriginalX64();
            fx.WriteGameFile("openvr_api.dll.bak", PatchFixture.OriginalDll(PeFixture.MachineX64, 7));
            fx.WriteGameFile("openvr_api.dll.bak.old-20260929-101500", PatchFixture.OriginalDll(PeFixture.MachineX64, 6));
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);

            PatchPlan plan = PlanPatch(fx.Scan(), PatchOptions.None);
            ApplyResult result = fx.Service.Apply(plan, true, false);

            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
            Assert.IsTrue(File.Exists(dll + ".bak.old-20260929-101500-2"));
        }
    }
}
