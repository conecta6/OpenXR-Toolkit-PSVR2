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
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    [TestClass]
    public class PatchExecutorTests
    {
        private PatchFixture fx;
        private PatchExecutor executor;
        private string dll;
        private string bak;
        private string cached;
        private string originalHash;
        private string ocHash;

        [TestInitialize]
        public void SetUp()
        {
            fx = new PatchFixture();
            executor = new PatchExecutor(fx.Guard);
            dll = fx.WriteGameFile("openvr_api.dll", PatchFixture.OriginalDll(PeFixture.MachineX64, 1));
            bak = dll + ".bak";
            originalHash = FileHash.Sha256(dll);
            ocHash = fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
            cached = fx.Cache.DllPath(OpenCompositeArch.X64);
        }

        [TestCleanup]
        public void TearDown()
        {
            fx.Dispose();
        }

        private PatchPlan Plan(params DllChange[] changes)
        {
            return new PatchPlan(PatchOperation.Patch, fx.Game, changes, null, null, null);
        }

        private DllChange PatchSteps(string dllPath, string original)
        {
            return new DllChange(dllPath, new[]
            {
                PatchAction.Verify(dllPath, original),
                PatchAction.Backup(dllPath, dllPath + ".bak"),
                PatchAction.Verify(dllPath + ".bak", original),
                PatchAction.Copy(cached, dllPath, ocHash),
                PatchAction.Verify(dllPath, ocHash),
            }, null, false);
        }

        private string[] TemporaryFiles()
        {
            return Directory.GetFiles(fx.InstallDir, "*.tmp", SearchOption.AllDirectories);
        }

        [TestMethod]
        public void Execute_BackupThenCopy_PatchesAndKeepsAVerifiedBackup()
        {
            ExecutionResult result = executor.Execute(Plan(PatchSteps(dll, originalHash)));

            Assert.IsTrue(result.Succeeded, result.Error);
            Assert.AreEqual(1, result.CompletedChanges);
            Assert.AreEqual(5, result.CompletedActions.Count);
            Assert.AreEqual(ocHash, FileHash.Sha256(dll));
            Assert.AreEqual(originalHash, FileHash.Sha256(bak));
            Assert.AreEqual(0, TemporaryFiles().Length);
        }

        [TestMethod]
        public void Execute_BackupTargetAlreadyExists_StopsWithoutOverwritingIt()
        {
            File.WriteAllBytes(bak, PatchFixture.OriginalDll(PeFixture.MachineX64, 9));
            string before = FileHash.Sha256(bak);

            ExecutionResult result = executor.Execute(Plan(PatchSteps(dll, originalHash)));

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(PatchActionKind.BackupFile, result.FailedAction.Kind);
            StringAssert.Contains(result.Error, "already exists");
            Assert.AreEqual(before, FileHash.Sha256(bak));
            Assert.AreEqual(originalHash, FileHash.Sha256(dll));
        }

        [TestMethod]
        public void Execute_DllChangedSinceThePlan_StopsBeforeWritingAnything()
        {
            File.WriteAllBytes(dll, PatchFixture.OriginalDll(PeFixture.MachineX64, 2));
            string updated = FileHash.Sha256(dll);

            ExecutionResult result = executor.Execute(Plan(PatchSteps(dll, originalHash)));

            Assert.AreEqual(PatchActionKind.VerifyHash, result.FailedAction.Kind);
            Assert.AreEqual(0, result.CompletedActions.Count);
            StringAssert.Contains(result.Error, "has changed");
            Assert.IsFalse(File.Exists(bak));
            Assert.AreEqual(updated, FileHash.Sha256(dll));
        }

        [TestMethod]
        public void Execute_BackupCopyIsNotTheSource_LeavesNoBackupAndNoTemporaryFile()
        {
            string bad = Path.Combine(fx.InstallDir, "nothing.dll");

            var change = new DllChange(dll, new[] { PatchAction.Backup(bad, bak) }, null, false);
            ExecutionResult result = executor.Execute(Plan(change));

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(File.Exists(bak));
            Assert.AreEqual(0, TemporaryFiles().Length);
        }

        [TestMethod]
        public void Execute_SourceChangesWhileBeingBackedUp_LeavesNoBackupAndNoTemporaryFile()
        {
            try
            {
                // The source changes after the copy was made and before the hashes are compared.
                PatchExecutor.AfterBackupCopy = (source, temp) => File.WriteAllBytes(source, PatchFixture.OriginalDll(PeFixture.MachineX64, 9));

                var change = new DllChange(dll, new[] { PatchAction.Backup(dll, bak) }, null, false);
                ExecutionResult result = executor.Execute(Plan(change));

                Assert.IsFalse(result.Succeeded);
                StringAssert.Contains(result.Error, "does not match the original");
                Assert.IsFalse(File.Exists(bak));
                Assert.AreEqual(0, TemporaryFiles().Length);
                Assert.AreEqual(0, result.CompletedActions.Count);
            }
            finally
            {
                PatchExecutor.AfterBackupCopy = null;
            }
        }

        [TestMethod]
        public void Execute_DllDeletedSinceThePlan_StopsBeforeWritingAnything()
        {
            File.Delete(dll);

            ExecutionResult result = executor.Execute(Plan(PatchSteps(dll, originalHash)));

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(PatchActionKind.VerifyHash, result.FailedAction.Kind);
            Assert.AreEqual(0, result.CompletedActions.Count);
            StringAssert.Contains(result.Error, "is missing");
            Assert.IsFalse(File.Exists(dll));
            Assert.IsFalse(File.Exists(bak));
        }

        [TestMethod]
        public void Execute_SecondDllChangedSinceThePlan_WritesNothingForTheFirstEither()
        {
            string dll2 = fx.WriteGameFile(@"x86\openvr_api.dll", PatchFixture.OriginalDll(PeFixture.MachineX86, 1));
            string hash2 = FileHash.Sha256(dll2);
            File.WriteAllBytes(dll2, PatchFixture.OriginalDll(PeFixture.MachineX86, 7));
            string before = fx.Snapshot();

            ExecutionResult result = executor.Execute(Plan(PatchSteps(dll, originalHash), PatchSteps(dll2, hash2)));

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(0, result.CompletedChanges);
            Assert.AreEqual(0, result.CompletedActions.Count);
            Assert.AreEqual(dll2, result.FailedAction.Target);
            Assert.AreEqual(before, fx.Snapshot());
        }

        [TestMethod]
        public void Execute_CopySourceIsNotTheFileOfThePlan_LeavesTargetAndNoTemporaryFile()
        {
            var change = new DllChange(dll, new[] { PatchAction.Copy(cached, dll, new string('0', 64)) }, null, false);

            ExecutionResult result = executor.Execute(Plan(change));

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(originalHash, FileHash.Sha256(dll));
            Assert.AreEqual(0, TemporaryFiles().Length);
        }

        [TestMethod]
        public void Execute_SecondDllFails_ReportsTheFirstDllCompleted()
        {
            string dll2 = fx.WriteGameFile(@"x86\openvr_api.dll", PatchFixture.OriginalDll(PeFixture.MachineX86, 1));
            // A copy whose source is not the planned file fails while running, after the first DLL is done.
            var failing = new DllChange(dll2, new[] { PatchAction.Copy(cached, dll2, new string('0', 64)) }, null, false);

            ExecutionResult result = executor.Execute(Plan(PatchSteps(dll, originalHash), failing));

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(1, result.CompletedChanges);
            Assert.AreEqual(ocHash, FileHash.Sha256(dll));
        }

        [TestMethod]
        public void Execute_GameRunning_RefusesAndWritesNothing()
        {
            fx.Processes.Paths.Add(Path.Combine(fx.InstallDir, "Beat Saber.exe"));
            string before = fx.Snapshot();

            ExecutionResult result = executor.Execute(Plan(PatchSteps(dll, originalHash)));

            Assert.IsTrue(result.Refused);
            Assert.AreEqual(0, result.CompletedActions.Count);
            Assert.AreEqual(before, fx.Snapshot());
        }

        [TestMethod]
        public void Execute_WriteIniOverAFileNotAllowed_FailsAndKeepsIt()
        {
            string ini = fx.WriteGameFile("opencomposite.ini", System.Text.Encoding.ASCII.GetBytes("supersampleRatio=1.7\r\n"));
            var change = new DllChange(dll, new[] { PatchAction.WriteIni(ini, "supersampleRatio=1.0\r\n", false) }, null, false);

            ExecutionResult result = executor.Execute(Plan(change));

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual("supersampleRatio=1.7\r\n", File.ReadAllText(ini));
        }

        [TestMethod]
        public void Execute_WriteIniOverOwnFile_ReplacesIt()
        {
            string ini = fx.WriteGameFile("opencomposite.ini", System.Text.Encoding.ASCII.GetBytes("supersampleRatio=1.7\r\n"));
            var change = new DllChange(dll, new[] { PatchAction.WriteIni(ini, "supersampleRatio=1.25\r\n", true) }, null, false);

            ExecutionResult result = executor.Execute(Plan(change));

            Assert.IsTrue(result.Succeeded, result.Error);
            Assert.AreEqual("supersampleRatio=1.25\r\n", File.ReadAllText(ini));
        }

        [TestMethod]
        public void Execute_DeleteWithHashMismatch_KeepsTheFile()
        {
            var change = new DllChange(dll, new[] { PatchAction.Delete(dll, new string('0', 64)) }, null, false);

            ExecutionResult result = executor.Execute(Plan(change));

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(File.Exists(dll));
        }

        [TestMethod]
        public void Execute_FolderDeniesCreatingFiles_ReportsNeedsElevation()
        {
            var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User, FileSystemRights.CreateFiles, AccessControlType.Deny);
            DirectorySecurity security = Directory.GetAccessControl(fx.InstallDir);
            security.AddAccessRule(rule);
            Directory.SetAccessControl(fx.InstallDir, security);
            try
            {
                if (CanCreateFileIn(fx.InstallDir))
                {
                    Assert.Inconclusive("A deny ACL did not block creating files for this account.");
                }

                ExecutionResult result = executor.Execute(Plan(PatchSteps(dll, originalHash)));

                Assert.IsFalse(result.Succeeded);
                Assert.IsTrue(result.NeedsElevation);
                Assert.AreEqual(PatchActionKind.BackupFile, result.FailedAction.Kind);
                Assert.AreEqual(originalHash, FileHash.Sha256(dll));
            }
            finally
            {
                security.RemoveAccessRule(rule);
                Directory.SetAccessControl(fx.InstallDir, security);
            }
        }

        [TestMethod]
        public void Execute_PlanWithBlockers_Throws()
        {
            var blocked = new PatchPlan(PatchOperation.Patch, fx.Game, new[] { PatchSteps(dll, originalHash) }, new[] { "blocked" }, null, null);

            Assert.ThrowsException<InvalidOperationException>(() => executor.Execute(blocked));
            Assert.IsFalse(File.Exists(bak));
        }

        private static bool CanCreateFileIn(string folder)
        {
            string probe = Path.Combine(folder, "probe-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
