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
using System.Security.AccessControl;
using System.Security.Principal;
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    [TestClass]
    public class PatchServiceTests
    {
        private PatchFixture fx;
        private string dll;
        private string cached;
        private string originalHash;
        private string ocHash;

        [TestInitialize]
        public void SetUp()
        {
            fx = new PatchFixture();
            dll = fx.WriteGameFile("openvr_api.dll", PatchFixture.OriginalDll(PeFixture.MachineX64, 1));
            originalHash = FileHash.Sha256(dll);
            ocHash = fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            cached = fx.Cache.DllPath(OpenCompositeArch.X64);
        }

        [TestCleanup]
        public void TearDown()
        {
            fx.Dispose();
        }

        private PatchRecord RecordFor(string dllPath, string openCompositeSha256)
        {
            return new PatchRecord(620980, "Beat Saber", fx.InstallDir, dllPath, OpenCompositeArch.X64, originalHash, openCompositeSha256, false, PatchFixture.Start);
        }

        private DllChange PatchSteps(string dllPath, PatchRecord record)
        {
            return new DllChange(dllPath, new[]
            {
                PatchAction.Verify(dllPath, originalHash),
                PatchAction.Backup(dllPath, dllPath + ".bak"),
                PatchAction.Verify(dllPath + ".bak", originalHash),
                PatchAction.Copy(cached, dllPath, ocHash),
                PatchAction.Verify(dllPath, ocHash),
            }, record, false);
        }

        private PatchPlan Plan(IReadOnlyList<string> confirmations, params DllChange[] changes)
        {
            return new PatchPlan(PatchOperation.Patch, fx.Game, changes, null, confirmations, null);
        }

        [TestMethod]
        public void Apply_Simulation_ChangesNothingOnDisk()
        {
            PatchPlan plan = Plan(null, PatchSteps(dll, RecordFor(dll, ocHash)));
            string before = fx.Snapshot();

            ApplyResult result = fx.Service.Apply(plan, true, true);

            Assert.AreEqual(ApplyOutcome.Simulated, result.Outcome);
            Assert.AreEqual(before, fx.Snapshot());
            Assert.IsFalse(File.Exists(fx.Paths.StateFile));
            StringAssert.Contains(result.Message, "Simulation");
            StringAssert.Contains(result.Message, "nothing was written into any game folder");
            StringAssert.Contains(result.Message, "Back up " + dll);
            Assert.IsFalse(result.ChangedSomething);
        }

        [TestMethod]
        public void Apply_NeedsConfirmationButNotConfirmed_ChangesNothing()
        {
            PatchPlan plan = Plan(new[] { "Sure?" }, PatchSteps(dll, RecordFor(dll, ocHash)));
            string before = fx.Snapshot();

            ApplyResult result = fx.Service.Apply(plan, false, false);

            Assert.AreEqual(ApplyOutcome.NotRun, result.Outcome);
            Assert.AreEqual(before, fx.Snapshot());
        }

        [TestMethod]
        public void Apply_PlanWithBlockers_ChangesNothing()
        {
            var plan = new PatchPlan(PatchOperation.Patch, fx.Game, new[] { PatchSteps(dll, RecordFor(dll, ocHash)) }, new[] { "No OpenComposite x86." }, null, null);
            string before = fx.Snapshot();

            ApplyResult result = fx.Service.Apply(plan, true, false);

            Assert.AreEqual(ApplyOutcome.NotRun, result.Outcome);
            StringAssert.Contains(result.Message, "No OpenComposite x86.");
            Assert.AreEqual(before, fx.Snapshot());
        }

        [TestMethod]
        public void Apply_Succeeded_RecordsTheHashOfTheWrittenDll()
        {
            // R30: the plan carries a wrong OpenComposite hash on purpose (as if cache.json lagged behind).
            PatchPlan plan = Plan(null, PatchSteps(dll, RecordFor(dll, new string('0', 64))));
            fx.Now = PatchFixture.Start.AddMinutes(5);

            ApplyResult result = fx.Service.Apply(plan, false, false);

            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
            PatchRecord record = fx.FindRecord(dll);
            Assert.AreEqual(FileHash.Sha256(dll), record.OpenCompositeSha256);
            Assert.AreEqual(ocHash, record.OpenCompositeSha256);
            Assert.AreEqual(originalHash, record.OriginalSha256);
            Assert.AreEqual(PatchFixture.Start.AddMinutes(5), record.PatchedUtc);
            Assert.IsTrue(result.ChangedSomething);
        }

        [TestMethod]
        public void Apply_SecondDllFails_RecordsOnlyTheFirst()
        {
            string dll2 = fx.WriteGameFile(@"x86\openvr_api.dll", PatchFixture.OriginalDll(PeFixture.MachineX86, 1));
            // A copy whose source is not the planned file fails while running, after the first DLL is done.
            var failing = new DllChange(dll2, new[] { PatchAction.Copy(cached, dll2, new string('0', 64)) }, RecordFor(dll2, ocHash), false);
            PatchPlan plan = Plan(null, PatchSteps(dll, RecordFor(dll, ocHash)), failing);

            ApplyResult result = fx.Service.Apply(plan, false, false);

            Assert.AreEqual(ApplyOutcome.Failed, result.Outcome);
            Assert.IsNotNull(fx.FindRecord(dll));
            Assert.IsNull(fx.FindRecord(dll2));
            StringAssert.Contains(result.Message, "Done before the stop");
            StringAssert.Contains(result.Message, "Restore");
        }

        [TestMethod]
        public void Apply_StateFileUnreadable_RefusesBeforeTouchingFiles()
        {
            File.WriteAllText(fx.Paths.StateFile, "{ \"version\": 1, \"records\": [] }");
            PatchPlan plan = Plan(null, PatchSteps(dll, RecordFor(dll, ocHash)));

            using (new FileStream(fx.Paths.StateFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                ApplyResult result = fx.Service.Apply(plan, false, false);

                Assert.AreEqual(ApplyOutcome.NotRun, result.Outcome);
                StringAssert.Contains(result.Message, "could not be read");
            }

            Assert.AreEqual(originalHash, FileHash.Sha256(dll));
            Assert.IsFalse(File.Exists(dll + ".bak"));
        }

        [TestMethod]
        public void Apply_StateCannotBeSaved_RefusesBeforeTouchingFiles()
        {
            // A state.json that is not valid JSON and cannot be set aside makes CanSave false.
            File.WriteAllText(fx.Paths.StateFile, "not json");
            Directory.CreateDirectory(fx.Paths.StateFile + ".corrupt-" + PatchFixture.Start.ToString("yyyyMMdd-HHmmss"));
            PatchPlan plan = Plan(null, PatchSteps(dll, RecordFor(dll, ocHash)));

            ApplyResult result = fx.Service.Apply(plan, false, false);

            Assert.AreEqual(ApplyOutcome.NotRun, result.Outcome);
            StringAssert.Contains(result.Message, "nothing was changed");
            Assert.AreEqual(originalHash, FileHash.Sha256(dll));
            Assert.IsFalse(File.Exists(dll + ".bak"));
        }

        [TestMethod]
        public void Apply_SteamUpdatedDllAfterPlanning_NothingIsWritten()
        {
            PatchPlan plan = Plan(null, PatchSteps(dll, RecordFor(dll, ocHash)));
            File.WriteAllBytes(dll, PatchFixture.OriginalDll(PeFixture.MachineX64, 2));
            string before = fx.Snapshot();

            ApplyResult result = fx.Service.Apply(plan, false, false);

            Assert.AreEqual(ApplyOutcome.Failed, result.Outcome);
            StringAssert.Contains(result.Message, "Nothing was changed");
            Assert.AreEqual(before, fx.Snapshot());
            Assert.IsNull(fx.FindRecord(dll));
        }

        [TestMethod]
        public void Apply_DllDeletedAfterPlanning_NothingIsWritten()
        {
            PatchPlan plan = Plan(null, PatchSteps(dll, RecordFor(dll, ocHash)));
            File.Delete(dll);
            string before = fx.Snapshot();

            ApplyResult result = fx.Service.Apply(plan, false, false);

            Assert.AreEqual(ApplyOutcome.Failed, result.Outcome);
            StringAssert.Contains(result.Message, "is missing");
            Assert.AreEqual(before, fx.Snapshot());
            Assert.IsFalse(File.Exists(dll + ".bak"));
            Assert.IsNull(fx.FindRecord(dll));
        }

        [TestMethod]
        public void Apply_IniWriteFailsAfterTheCopy_StillRecordsThePatchedDll()
        {
            string ini = fx.WriteGameFile("opencomposite.ini", System.Text.Encoding.ASCII.GetBytes("supersampleRatio=1.7\r\n"));
            PatchRecord planned = new PatchRecord(620980, "Beat Saber", fx.InstallDir, dll, OpenCompositeArch.X64, originalHash, new string('0', 64), false, PatchFixture.Start);
            DllChange patch = PatchSteps(dll, planned);
            var actions = new List<PatchAction>(patch.Actions) { PatchAction.WriteIni(ini, "supersampleRatio=1.0\r\n", false) };
            var change = new DllChange(dll, actions, planned, false);

            ApplyResult result = fx.Service.Apply(Plan(null, change), false, false);

            Assert.AreEqual(ApplyOutcome.Failed, result.Outcome);
            StringAssert.Contains(result.Message, "Restore");
            PatchRecord record = fx.FindRecord(dll);
            Assert.IsNotNull(record);
            Assert.AreEqual(FileHash.Sha256(dll), record.OpenCompositeSha256);
            Assert.AreEqual(ocHash, record.OpenCompositeSha256);
            Assert.IsFalse(record.IniCreated);
            Assert.AreEqual("supersampleRatio=1.7\r\n", File.ReadAllText(ini));
        }

        [TestMethod]
        public void Apply_BackupDeleteFailsAfterTheRestore_RemovesTheRecord()
        {
            string bak = dll + ".bak";
            File.Copy(dll, bak);
            File.Copy(cached, dll, true);
            PatchState state = fx.StateStore.Load(new List<string>());
            state.Put(RecordFor(dll, ocHash));
            fx.StateStore.Save(state);
            var change = new DllChange(dll, new[]
            {
                PatchAction.Verify(dll, ocHash),
                PatchAction.Verify(bak, originalHash),
                PatchAction.Restore(bak, dll, originalHash),
                PatchAction.Verify(dll, originalHash),
                PatchAction.Delete(bak, new string('0', 64)),
            }, null, true);
            var plan = new PatchPlan(PatchOperation.Restore, fx.Game, new[] { change }, null, null, null);

            ApplyResult result = fx.Service.Apply(plan, false, false);

            Assert.AreEqual(ApplyOutcome.Failed, result.Outcome);
            Assert.AreEqual(originalHash, FileHash.Sha256(dll));
            Assert.IsNull(fx.FindRecord(dll));
            Assert.IsTrue(File.Exists(bak));
        }

        [TestMethod]
        public void Apply_ExecutorThrowsAfterAFinishedDll_StillRecordsTheFinishedDll()
        {
            string dll2 = fx.WriteGameFile(@"x86\openvr_api.dll", PatchFixture.OriginalDll(PeFixture.MachineX86, 1));
            // A null source is a programming error, not a disk error: it must not lose the record of the first DLL.
            var broken = new DllChange(dll2, new[] { PatchAction.Copy(null, dll2, ocHash) }, RecordFor(dll2, ocHash), false);
            PatchPlan plan = Plan(null, PatchSteps(dll, RecordFor(dll, ocHash)), broken);

            Assert.ThrowsException<ArgumentNullException>(() => fx.Service.Apply(plan, false, false));

            Assert.IsNotNull(fx.FindRecord(dll));
            Assert.IsNull(fx.FindRecord(dll2));
            Assert.AreEqual(ocHash, FileHash.Sha256(dll));
        }

        [TestMethod]
        public void Apply_FolderDeniesCreatingFiles_ReportsNeedsElevation()
        {
            PatchPlan plan = Plan(null, PatchSteps(dll, RecordFor(dll, ocHash)));
            var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User, FileSystemRights.CreateFiles, AccessControlType.Deny);
            DirectorySecurity security = Directory.GetAccessControl(fx.InstallDir);
            security.AddAccessRule(rule);
            Directory.SetAccessControl(fx.InstallDir, security);
            try
            {
                string probe = Path.Combine(fx.InstallDir, "probe.txt");
                try
                {
                    File.WriteAllText(probe, "x");
                    File.Delete(probe);
                    Assert.Inconclusive("A deny ACL did not block creating files for this account.");
                }
                catch (UnauthorizedAccessException)
                {
                }

                ApplyResult result = fx.Service.Apply(plan, false, false);

                Assert.IsTrue(result.NeedsElevation);
                Assert.AreNotEqual(ApplyOutcome.Succeeded, result.Outcome);
                Assert.AreEqual(originalHash, FileHash.Sha256(dll));
            }
            finally
            {
                security.RemoveAccessRule(rule);
                Directory.SetAccessControl(fx.InstallDir, security);
            }
        }

        [TestMethod]
        public void Apply_ChangeThatRemovesTheRecord_RemovesIt()
        {
            PatchState state = fx.StateStore.Load(new List<string>());
            state.Put(RecordFor(dll, ocHash));
            fx.StateStore.Save(state);
            var plan = new PatchPlan(PatchOperation.Restore, fx.Game, new[] { new DllChange(dll, new PatchAction[0], null, true) }, null, null, null);

            ApplyResult result = fx.Service.Apply(plan, false, false);

            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
            Assert.IsNull(fx.FindRecord(dll));
        }
    }
}
