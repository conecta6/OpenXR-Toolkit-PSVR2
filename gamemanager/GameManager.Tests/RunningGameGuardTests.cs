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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    [TestClass]
    public class RunningGameGuardTests
    {
        private TempDir temp;
        private FakeProcessImageSource processes;
        private RunningGameGuard guard;
        private string gameDir;
        private string dll;

        [TestInitialize]
        public void SetUp()
        {
            temp = new TempDir();
            processes = new FakeProcessImageSource();
            guard = new RunningGameGuard(processes, directory => null);
            dll = temp.WriteBytes(@"Game\bin\openvr_api.dll", PeFixture.Build(PeFixture.MachineX64));
            gameDir = temp.PathOf("Game");
        }

        [TestCleanup]
        public void TearDown()
        {
            temp.Dispose();
        }

        [TestMethod]
        public void Check_NothingRunningAndFileFree_IsClear()
        {
            GuardResult result = guard.Check(gameDir, new[] { dll });

            Assert.IsTrue(result.IsClear, result.Message);
            Assert.AreEqual(GuardVerdict.Clear, result.Verdict);
        }

        [TestMethod]
        public void Check_ProcessUnderInstallFolder_RefusesAsGameRunning()
        {
            processes.Paths.Add(Path.Combine(gameDir, @"bin\Game.exe"));

            GuardResult result = guard.Check(gameDir, new[] { dll });

            Assert.AreEqual(GuardVerdict.GameRunning, result.Verdict);
            StringAssert.Contains(result.Message, "Game.exe");
            Assert.IsFalse(result.NeedsElevation);
        }

        [TestMethod]
        public void Check_ProcessPathInOtherCase_IsStillRefused()
        {
            processes.Paths.Add(gameDir.ToUpperInvariant() + @"\GAME.EXE");

            Assert.AreEqual(GuardVerdict.GameRunning, guard.Check(gameDir, new[] { dll }).Verdict);
        }

        [TestMethod]
        public void Check_ProcessInSiblingFolderSharingThePrefix_IsClear()
        {
            processes.Paths.Add(temp.PathOf(@"Game2\Game.exe"));

            Assert.IsTrue(guard.Check(gameDir, new[] { dll }).IsClear);
        }

        [TestMethod]
        public void Check_DllHeldOpenByAnotherProgram_RefusesAsFileInUse()
        {
            // What a running game (or an anti-virus scan) looks like to the guard: another open handle.
            using (new FileStream(dll, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                GuardResult result = guard.Check(gameDir, new[] { dll });

                Assert.AreEqual(GuardVerdict.FileInUse, result.Verdict);
                StringAssert.Contains(result.Message, "in use");
                Assert.IsFalse(result.NeedsElevation);
            }
        }

        [TestMethod]
        public void Check_DllWriteDenied_ReportsAccessDeniedAndNeedsElevation()
        {
            var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User, FileSystemRights.WriteData, AccessControlType.Deny);
            FileSecurity security = File.GetAccessControl(dll);
            security.AddAccessRule(rule);
            File.SetAccessControl(dll, security);
            try
            {
                if (CanOpenForWrite(dll))
                {
                    Assert.Inconclusive("A deny ACL did not block writing for this account.");
                }

                GuardResult result = guard.Check(gameDir, new[] { dll });

                Assert.AreEqual(GuardVerdict.AccessDenied, result.Verdict);
                Assert.IsTrue(result.NeedsElevation);
            }
            finally
            {
                security.RemoveAccessRule(rule);
                File.SetAccessControl(dll, security);
            }
        }

        [TestMethod]
        public void Check_TargetThatDoesNotExistYet_IsClear()
        {
            Assert.IsTrue(guard.Check(gameDir, new[] { Path.Combine(gameDir, "opencomposite.ini") }).IsClear);
        }

        [TestMethod]
        [Timeout(30000)]
        public void Check_InstallFolderIsJunction_ProcessUnderItsTarget_IsRefused()
        {
            temp.CreateDirectory(@"OtherDrive\Moved Game");
            if (!temp.TryCreateJunction("Linked Game", temp.PathOf(@"OtherDrive\Moved Game")))
            {
                Assert.Inconclusive("Could not create a directory junction with mklink /J on this machine.");
            }
            processes.Paths.Add(temp.PathOf(@"OtherDrive\Moved Game\Game.exe"));
            var realResolver = new RunningGameGuard(processes);

            GuardResult result = realResolver.Check(temp.PathOf("Linked Game"), new string[0]);

            Assert.AreEqual(GuardVerdict.GameRunning, result.Verdict);
        }

        [TestMethod]
        public void WindowsProcessImageSource_ListsTheCurrentProcess()
        {
            string self = Process.GetCurrentProcess().MainModule.FileName;

            IReadOnlyList<string> paths = new WindowsProcessImageSource().GetRunningImagePaths();

            Assert.IsTrue(paths.Any(p => string.Equals(p, self, StringComparison.OrdinalIgnoreCase)), self);
        }

        [TestMethod]
        public void QueryImagePath_ProcessThatCannotBeOpened_IsSkipped()
        {
            // Process 0 (System Idle Process) can never be opened: R19 says skip, not fail.
            Assert.IsNull(WindowsProcessImageSource.QueryImagePath(0));
        }

        [TestMethod]
        public void Check_RealProcessList_RefusesTheFolderOfTheTestHost()
        {
            // End to end over the real P/Invoke path, read-only: the test host is "a game running from this folder".
            string hostFolder = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            var real = new RunningGameGuard(new WindowsProcessImageSource());

            GuardResult result = real.Check(hostFolder, new string[0]);

            Assert.AreEqual(GuardVerdict.GameRunning, result.Verdict);
        }

        private static bool CanOpenForWrite(string path)
        {
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    return true;
                }
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
