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
using System.Threading;
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
                result = GameListBuilder.BuildFrom(steam.SteamRoot, CompatibilityList.Empty, progress);
            }

            Assert.AreEqual(steam.SteamRoot, result.SteamRoot, true);
            Assert.AreEqual(3, result.Entries.Count);
            Assert.AreEqual("Beat Saber", result.Entries[0].Game.Name);
            Assert.AreEqual(GameKind.OpenVr, result.Entries[0].Classification.Kind);
            Assert.AreEqual("Flat Game", result.Entries[1].Game.Name);
            Assert.AreEqual(GameKind.Unknown, result.Entries[1].Classification.Kind);
            Assert.AreEqual("Locked Game", result.Entries[2].Game.Name);
            Assert.AreEqual(GameKind.OpenVr, result.Entries[2].Classification.Kind);
            Assert.AreEqual(PeMachine.Unknown, result.Entries[2].Classification.OpenVrDlls[0].Machine);
            Assert.AreEqual(1, result.Warnings.Count);
            StringAssert.StartsWith(result.Warnings[0], "Locked Game: ");
            CollectionAssert.AreEqual(new[] { "Beat Saber", "Flat Game", "Locked Game" }, progress.Reports);
        }

        [TestMethod]
        public void BuildFrom_KeepsScannerWarnings()
        {
            steam.WriteLibraryFolders(SteamFixture.CurrentFormat(steam.SteamRoot, temp.PathOf("UnpluggedDrive")));

            GameListResult result = GameListBuilder.BuildFrom(steam.SteamRoot, CompatibilityList.Empty);

            Assert.AreEqual(0, result.Entries.Count);
            Assert.AreEqual(1, result.Warnings.Count);
            StringAssert.Contains(result.Warnings[0], "UnpluggedDrive");
        }

        [TestMethod]
        public void BuildFrom_AppliesCompatibilityListAndAntiCheatMarkers()
        {
            steam.WriteLibraryFolders(SteamFixture.CurrentFormat(steam.SteamRoot));
            steam.AddGame(steam.SteamRoot, 438100, "VRChat", "VRChat");
            string beatSaber = steam.AddGame(steam.SteamRoot, 620980, "Beat Saber", "Beat Saber");
            File.WriteAllBytes(Path.Combine(beatSaber, "openvr_api.dll"), PeFixture.Build(PeFixture.MachineX64));
            string eacGame = steam.AddGame(steam.SteamRoot, 777, "EAC Game", "EAC Game");
            Directory.CreateDirectory(Path.Combine(eacGame, "EasyAntiCheat"));
            steam.AddGame(steam.SteamRoot, 1000, "Flat Game", "Flat Game");
            var listWarnings = new List<string>();
            CompatibilityList list = CompatibilityList.Parse(
                "{ \"version\": 1, \"games\": [" +
                " { \"appId\": 438100, \"name\": \"VRChat\", \"status\": \"anticheat\", \"notes\": \"EasyAntiCheat\" }," +
                " { \"appId\": 620980, \"name\": \"Beat Saber\", \"status\": \"works\" }," +
                " { \"appId\": 777, \"name\": \"EAC Game\", \"status\": \"works\" } ] }",
                "test", listWarnings);

            GameListResult result = GameListBuilder.BuildFrom(steam.SteamRoot, list);

            Assert.AreEqual(0, listWarnings.Count);
            Assert.AreEqual(4, result.Entries.Count);

            GameEntry beat = result.Entries[0];
            Assert.AreEqual("Beat Saber", beat.Game.Name);
            Assert.AreEqual(CompatibilityStatus.Works, beat.Compatibility.ListStatus);
            Assert.IsFalse(beat.Compatibility.Blocked);

            // Listed as working, but ships anti-cheat files: the files win.
            GameEntry eac = result.Entries[1];
            Assert.AreEqual("EAC Game", eac.Game.Name);
            Assert.IsTrue(eac.Compatibility.Blocked);
            StringAssert.Contains(eac.Compatibility.Reason, "EasyAntiCheat");

            GameEntry flat = result.Entries[2];
            Assert.AreEqual("Flat Game", flat.Game.Name);
            Assert.AreEqual(CompatibilityStatus.Untested, flat.Compatibility.ListStatus);
            Assert.IsFalse(flat.Compatibility.Blocked);

            // No anti-cheat files on disk, but listed as anti-cheat: the list wins.
            GameEntry vrChat = result.Entries[3];
            Assert.AreEqual("VRChat", vrChat.Game.Name);
            Assert.AreEqual(CompatibilityStatus.AntiCheat, vrChat.Compatibility.ListStatus);
            Assert.IsTrue(vrChat.Compatibility.Blocked);
        }

        [TestMethod]
        public void BuildFrom_ClassifierThrowsForOneGame_OtherGamesStillListed()
        {
            steam.WriteLibraryFolders(SteamFixture.CurrentFormat(steam.SteamRoot));
            string beatSaber = steam.AddGame(steam.SteamRoot, 620980, "Beat Saber", "Beat Saber");
            File.WriteAllBytes(Path.Combine(beatSaber, "openvr_api.dll"), PeFixture.Build(PeFixture.MachineX64));
            steam.AddGame(steam.SteamRoot, 555, "Odd Game", "Odd Game");
            steam.AddGame(steam.SteamRoot, 1000, "Flat Game", "Flat Game");

            GameListResult result = GameListBuilder.BuildFrom(
                steam.SteamRoot,
                CompatibilityList.Empty,
                null,
                CancellationToken.None,
                installDir => installDir.EndsWith(@"\Odd Game", StringComparison.OrdinalIgnoreCase)
                    ? throw new InvalidOperationException("unexpected walk failure")
                    : GameClassifier.Classify(installDir));

            Assert.AreEqual(3, result.Entries.Count);
            Assert.AreEqual(GameKind.OpenVr, result.Entries[0].Classification.Kind);
            Assert.AreEqual(GameKind.Unknown, result.Entries[1].Classification.Kind);
            GameEntry odd = result.Entries[2];
            Assert.AreEqual("Odd Game", odd.Game.Name);
            Assert.AreEqual(GameKind.Unknown, odd.Classification.Kind);
            Assert.IsFalse(odd.Compatibility.Blocked);
            Assert.AreEqual(1, result.Warnings.Count);
            StringAssert.StartsWith(result.Warnings[0], "Odd Game: ");
            StringAssert.Contains(result.Warnings[0], "unexpected walk failure");
        }

        [TestMethod]
        public void BuildFrom_CancelledToken_ThrowsOperationCanceled()
        {
            steam.WriteLibraryFolders(SteamFixture.CurrentFormat(steam.SteamRoot));
            steam.AddGame(steam.SteamRoot, 620980, "Beat Saber", "Beat Saber");
            var progress = new ListProgress();

            Assert.ThrowsException<OperationCanceledException>(
                () => GameListBuilder.BuildFrom(steam.SteamRoot, CompatibilityList.Empty, progress, new CancellationToken(true)));
            Assert.AreEqual(0, progress.Reports.Count);
        }

        [TestMethod]
        public void BuildFrom_CancelledDuringScan_StopsBeforeTheNextGame()
        {
            steam.WriteLibraryFolders(SteamFixture.CurrentFormat(steam.SteamRoot));
            steam.AddGame(steam.SteamRoot, 1, "A Game", "A Game");
            steam.AddGame(steam.SteamRoot, 2, "B Game", "B Game");

            using (var cancellation = new CancellationTokenSource())
            {
                var progress = new ListProgress(name => cancellation.Cancel());

                Assert.ThrowsException<OperationCanceledException>(
                    () => GameListBuilder.BuildFrom(steam.SteamRoot, CompatibilityList.Empty, progress, cancellation.Token));
                CollectionAssert.AreEqual(new[] { "A Game" }, progress.Reports);
            }
        }

        [TestMethod]
        public void Build_SteamNotFound_ReturnsNullRootAndNoEntries()
        {
            GameListResult result = GameListBuilder.Build(new SteamLocator(new FakeRegistryReader()), CompatibilityList.Empty);

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

            GameListResult result = GameListBuilder.Build(new SteamLocator(registry), CompatibilityList.Empty);

            Assert.AreEqual(steam.SteamRoot, result.SteamRoot, true);
            Assert.AreEqual(1, result.Entries.Count);
        }

        private sealed class ListProgress : IProgress<string>
        {
            private readonly Action<string> onReport;

            public ListProgress(Action<string> onReport = null)
            {
                this.onReport = onReport;
            }

            public List<string> Reports { get; } = new List<string>();

            public void Report(string value)
            {
                Reports.Add(value);
                onReport?.Invoke(value);
            }
        }
    }
}
