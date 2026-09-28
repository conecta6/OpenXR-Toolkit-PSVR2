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
    public class AntiCheatDetectorTests
    {
        [TestMethod]
        [DataRow("EasyAntiCheat", true)]
        [DataRow("easyanticheat_eos", true)]
        [DataRow("BATTLEYE", true)]
        [DataRow("EasyAntiCheat_EOS_Setup.exe", false)]
        [DataRow("EasyAntiCheat.sys", false)]
        [DataRow("easyanticheat_x64.dll", false)]
        [DataRow("start_protected_game.exe", false)]
        [DataRow("START_PROTECTED_GAME.EXE", false)]
        [DataRow("BEService_x64.exe", false)]
        [DataRow("BEClient_x64.dll", false)]
        public void IsMarker_KnownMarkers_AreDetected(string name, bool isDirectory)
        {
            Assert.IsTrue(AntiCheatDetector.IsMarker(name, isDirectory));
        }

        [TestMethod]
        [DataRow("EasyAntiCheat", false)]
        [DataRow("EasyAntiCheat.txt", false)]
        [DataRow("EasyAntiCheatSettings", true)]
        [DataRow("BattlEye", false)]
        [DataRow("BEService.dll", false)]
        [DataRow("BEClient.exe", false)]
        [DataRow("start_protected_game.exe", true)]
        [DataRow("NoEasyAntiCheat.exe", false)]
        public void IsMarker_LookAlikes_AreNotMarkers(string name, bool isDirectory)
        {
            Assert.IsFalse(AntiCheatDetector.IsMarker(name, isDirectory));
        }
    }
}
