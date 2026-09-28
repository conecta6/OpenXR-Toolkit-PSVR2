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

namespace GameManager.Tests
{
    [TestClass]
    public class SettingsStoreTests
    {
        private static readonly DateTime Accepted = new DateTime(2026, 9, 27, 8, 30, 0, DateTimeKind.Utc);
        private static readonly DateTime Checked = new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc);

        private TempDir temp;
        private string path;

        [TestInitialize]
        public void SetUp()
        {
            temp = new TempDir();
            path = temp.PathOf(@"AppData\settings.json");
        }

        [TestCleanup]
        public void TearDown()
        {
            temp.Dispose();
        }

        [TestMethod]
        public void Load_MissingFile_ReturnsDefaultsWithoutWarning()
        {
            var warnings = new List<string>();

            AppSettings settings = new SettingsStore(path).Load(warnings);

            Assert.IsFalse(settings.OpenCompositeLicenseAccepted);
            Assert.IsNull(settings.OpenCompositeLicenseAcceptedUtc);
            Assert.IsNull(settings.LastUpdateCheckUtc);
            Assert.AreEqual(0, warnings.Count);
            Assert.IsFalse(Directory.Exists(temp.PathOf("AppData")), "loading must not create anything");
        }

        [TestMethod]
        public void Save_ThenLoad_RoundTrips()
        {
            var store = new SettingsStore(path);
            store.Save(new AppSettings
            {
                OpenCompositeLicenseAccepted = true,
                OpenCompositeLicenseAcceptedUtc = Accepted,
                LastUpdateCheckUtc = Checked,
            });
            var warnings = new List<string>();

            AppSettings loaded = store.Load(warnings);

            Assert.AreEqual(0, warnings.Count);
            Assert.IsTrue(loaded.OpenCompositeLicenseAccepted);
            Assert.AreEqual((DateTime?)Accepted, loaded.OpenCompositeLicenseAcceptedUtc);
            Assert.AreEqual((DateTime?)Checked, loaded.LastUpdateCheckUtc);
            Assert.AreEqual(DateTimeKind.Utc, loaded.LastUpdateCheckUtc.Value.Kind);
            StringAssert.Contains(File.ReadAllText(path), "2026-09-28T10:00:00Z");
        }

        [TestMethod]
        public void Save_CreatesFolderAndLeavesNoTempFile()
        {
            new SettingsStore(path).Save(new AppSettings { OpenCompositeLicenseAccepted = true });

            CollectionAssert.AreEqual(new[] { path }, Directory.GetFiles(temp.PathOf("AppData")));
        }

        [TestMethod]
        public void Save_OverwritesExistingFile()
        {
            var store = new SettingsStore(path);
            store.Save(new AppSettings { OpenCompositeLicenseAccepted = true, LastUpdateCheckUtc = Checked });

            store.Save(new AppSettings { OpenCompositeLicenseAccepted = false });

            AppSettings loaded = store.Load(new List<string>());
            Assert.IsFalse(loaded.OpenCompositeLicenseAccepted);
            Assert.IsNull(loaded.LastUpdateCheckUtc);
            Assert.AreEqual(1, Directory.GetFiles(temp.PathOf("AppData")).Length);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("not json")]
        [DataRow("null")]
        [DataRow("{ \"openCompositeLicenseAccepted\": \"yes\" }")]
        public void Load_CorruptFile_ReturnsDefaultsWithWarning(string content)
        {
            temp.WriteText(@"AppData\settings.json", content);
            var warnings = new List<string>();

            AppSettings settings = new SettingsStore(path).Load(warnings);

            Assert.IsFalse(settings.OpenCompositeLicenseAccepted);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings[0], "Default settings are used");
        }

        [TestMethod]
        public void Load_UnreadableTimestamp_IsTreatedAsNeverChecked()
        {
            temp.WriteText(@"AppData\settings.json", "{ \"openCompositeLicenseAccepted\": true, \"lastOpenCompositeCheckUtc\": \"yesterday\" }");
            var warnings = new List<string>();

            AppSettings settings = new SettingsStore(path).Load(warnings);

            Assert.IsTrue(settings.OpenCompositeLicenseAccepted);
            Assert.IsNull(settings.LastUpdateCheckUtc);
            Assert.AreEqual(0, warnings.Count);
        }
    }
}
