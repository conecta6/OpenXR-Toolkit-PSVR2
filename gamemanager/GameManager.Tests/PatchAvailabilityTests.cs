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
    public class PatchAvailabilityTests
    {
        private static GameEntry Entry(GameKind kind, CompatibilityVerdict verdict)
        {
            var game = new SteamGame(1, "A", @"C:\Games\A", @"C:\Games");
            OpenVrDll[] dlls = kind == GameKind.OpenVr ? new[] { new OpenVrDll("openvr_api.dll", PeMachine.X64) } : new OpenVrDll[0];
            return new GameEntry(game, new GameClassification(kind, dlls, false, new string[0], new string[0], new string[0]), verdict);
        }

        private static PatchRecord Record()
        {
            return new PatchRecord(1, "A", @"C:\Games\A", @"C:\Games\A\openvr_api.dll", OpenCompositeArch.X64, new string('a', 64), new string('b', 64), false, DateTime.UtcNow);
        }

        [TestMethod]
        public void CanPatch_OpenVrGameNotBlocked_IsTrue()
        {
            Assert.IsTrue(PatchAvailability.CanPatch(Entry(GameKind.OpenVr, CompatibilityVerdict.For(null, new string[0], new string[0]))));
        }

        [TestMethod]
        public void CanPatch_BlockedGame_IsFalse()
        {
            Assert.IsFalse(PatchAvailability.CanPatch(Entry(GameKind.OpenVr, CompatibilityVerdict.For(null, new[] { "BattlEye" }, new string[0]))));
        }

        [TestMethod]
        public void CanPatch_NotOpenVrOrNothingSelected_IsFalse()
        {
            Assert.IsFalse(PatchAvailability.CanPatch(Entry(GameKind.OpenXrProbable, CompatibilityVerdict.For(null, new string[0], new string[0]))));
            Assert.IsFalse(PatchAvailability.CanPatch(null));
        }

        [TestMethod]
        public void CanPatch_AntiCheatNotRuledOut_IsTrueBecausePatchAsksFirst()
        {
            Assert.IsTrue(PatchAvailability.CanPatch(Entry(GameKind.OpenVr, CompatibilityVerdict.For(null, new string[0], new[] { "bin" }))));
        }

        [TestMethod]
        public void CanRestore_BlockedGameWithARecord_IsTrue()
        {
            // Restoring removes OpenComposite, so it stays possible for a game later found to have anti-cheat.
            GameEntry blocked = Entry(GameKind.OpenVr, CompatibilityVerdict.For(null, new[] { "EasyAntiCheat" }, new string[0]));
            var status = new GamePatchStatus(blocked, PatchStatus.Patched, new[] { Record() }, new[] { PatchStatus.Patched }, new string[0]);

            Assert.IsTrue(PatchAvailability.CanRestore(status));
        }

        [TestMethod]
        public void CanRestore_NoRecordOrNothingSelected_IsFalse()
        {
            GameEntry entry = Entry(GameKind.OpenVr, CompatibilityVerdict.For(null, new string[0], new string[0]));
            var status = new GamePatchStatus(entry, PatchStatus.NotPatched, new PatchRecord[0], new PatchStatus[0], new string[0]);

            Assert.IsFalse(PatchAvailability.CanRestore(status));
            Assert.IsFalse(PatchAvailability.CanRestore(null));
        }
    }
}
