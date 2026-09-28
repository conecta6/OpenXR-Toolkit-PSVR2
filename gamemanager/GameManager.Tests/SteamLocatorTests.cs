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
using Microsoft.Win32;

namespace GameManager.Tests
{
    [TestClass]
    public class SteamLocatorTests
    {
        private const string UserKey = @"Software\Valve\Steam";
        private const string MachineKey = @"SOFTWARE\Valve\Steam";

        private TempDir temp;
        private FakeRegistryReader registry;

        [TestInitialize]
        public void SetUp()
        {
            temp = new TempDir();
            registry = new FakeRegistryReader();
        }

        [TestCleanup]
        public void TearDown()
        {
            temp.Dispose();
        }

        [TestMethod]
        public void FindSteamRoot_HkcuSteamPathWithForwardSlashes_IsNormalized()
        {
            string steam = temp.CreateDirectory("Steam");
            registry.Set(RegistryHive.CurrentUser, RegistryView.Default, UserKey, "SteamPath", steam.Replace('\\', '/').ToLowerInvariant());

            string root = new SteamLocator(registry).FindSteamRoot();

            Assert.AreEqual(steam, root, true);
            Assert.IsFalse(root.Contains("/"));
        }

        [TestMethod]
        public void FindSteamRoot_NoHkcu_FallsBackToHklm32()
        {
            string steam = temp.CreateDirectory("Steam");
            registry.Set(RegistryHive.LocalMachine, RegistryView.Registry32, MachineKey, "InstallPath", steam);

            Assert.AreEqual(steam, new SteamLocator(registry).FindSteamRoot(), true);
        }

        [TestMethod]
        public void FindSteamRoot_HkcuFolderMissing_FallsBackToHklm()
        {
            string steam = temp.CreateDirectory("Steam");
            registry.Set(RegistryHive.CurrentUser, RegistryView.Default, UserKey, "SteamPath", temp.PathOf("OldSteam"));
            registry.Set(RegistryHive.LocalMachine, RegistryView.Registry32, MachineKey, "InstallPath", steam);

            Assert.AreEqual(steam, new SteamLocator(registry).FindSteamRoot(), true);
        }

        [TestMethod]
        public void FindSteamRoot_NothingInRegistry_ReturnsNull()
        {
            Assert.IsNull(new SteamLocator(registry).FindSteamRoot());
        }

        [TestMethod]
        public void FindSteamRoot_BothFoldersMissing_ReturnsNull()
        {
            registry.Set(RegistryHive.CurrentUser, RegistryView.Default, UserKey, "SteamPath", temp.PathOf("Gone1"));
            registry.Set(RegistryHive.LocalMachine, RegistryView.Registry32, MachineKey, "InstallPath", temp.PathOf("Gone2"));

            Assert.IsNull(new SteamLocator(registry).FindSteamRoot());
        }

        [TestMethod]
        public void FindSteamRoot_InvalidPathInRegistry_ReturnsNull()
        {
            registry.Set(RegistryHive.CurrentUser, RegistryView.Default, UserKey, "SteamPath", "C:\\bad|path");

            Assert.IsNull(new SteamLocator(registry).FindSteamRoot());
        }

        [TestMethod]
        public void WindowsRegistryReader_MissingKey_ReturnsNull()
        {
            string subKey = @"Software\GameManagerTests\" + Guid.NewGuid().ToString("N");

            Assert.IsNull(new WindowsRegistryReader().ReadString(RegistryHive.CurrentUser, RegistryView.Default, subKey, "Missing"));
        }
    }
}
