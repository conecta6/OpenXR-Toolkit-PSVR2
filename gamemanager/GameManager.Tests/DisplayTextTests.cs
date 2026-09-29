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
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    [TestClass]
    public class DisplayTextTests
    {
        [TestMethod]
        public void Kind_HasReadableNames()
        {
            Assert.AreEqual("OpenVR", DisplayText.Kind(GameKind.OpenVr));
            Assert.AreEqual("OpenXR (probable)", DisplayText.Kind(GameKind.OpenXrProbable));
            Assert.AreEqual("Unknown", DisplayText.Kind(GameKind.Unknown));
        }

        [TestMethod]
        public void Machine_HasReadableNames()
        {
            Assert.AreEqual("x86", DisplayText.Machine(PeMachine.X86));
            Assert.AreEqual("x64", DisplayText.Machine(PeMachine.X64));
            Assert.AreEqual("ARM64", DisplayText.Machine(PeMachine.Arm64));
            Assert.AreEqual("unknown", DisplayText.Machine(PeMachine.Unknown));
        }

        [TestMethod]
        public void OpenVrDlls_JoinsArchitectureAndPathWithSemicolons()
        {
            var dlls = new[]
            {
                new OpenVrDll(@"Beat Saber_Data\Plugins\x86\openvr_api.dll", PeMachine.X86),
                new OpenVrDll(@"Beat Saber_Data\Plugins\x86_64\openvr_api.dll", PeMachine.X64),
            };

            Assert.AreEqual(
                @"x86: Beat Saber_Data\Plugins\x86\openvr_api.dll; x64: Beat Saber_Data\Plugins\x86_64\openvr_api.dll",
                DisplayText.OpenVrDlls(dlls));
        }

        [TestMethod]
        public void OpenVrDlls_Empty_IsEmptyString()
        {
            Assert.AreEqual("", DisplayText.OpenVrDlls(new OpenVrDll[0]));
        }

        [TestMethod]
        public void Compatibility_HasReadableNames()
        {
            Assert.AreEqual("Works", DisplayText.Compatibility(CompatibilityVerdict.For(new CompatibilityEntry(1, "A", CompatibilityStatus.Works, ""), new string[0], new string[0])));
            Assert.AreEqual("Broken", DisplayText.Compatibility(CompatibilityVerdict.For(new CompatibilityEntry(1, "A", CompatibilityStatus.Broken, ""), new string[0], new string[0])));
            Assert.AreEqual("Untested", DisplayText.Compatibility(CompatibilityVerdict.For(null, new string[0], new string[0])));
            Assert.AreEqual("Anti-cheat — blocked", DisplayText.Compatibility(CompatibilityVerdict.For(new CompatibilityEntry(1, "A", CompatibilityStatus.AntiCheat, ""), new string[0], new string[0])));
            Assert.AreEqual("Anti-cheat — blocked", DisplayText.Compatibility(CompatibilityVerdict.For(new CompatibilityEntry(1, "A", CompatibilityStatus.Works, ""), new[] { "BattlEye" }, new string[0])));
        }

        [TestMethod]
        public void CompatibilityDetails_JoinsReasonAndNotes()
        {
            var entry = new CompatibilityEntry(438100, "VRChat", CompatibilityStatus.AntiCheat, "Ships EasyAntiCheat.");

            Assert.AreEqual(
                "Listed as an anti-cheat game in compatibility.json. Anti-cheat files found: EasyAntiCheat. Notes: Ships EasyAntiCheat.",
                DisplayText.CompatibilityDetails(CompatibilityVerdict.For(entry, new[] { "EasyAntiCheat" }, new string[0])));
            Assert.AreEqual("", DisplayText.CompatibilityDetails(CompatibilityVerdict.For(null, new string[0], new string[0])));
        }

        [TestMethod]
        public void OpenCompositeLicenseNotice_NamesLicenseAuthorsSourceAndLink()
        {
            string notice = DisplayText.OpenCompositeLicenseNotice;

            StringAssert.Contains(notice, "GPLv3");
            StringAssert.Contains(notice, "its own authors");
            StringAssert.Contains(notice, "unmodified");
            StringAssert.Contains(notice, "znix.xyz");
            StringAssert.Contains(notice, "https://gitlab.com/znixian/OpenOVR");
            Assert.AreEqual("https://gitlab.com/znixian/OpenOVR", DisplayText.OpenCompositeSourceUrl);
        }

        [TestMethod]
        public void OpenCompositeSummary_NothingCached_SaysNotDownloaded()
        {
            Assert.AreEqual("OpenComposite: not downloaded", DisplayText.OpenCompositeSummary(new CachedBuild[0]));
        }

        [TestMethod]
        public void OpenCompositeSummary_ListsBuildsAndPendingUpdate()
        {
            var builds = new[]
            {
                new CachedBuild(OpenCompositeArch.X64, @"C:\cache\x64\openvr_api.dll", "aa", new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc), "u", "bb", new DateTime(2026, 9, 30, 9, 0, 0, DateTimeKind.Utc)),
                new CachedBuild(OpenCompositeArch.X86, @"C:\cache\x86\openvr_api.dll", "cc", null, "u", null, null),
            };

            Assert.AreEqual(
                "OpenComposite: x64 downloaded 2026-09-28 (new build available), x86 downloaded (date unknown)",
                DisplayText.OpenCompositeSummary(builds));
        }

        [TestMethod]
        public void CompatibilityDetails_AntiCheatNotRuledOut_NamesTheFolders()
        {
            CompatibilityVerdict verdict = CompatibilityVerdict.For(null, new string[0], new[] { @"C:\G\Link", @"C:\G\Locked" });

            Assert.AreEqual(
                @"Anti-cheat not ruled out: these folders could not be checked: C:\G\Link, C:\G\Locked.",
                DisplayText.CompatibilityDetails(verdict));
        }

        [TestMethod]
        public void PlanSummary_ListsQuestionsStepsAndNotes_OrWhyItCannotRun()
        {
            string nl = Environment.NewLine;
            var game = new SteamGame(620980, "Beat Saber", @"C:\Games\Beat Saber", @"C:\Games");
            string dll = @"C:\Games\Beat Saber\openvr_api.dll";
            var change = new DllChange(dll, new[] { PatchAction.Verify(dll, new string('a', 64)), PatchAction.Backup(dll, dll + ".bak") }, null, false);
            var plan = new PatchPlan(PatchOperation.Patch, game, new[] { change }, null, new[] { "Sure?" }, new[] { "A note." });
            var blocked = new PatchPlan(PatchOperation.Restore, game, new[] { change }, new[] { "No backup." }, null, null);

            Assert.AreEqual(
                "Patch Beat Saber (620980)" + nl + nl
                + "Asks first (default No):" + nl + "- Sure?" + nl + nl
                + "Steps:" + nl + "1. Check " + dll + " (SHA-256 aaaaaaaaaaaa...)" + nl + "2. Back up " + dll + " as openvr_api.dll.bak" + nl + nl
                + "Notes:" + nl + "- A note.",
                DisplayText.PlanSummary(plan));
            Assert.AreEqual(
                "Restore Beat Saber (620980)" + nl + nl + "Not possible:" + nl + "- No backup.",
                DisplayText.PlanSummary(blocked));
        }
    }
}
