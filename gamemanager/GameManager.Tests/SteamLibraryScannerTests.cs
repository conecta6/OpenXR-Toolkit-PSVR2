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

using System.Collections.Generic;
using System.IO;
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    [TestClass]
    public class SteamLibraryScannerTests
    {
        private TempDir temp;
        private SteamFixture steam;

        [TestInitialize]
        public void SetUp()
        {
            temp = new TempDir();
            steam = new SteamFixture(temp);
        }

        [TestCleanup]
        public void TearDown()
        {
            temp.Dispose();
        }

        [TestMethod]
        public void Scan_CurrentFormat_ListsGamesFromAllLibraries()
        {
            string libD = steam.CreateLibrary("SteamLibraryD");
            steam.WriteLibraryFolders(SteamFixture.CurrentFormat(steam.SteamRoot, libD));
            string beatSaber = steam.AddGame(steam.SteamRoot, 620980, "Beat Saber", "Beat Saber");
            string alyx = steam.AddGame(libD, 546560, "Half-Life: Alyx", "Half-Life Alyx");

            LibraryScanResult result = SteamLibraryScanner.Scan(steam.SteamRoot);

            Assert.AreEqual(0, result.Warnings.Count, string.Join("\n", result.Warnings));
            Assert.AreEqual(2, result.Games.Count);
            Assert.AreEqual(620980, result.Games[0].AppId);
            Assert.AreEqual("Beat Saber", result.Games[0].Name);
            Assert.AreEqual(beatSaber, result.Games[0].InstallDir, true);
            Assert.AreEqual(steam.SteamRoot, result.Games[0].LibraryPath, true);
            Assert.AreEqual(546560, result.Games[1].AppId);
            Assert.AreEqual(alyx, result.Games[1].InstallDir, true);
            Assert.AreEqual(libD, result.Games[1].LibraryPath, true);
        }

        [TestMethod]
        public void FindLibraries_EscapedBackslashesInPath_AreUnescaped()
        {
            string libD = steam.CreateLibrary(@"Deep\Steam Library (D)");
            steam.WriteLibraryFolders(SteamFixture.CurrentFormat(steam.SteamRoot, libD));
            var warnings = new List<string>();

            IReadOnlyList<string> libraries = SteamLibraryScanner.FindLibraries(steam.SteamRoot, warnings);

            Assert.AreEqual(2, libraries.Count);
            Assert.AreEqual(libD, libraries[1], true);
            Assert.AreEqual(0, warnings.Count, string.Join("\n", warnings));
        }

        [TestMethod]
        public void FindLibraries_LegacyFormat_ReadsNumericKeysAndAlwaysIncludesSteamRoot()
        {
            string libD = steam.CreateLibrary("SteamLibraryD");
            steam.WriteLibraryFolders(SteamFixture.LegacyFormat(libD));
            var warnings = new List<string>();

            IReadOnlyList<string> libraries = SteamLibraryScanner.FindLibraries(steam.SteamRoot, warnings);

            Assert.AreEqual(2, libraries.Count);
            Assert.AreEqual(steam.SteamRoot, libraries[0], true);
            Assert.AreEqual(libD, libraries[1], true);
            Assert.AreEqual(0, warnings.Count, string.Join("\n", warnings));
        }

        [TestMethod]
        public void FindLibraries_DuplicateEntries_AreListedOnce()
        {
            string libD = steam.CreateLibrary("SteamLibraryD");
            steam.WriteLibraryFolders(SteamFixture.CurrentFormat(
                steam.SteamRoot,
                libD,
                libD.ToUpperInvariant() + "\\",
                libD.Replace('\\', '/'),
                steam.SteamRoot + "\\"));
            var warnings = new List<string>();

            IReadOnlyList<string> libraries = SteamLibraryScanner.FindLibraries(steam.SteamRoot, warnings);

            Assert.AreEqual(2, libraries.Count);
            Assert.AreEqual(0, warnings.Count, string.Join("\n", warnings));
        }

        [TestMethod]
        public void FindLibraries_MissingLibraryFolder_WarnsAndKeepsOthers()
        {
            steam.WriteLibraryFolders(SteamFixture.CurrentFormat(steam.SteamRoot, temp.PathOf("UnpluggedDrive")));
            var warnings = new List<string>();

            IReadOnlyList<string> libraries = SteamLibraryScanner.FindLibraries(steam.SteamRoot, warnings);

            Assert.AreEqual(1, libraries.Count);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings[0], "UnpluggedDrive");
        }

        [TestMethod]
        public void FindLibraries_NoLibraryFoldersFile_ScansSteamRootWithWarning()
        {
            var warnings = new List<string>();

            IReadOnlyList<string> libraries = SteamLibraryScanner.FindLibraries(steam.SteamRoot, warnings);

            Assert.AreEqual(1, libraries.Count);
            Assert.AreEqual(steam.SteamRoot, libraries[0], true);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings[0], "libraryfolders.vdf");
        }

        [TestMethod]
        public void FindLibraries_CorruptLibraryFoldersFile_ScansSteamRootWithWarning()
        {
            steam.WriteLibraryFolders("\"libraryfolders\" { \"0\" { \"path\" \"C:\\\\x\" ");
            var warnings = new List<string>();

            IReadOnlyList<string> libraries = SteamLibraryScanner.FindLibraries(steam.SteamRoot, warnings);

            Assert.AreEqual(1, libraries.Count);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings[0], "libraryfolders.vdf");
        }

        [TestMethod]
        public void Scan_CorruptManifest_IsSkippedWithWarningAndOthersStillListed()
        {
            steam.WriteLibraryFolders(SteamFixture.CurrentFormat(steam.SteamRoot));
            steam.AddGame(steam.SteamRoot, 620980, "Beat Saber", "Beat Saber");
            File.WriteAllText(Path.Combine(steam.SteamRoot, "steamapps", "appmanifest_999.acf"), "\"AppState\" { \"appid\" \"999\" ");

            LibraryScanResult result = SteamLibraryScanner.Scan(steam.SteamRoot);

            Assert.AreEqual(1, result.Games.Count);
            Assert.AreEqual(620980, result.Games[0].AppId);
            Assert.AreEqual(1, result.Warnings.Count);
            StringAssert.Contains(result.Warnings[0], "appmanifest_999.acf");
        }

        [TestMethod]
        public void Scan_ManifestWithInvalidAppId_IsSkippedWithWarning()
        {
            steam.WriteLibraryFolders(SteamFixture.CurrentFormat(steam.SteamRoot));
            File.WriteAllText(
                Path.Combine(steam.SteamRoot, "steamapps", "appmanifest_1.acf"),
                "\"AppState\" { \"appid\" \"abc\" \"name\" \"X\" \"installdir\" \"X\" }");
            Directory.CreateDirectory(Path.Combine(steam.SteamRoot, "steamapps", "common", "X"));

            LibraryScanResult result = SteamLibraryScanner.Scan(steam.SteamRoot);

            Assert.AreEqual(0, result.Games.Count);
            Assert.AreEqual(1, result.Warnings.Count);
            StringAssert.Contains(result.Warnings[0], "appmanifest_1.acf");
        }

        [TestMethod]
        public void Scan_SteamVrAndRedistributables_AreExcludedSilently()
        {
            steam.WriteLibraryFolders(SteamFixture.CurrentFormat(steam.SteamRoot));
            steam.AddGame(steam.SteamRoot, 250820, "SteamVR", "SteamVR");
            steam.AddGame(steam.SteamRoot, 228980, "Steamworks Common Redistributables", "Steamworks Shared");
            steam.AddGame(steam.SteamRoot, 620980, "Beat Saber", "Beat Saber");

            LibraryScanResult result = SteamLibraryScanner.Scan(steam.SteamRoot);

            Assert.AreEqual(1, result.Games.Count);
            Assert.AreEqual(620980, result.Games[0].AppId);
            Assert.AreEqual(0, result.Warnings.Count, string.Join("\n", result.Warnings));
        }

        [TestMethod]
        public void Scan_InstallFolderMissing_IsSkippedWithWarning()
        {
            steam.WriteLibraryFolders(SteamFixture.CurrentFormat(steam.SteamRoot));
            steam.AddGame(steam.SteamRoot, 438100, "VRChat", "VRChat", createFolder: false);

            LibraryScanResult result = SteamLibraryScanner.Scan(steam.SteamRoot);

            Assert.AreEqual(0, result.Games.Count);
            Assert.AreEqual(1, result.Warnings.Count);
            StringAssert.Contains(result.Warnings[0], "VRChat");
        }

        [TestMethod]
        public void Scan_InstallDirOutsideLibrary_IsSkippedWithWarning()
        {
            steam.WriteLibraryFolders(SteamFixture.CurrentFormat(steam.SteamRoot));
            steam.AddGame(steam.SteamRoot, 12345, "Broken", @"..\..\..\Windows", createFolder: false);

            LibraryScanResult result = SteamLibraryScanner.Scan(steam.SteamRoot);

            Assert.AreEqual(0, result.Games.Count);
            Assert.AreEqual(1, result.Warnings.Count);
            StringAssert.Contains(result.Warnings[0], "outside");
        }

        [TestMethod]
        public void Scan_NonAsciiName_IsPreserved()
        {
            string name = "\u6771\u65b9 Pok\u00e9mon";
            steam.WriteLibraryFolders(SteamFixture.CurrentFormat(steam.SteamRoot));
            steam.AddGame(steam.SteamRoot, 777, name, "Touhou");

            LibraryScanResult result = SteamLibraryScanner.Scan(steam.SteamRoot);

            Assert.AreEqual(1, result.Games.Count);
            Assert.AreEqual(name, result.Games[0].Name);
        }

        [TestMethod]
        public void Scan_GamesAreSortedByNameIgnoringCase()
        {
            steam.WriteLibraryFolders(SteamFixture.CurrentFormat(steam.SteamRoot));
            steam.AddGame(steam.SteamRoot, 3, "Zeta", "Zeta");
            steam.AddGame(steam.SteamRoot, 4, "alpha", "alpha");

            LibraryScanResult result = SteamLibraryScanner.Scan(steam.SteamRoot);

            Assert.AreEqual("alpha", result.Games[0].Name);
            Assert.AreEqual("Zeta", result.Games[1].Name);
        }
    }
}
