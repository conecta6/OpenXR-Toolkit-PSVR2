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
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    [TestClass]
    public class GameClassifierTests
    {
        private TempDir temp;

        [TestInitialize]
        public void SetUp()
        {
            temp = new TempDir();
        }

        [TestCleanup]
        public void TearDown()
        {
            temp.Dispose();
        }

        private string GameDir
        {
            get { return temp.PathOf("Game"); }
        }

        [TestMethod]
        public void Classify_EmptyFolder_IsUnknown()
        {
            temp.CreateDirectory("Game");

            GameClassification result = GameClassifier.Classify(GameDir);

            Assert.AreEqual(GameKind.Unknown, result.Kind);
            Assert.AreEqual(0, result.OpenVrDlls.Count);
            Assert.IsFalse(result.HasOpenXrLoader);
            Assert.AreEqual(0, result.AntiCheatMarkers.Count);
            Assert.AreEqual(0, result.Warnings.Count);
        }

        [TestMethod]
        public void Classify_OpenXrLoaderOnly_IsOpenXrProbable()
        {
            temp.WriteBytes(@"Game\bin\openxr_loader.dll", new byte[] { 1, 2, 3 });

            GameClassification result = GameClassifier.Classify(GameDir);

            Assert.AreEqual(GameKind.OpenXrProbable, result.Kind);
            Assert.IsTrue(result.HasOpenXrLoader);
            Assert.AreEqual(0, result.OpenVrDlls.Count);
        }

        [TestMethod]
        public void Classify_NestedDllsOfDifferentArchitectures_ListsEachWithRelativePath()
        {
            temp.WriteBytes(@"Game\Beat Saber_Data\Plugins\x86_64\openvr_api.dll", PeFixture.Build(PeFixture.MachineX64));
            temp.WriteBytes(@"Game\Beat Saber_Data\Plugins\x86\openvr_api.dll", PeFixture.Build(PeFixture.MachineX86));
            temp.WriteBytes(@"Game\Beat Saber_Data\Plugins\x86_64\openxr_loader.dll", new byte[] { 0 });
            temp.WriteText(@"Game\Beat Saber.exe", "not really an exe");

            GameClassification result = GameClassifier.Classify(GameDir);

            Assert.AreEqual(GameKind.OpenVr, result.Kind);
            Assert.IsTrue(result.HasOpenXrLoader);
            Assert.AreEqual(2, result.OpenVrDlls.Count);
            Assert.AreEqual(@"Beat Saber_Data\Plugins\x86\openvr_api.dll", result.OpenVrDlls[0].RelativePath);
            Assert.AreEqual(PeMachine.X86, result.OpenVrDlls[0].Machine);
            Assert.AreEqual(@"Beat Saber_Data\Plugins\x86_64\openvr_api.dll", result.OpenVrDlls[1].RelativePath);
            Assert.AreEqual(PeMachine.X64, result.OpenVrDlls[1].Machine);
            Assert.AreEqual(0, result.Warnings.Count);
        }

        [TestMethod]
        public void Classify_UppercaseDllName_IsFound()
        {
            temp.WriteBytes(@"Game\OPENVR_API.DLL", PeFixture.Build(PeFixture.MachineX64));

            GameClassification result = GameClassifier.Classify(GameDir);

            Assert.AreEqual(GameKind.OpenVr, result.Kind);
            Assert.AreEqual("OPENVR_API.DLL", result.OpenVrDlls[0].RelativePath);
            Assert.AreEqual(PeMachine.X64, result.OpenVrDlls[0].Machine);
        }

        [TestMethod]
        public void Classify_DllThatIsNotAPeFile_IsOpenVrWithUnknownArchitecture()
        {
            temp.WriteText(@"Game\openvr_api.dll", "garbage");

            GameClassification result = GameClassifier.Classify(GameDir);

            Assert.AreEqual(GameKind.OpenVr, result.Kind);
            Assert.AreEqual(PeMachine.Unknown, result.OpenVrDlls[0].Machine);
            Assert.AreEqual(0, result.Warnings.Count);
        }

        [TestMethod]
        public void Classify_MissingInstallFolder_IsUnknownWithWarning()
        {
            GameClassification result = GameClassifier.Classify(GameDir);

            Assert.AreEqual(GameKind.Unknown, result.Kind);
            Assert.AreEqual(1, result.Warnings.Count);
        }

        [TestMethod]
        [Timeout(30000)]
        public void Classify_JunctionsInsideGame_AreSkippedWithWarning()
        {
            temp.WriteBytes(@"Game\openvr_api.dll", PeFixture.Build(PeFixture.MachineX64));
            temp.WriteBytes(@"Outside\openvr_api.dll", PeFixture.Build(PeFixture.MachineX86));
            if (!temp.TryCreateJunction(@"Game\LinkToOutside", temp.PathOf("Outside"))
                || !temp.TryCreateJunction(@"Game\LinkToSelf", GameDir))
            {
                Assert.Inconclusive("Could not create directory junctions with mklink /J on this machine.");
            }

            GameClassification result = GameClassifier.Classify(GameDir);

            Assert.AreEqual(1, result.OpenVrDlls.Count);
            Assert.AreEqual("openvr_api.dll", result.OpenVrDlls[0].RelativePath);
            Assert.AreEqual(2, result.Warnings.Count);
            Assert.IsTrue(result.Warnings.Any(w => w.Contains("LinkToOutside")));
            Assert.IsTrue(result.Warnings.Any(w => w.Contains("LinkToSelf")));
        }

        [TestMethod]
        public void Classify_InstallFolderIsJunction_IsStillWalked()
        {
            temp.WriteBytes(@"OtherDrive\Moved Game\bin\win64\openvr_api.dll", PeFixture.Build(PeFixture.MachineX64));
            if (!temp.TryCreateJunction("Game", temp.PathOf(@"OtherDrive\Moved Game")))
            {
                Assert.Inconclusive("Could not create a directory junction with mklink /J on this machine.");
            }

            GameClassification result = GameClassifier.Classify(GameDir);

            Assert.AreEqual(GameKind.OpenVr, result.Kind);
            Assert.AreEqual(@"bin\win64\openvr_api.dll", result.OpenVrDlls[0].RelativePath);
            Assert.AreEqual(0, result.Warnings.Count);
        }

        [TestMethod]
        public void Classify_UnreadableFolder_WarnsAndKeepsScanning()
        {
            temp.WriteBytes(@"Game\Readable\openvr_api.dll", PeFixture.Build(PeFixture.MachineX64));
            string locked = temp.CreateDirectory(@"Game\Locked");
            var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User, FileSystemRights.ListDirectory, AccessControlType.Deny);
            DirectorySecurity security = Directory.GetAccessControl(locked);
            security.AddAccessRule(rule);
            Directory.SetAccessControl(locked, security);
            try
            {
                if (CanList(locked))
                {
                    Assert.Inconclusive("A deny ACL did not block listing for this account.");
                }

                GameClassification result = GameClassifier.Classify(GameDir);

                Assert.AreEqual(1, result.OpenVrDlls.Count);
                Assert.AreEqual(1, result.Warnings.Count);
                StringAssert.Contains(result.Warnings[0], "Locked");
            }
            finally
            {
                security.RemoveAccessRule(rule);
                Directory.SetAccessControl(locked, security);
            }
        }

        [TestMethod]
        public void Classify_LockedDll_ReportsUnknownWithWarning()
        {
            string dll = temp.WriteBytes(@"Game\openvr_api.dll", PeFixture.Build(PeFixture.MachineX64));
            temp.WriteBytes(@"Game\Sub\openvr_api.dll", PeFixture.Build(PeFixture.MachineX86));

            using (new FileStream(dll, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                GameClassification result = GameClassifier.Classify(GameDir);

                Assert.AreEqual(GameKind.OpenVr, result.Kind);
                Assert.AreEqual(2, result.OpenVrDlls.Count);
                Assert.AreEqual("openvr_api.dll", result.OpenVrDlls[0].RelativePath);
                Assert.AreEqual(PeMachine.Unknown, result.OpenVrDlls[0].Machine);
                Assert.AreEqual(@"Sub\openvr_api.dll", result.OpenVrDlls[1].RelativePath);
                Assert.AreEqual(PeMachine.X86, result.OpenVrDlls[1].Machine);
                Assert.AreEqual(1, result.Warnings.Count);
                StringAssert.Contains(result.Warnings[0], "openvr_api.dll");
            }
        }

        [TestMethod]
        public void Classify_WofCompressedDll_IsStillFoundAndRead()
        {
            string dll = temp.WriteBytes(@"Game\openvr_api.dll", PeFixture.Build(PeFixture.MachineX64, 0x80, 65536));
            int exitCode = TempDir.RunHidden("compact.exe", "/c /exe:xpress4k \"" + dll + "\"");
            if (exitCode != 0 || !NativeMethods.IsCompressedSmallerThanLength(dll, new FileInfo(dll).Length))
            {
                Assert.Inconclusive("compact.exe could not WOF-compress the test file on this volume.");
            }

            GameClassification result = GameClassifier.Classify(GameDir);

            Assert.AreEqual(GameKind.OpenVr, result.Kind);
            Assert.AreEqual(PeMachine.X64, result.OpenVrDlls[0].Machine);
            Assert.AreEqual(0, result.Warnings.Count);
        }

        [TestMethod]
        public void Classify_AntiCheatMarkersInSubfoldersAnyCase_AreRecorded()
        {
            temp.WriteBytes(@"Game\Binaries\Win64\openvr_api.dll", PeFixture.Build(PeFixture.MachineX64));
            temp.WriteText(@"Game\Binaries\Win64\EASYANTICHEAT_EOS\Settings.json", "{}");
            temp.WriteText(@"Game\Binaries\Win64\EASYANTICHEAT_EOS\EasyAntiCheat_EOS_Setup.exe", "x");
            temp.WriteText(@"Game\start_protected_game.exe", "x");
            temp.WriteText(@"Game\EasyAntiCheat.txt", "not a marker");

            GameClassification result = GameClassifier.Classify(GameDir);

            Assert.AreEqual(GameKind.OpenVr, result.Kind);
            CollectionAssert.AreEqual(
                new[]
                {
                    @"Binaries\Win64\EASYANTICHEAT_EOS",
                    @"Binaries\Win64\EASYANTICHEAT_EOS\EasyAntiCheat_EOS_Setup.exe",
                    "start_protected_game.exe",
                },
                result.AntiCheatMarkers.ToArray());
            Assert.AreEqual(0, result.Warnings.Count);
        }

        [TestMethod]
        public void Classify_BattlEyeMarkers_AreRecorded()
        {
            temp.WriteText(@"Game\BattlEye\BEClient_x64.dll", "x");
            temp.WriteText(@"Game\bin\BEService_x64.exe", "x");
            temp.WriteText(@"Game\bin\BEService.dll", "not a marker");

            GameClassification result = GameClassifier.Classify(GameDir);

            Assert.AreEqual(GameKind.Unknown, result.Kind);
            CollectionAssert.AreEqual(
                new[] { "BattlEye", @"BattlEye\BEClient_x64.dll", @"bin\BEService_x64.exe" },
                result.AntiCheatMarkers.ToArray());
        }

        private static bool CanList(string directory)
        {
            try
            {
                Directory.GetFileSystemEntries(directory);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Test-only access to the real on-disk size of a file, bypassing File.GetAttributes: the WOF
        /// compression filter driver can hide FILE_ATTRIBUTE_REPARSE_POINT from managed attribute APIs
        /// (File.GetAttributes, DirectoryInfo.EnumerateFileSystemInfos) even on a file it has compressed,
        /// so that bit cannot be used to detect WOF compression from a test.
        /// </summary>
        private static class NativeMethods
        {
            private const uint InvalidFileSize = 0xFFFFFFFF;

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern uint GetCompressedFileSizeW(string lpFileName, out uint lpFileSizeHigh);

            public static bool IsCompressedSmallerThanLength(string path, long length)
            {
                uint low = GetCompressedFileSizeW(path, out uint high);
                if (low == InvalidFileSize && Marshal.GetLastWin32Error() != 0)
                {
                    return false;
                }
                long compressedSize = ((long)high << 32) | low;
                return compressedSize < length;
            }
        }
    }
}
