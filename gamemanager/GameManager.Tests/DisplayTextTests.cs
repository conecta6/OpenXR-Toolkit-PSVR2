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
            Assert.AreEqual("Works", DisplayText.Compatibility(CompatibilityVerdict.For(new CompatibilityEntry(1, "A", CompatibilityStatus.Works, ""), new string[0])));
            Assert.AreEqual("Broken", DisplayText.Compatibility(CompatibilityVerdict.For(new CompatibilityEntry(1, "A", CompatibilityStatus.Broken, ""), new string[0])));
            Assert.AreEqual("Untested", DisplayText.Compatibility(CompatibilityVerdict.For(null, new string[0])));
            Assert.AreEqual("Anti-cheat — blocked", DisplayText.Compatibility(CompatibilityVerdict.For(new CompatibilityEntry(1, "A", CompatibilityStatus.AntiCheat, ""), new string[0])));
            Assert.AreEqual("Anti-cheat — blocked", DisplayText.Compatibility(CompatibilityVerdict.For(new CompatibilityEntry(1, "A", CompatibilityStatus.Works, ""), new[] { "BattlEye" })));
        }

        [TestMethod]
        public void CompatibilityDetails_JoinsReasonAndNotes()
        {
            var entry = new CompatibilityEntry(438100, "VRChat", CompatibilityStatus.AntiCheat, "Ships EasyAntiCheat.");

            Assert.AreEqual(
                "Listed as an anti-cheat game in compatibility.json. Anti-cheat files found: EasyAntiCheat. Notes: Ships EasyAntiCheat.",
                DisplayText.CompatibilityDetails(CompatibilityVerdict.For(entry, new[] { "EasyAntiCheat" })));
            Assert.AreEqual("", DisplayText.CompatibilityDetails(CompatibilityVerdict.For(null, new string[0])));
        }
    }
}
