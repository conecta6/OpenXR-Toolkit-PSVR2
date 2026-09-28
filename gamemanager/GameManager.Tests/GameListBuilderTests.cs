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
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;

namespace GameManager.Tests
{
    [TestClass]
    public class GameListBuilderTests
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
        public void BuildFrom_ClassifiesEveryGameAndPrefixesWarnings()
        {
            steam.WriteLibraryFolders(SteamFixture.CurrentFormat(steam.SteamRoot));
            string beatSaber = steam.AddGame(steam.SteamRoot, 620980, "Beat Saber", "Beat Saber");
            File.WriteAllBytes(Path.Combine(beatSaber, "openvr_api.dll"), PeFixture.Build(PeFixture.MachineX64));
            string lockedGame = steam.AddGame(steam.SteamRoot, 555, "Locked Game", "Locked Game");
            string lockedDll = Path.Combine(lockedGame, "openvr_api.dll");
            File.WriteAllBytes(lockedDll, PeFixture.Build(PeFixture.MachineX86));
            steam.AddGame(steam.SteamRoot, 1000, "Flat Game", "Flat Game");
            var progress = new ListProgress();

            GameListResult result;
            using (new FileStream(lockedDll, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                result = GameListBuilder.BuildFrom(steam.SteamRoot, progress);
            }

            Assert.AreEqual(steam.SteamRoot, result.SteamRoot, true);
            Assert.AreEqual(3, result.Entries.Count);
            Assert.AreEqual("Beat Saber", result.Entries[0].Game.Name);
            Assert.AreEqual(GameKind.OpenVr, result.Entries[0].Classification.Kind);
            Assert.AreEqual("Flat Game", result.Entries[1].Game.Name);
            Assert.AreEqual(GameKind.Unknown, result.Entries[1].Classification.Kind);
            Assert.AreEqual("Locked Game", result.Entries[2].Game.Name);
            Assert.AreEqual(PeMachine.Unknown, result.Entries[2].Classification.OpenVrDlls[0].Machine);
            Assert.AreEqual(1, result.Warnings.Count);
            StringAssert.StartsWith(result.Warnings[0], "Locked Game: ");
            CollectionAssert.AreEqual(new[] { "Beat Saber", "Flat Game", "Locked Game" }, progress.Reports);
        }

        [TestMethod]
        public void BuildFrom_KeepsScannerWarnings()
        {
            steam.WriteLibraryFolders(SteamFixture.CurrentFormat(steam.SteamRoot, temp.PathOf("UnpluggedDrive")));

            GameListResult result = GameListBuilder.BuildFrom(steam.SteamRoot);

            Assert.AreEqual(0, result.Entries.Count);
            Assert.AreEqual(1, result.Warnings.Count);
            StringAssert.Contains(result.Warnings[0], "UnpluggedDrive");
        }

        [TestMethod]
        public void Build_SteamNotFound_ReturnsNullRootAndNoEntries()
        {
            GameListResult result = GameListBuilder.Build(new SteamLocator(new FakeRegistryReader()));

            Assert.IsNull(result.SteamRoot);
            Assert.AreEqual(0, result.Entries.Count);
            Assert.AreEqual(0, result.Warnings.Count);
        }

        [TestMethod]
        public void Build_SteamFound_ScansIt()
        {
            var registry = new FakeRegistryReader();
            registry.Set(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Valve\Steam", "SteamPath", steam.SteamRoot.Replace('\\', '/'));
            steam.WriteLibraryFolders(SteamFixture.CurrentFormat(steam.SteamRoot));
            steam.AddGame(steam.SteamRoot, 620980, "Beat Saber", "Beat Saber");

            GameListResult result = GameListBuilder.Build(new SteamLocator(registry));

            Assert.AreEqual(steam.SteamRoot, result.SteamRoot, true);
            Assert.AreEqual(1, result.Entries.Count);
        }

        private sealed class ListProgress : IProgress<string>
        {
            public List<string> Reports { get; } = new List<string>();

            public void Report(string value)
            {
                Reports.Add(value);
            }
        }
    }
}
