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

using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    [TestClass]
    public class CompatibilityVerdictTests
    {
        [TestMethod]
        public void For_NotListedNoMarkers_IsUntestedAndNotBlocked()
        {
            CompatibilityVerdict verdict = CompatibilityVerdict.For(null, new string[0]);

            Assert.AreEqual(CompatibilityStatus.Untested, verdict.ListStatus);
            Assert.IsFalse(verdict.Blocked);
            Assert.AreEqual("", verdict.Reason);
            Assert.AreEqual("", verdict.Notes);
        }

        [TestMethod]
        public void For_ListedAntiCheat_IsBlocked()
        {
            var entry = new CompatibilityEntry(438100, "VRChat", CompatibilityStatus.AntiCheat, "EasyAntiCheat");

            CompatibilityVerdict verdict = CompatibilityVerdict.For(entry, new string[0]);

            Assert.IsTrue(verdict.Blocked);
            Assert.AreEqual(CompatibilityStatus.AntiCheat, verdict.ListStatus);
            Assert.AreEqual("Listed as an anti-cheat game in compatibility.json.", verdict.Reason);
            Assert.AreEqual("EasyAntiCheat", verdict.Notes);
        }

        [TestMethod]
        public void For_ListedWorksButMarkersFound_IsBlocked()
        {
            var entry = new CompatibilityEntry(1, "Game", CompatibilityStatus.Works, "");

            CompatibilityVerdict verdict = CompatibilityVerdict.For(entry, new[] { "EasyAntiCheat", @"EasyAntiCheat\EasyAntiCheat_EOS_Setup.exe" });

            Assert.IsTrue(verdict.Blocked);
            Assert.AreEqual(CompatibilityStatus.Works, verdict.ListStatus);
            Assert.AreEqual(@"Anti-cheat files found: EasyAntiCheat, EasyAntiCheat\EasyAntiCheat_EOS_Setup.exe.", verdict.Reason);
        }

        [TestMethod]
        public void For_ListedBroken_IsNotBlocked()
        {
            var entry = new CompatibilityEntry(546560, "Half-Life: Alyx", CompatibilityStatus.Broken, "Input");

            CompatibilityVerdict verdict = CompatibilityVerdict.For(entry, new string[0]);

            Assert.IsFalse(verdict.Blocked);
            Assert.AreEqual(CompatibilityStatus.Broken, verdict.ListStatus);
            Assert.AreEqual("Input", verdict.Notes);
        }

        [TestMethod]
        public void For_UncheckedFolders_AntiCheatNotRuledOutButNotBlocked()
        {
            CompatibilityVerdict verdict = CompatibilityVerdict.For(null, new string[0], new[] { @"C:\Games\X\Link" });

            Assert.IsFalse(verdict.Blocked);
            Assert.IsTrue(verdict.AntiCheatNotRuledOut);
            Assert.AreEqual(1, verdict.UncheckedFolders.Count);
            Assert.AreEqual(@"C:\Games\X\Link", verdict.UncheckedFolders[0]);
        }

        [TestMethod]
        public void For_WithoutUncheckedFolders_AntiCheatIsRuledOut()
        {
            CompatibilityVerdict verdict = CompatibilityVerdict.For(null, new string[0]);

            Assert.IsFalse(verdict.AntiCheatNotRuledOut);
            Assert.AreEqual(0, verdict.UncheckedFolders.Count);
        }
    }
}
