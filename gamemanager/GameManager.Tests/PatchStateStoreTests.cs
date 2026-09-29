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
using System.Linq;
using System.Text;
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    [TestClass]
    public class PatchStateStoreTests
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 29, 10, 15, 0, DateTimeKind.Utc);
        private static readonly string HashA = new string('a', 64);
        private static readonly string HashB = new string('b', 64);

        private TempDir temp;
        private string path;
        private PatchStateStore store;

        [TestInitialize]
        public void SetUp()
        {
            temp = new TempDir();
            path = temp.PathOf(@"AppData\state.json");
            store = new PatchStateStore(path, () => Now);
        }

        [TestCleanup]
        public void TearDown()
        {
            temp.Dispose();
        }

        private static PatchRecord Record(string dllPath, int appId)
        {
            return new PatchRecord(appId, "Beat Saber", @"C:\Games\Beat Saber", dllPath, OpenCompositeArch.X64, HashA, HashB, false, Now);
        }

        [TestMethod]
        public void Load_NoFile_IsEmptyAndSavable()
        {
            var warnings = new List<string>();

            PatchState state = store.Load(warnings);

            Assert.AreEqual(0, state.Records.Count);
            Assert.IsTrue(state.CanSave);
            Assert.AreEqual(0, warnings.Count);
        }

        [TestMethod]
        public void SaveThenLoad_RoundTripsEveryField()
        {
            PatchState state = store.Load(new List<string>());
            state.Put(new PatchRecord(
                777, "\u6771\u65b9 VR", @"D:\Lib\steamapps\common\Touhou", @"D:\Lib\steamapps\common\Touhou\x86\openvr_api.dll",
                OpenCompositeArch.X86, HashA, HashB, true, Now));

            store.Save(state);
            var warnings = new List<string>();
            PatchRecord loaded = store.Load(warnings).Find(@"D:\Lib\steamapps\common\Touhou\x86\openvr_api.dll");

            Assert.AreEqual(0, warnings.Count, string.Join("\n", warnings));
            Assert.AreEqual(777, loaded.AppId);
            Assert.AreEqual("\u6771\u65b9 VR", loaded.GameName);
            Assert.AreEqual(@"D:\Lib\steamapps\common\Touhou", loaded.InstallDir);
            Assert.AreEqual(OpenCompositeArch.X86, loaded.Arch);
            Assert.AreEqual(HashA, loaded.OriginalSha256);
            Assert.AreEqual(HashB, loaded.OpenCompositeSha256);
            Assert.IsTrue(loaded.IniCreated);
            Assert.AreEqual(Now, loaded.PatchedUtc);
            StringAssert.Contains(File.ReadAllText(path), "\"version\"");
        }

        [TestMethod]
        public void Put_SameAppIdTwoDlls_KeepsBothRecords()
        {
            // R4: keyed by DLL path, never by AppId (a Unity game ships an x86 and an x64 DLL).
            PatchState state = store.Load(new List<string>());
            state.Put(Record(@"C:\Games\Beat Saber\Plugins\x86\openvr_api.dll", 620980));
            state.Put(Record(@"C:\Games\Beat Saber\Plugins\x86_64\openvr_api.dll", 620980));

            store.Save(state);

            Assert.AreEqual(2, store.Load(new List<string>()).Records.Count);
        }

        [TestMethod]
        public void Put_SameDllPathInOtherCaseOrSlashes_ReplacesTheRecord()
        {
            PatchState state = store.Load(new List<string>());
            state.Put(Record(@"C:\Games\A\openvr_api.dll", 1));
            state.Put(Record("c:/games/a/OPENVR_API.DLL", 2));

            Assert.AreEqual(1, state.Records.Count);
            Assert.AreEqual(2, state.Find(@"C:\Games\A\openvr_api.dll").AppId);
        }

        [TestMethod]
        public void Load_CorruptFile_IsKeptAsideAndStartsEmpty()
        {
            temp.WriteText(@"AppData\state.json", "{ not json");
            var warnings = new List<string>();

            PatchState state = store.Load(warnings);

            string aside = path + ".corrupt-20260929-101500";
            Assert.AreEqual(0, state.Records.Count);
            Assert.IsTrue(state.CanSave);
            Assert.IsFalse(File.Exists(path));
            Assert.AreEqual("{ not json", File.ReadAllText(aside));
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings[0], aside);
        }

        [TestMethod]
        public void Load_WrongVersion_IsKeptAside()
        {
            temp.WriteText(@"AppData\state.json", "{ \"version\": 2, \"records\": [] }");
            var warnings = new List<string>();

            PatchState state = store.Load(warnings);

            Assert.AreEqual(0, state.Records.Count);
            Assert.IsTrue(File.Exists(path + ".corrupt-20260929-101500"));
            StringAssert.Contains(warnings[0], "version is 2");
        }

        [TestMethod]
        public void Load_FileLockedByAnotherProgram_WarnsAndRefusesToSave()
        {
            PatchState first = store.Load(new List<string>());
            first.Put(Record(@"C:\Games\A\openvr_api.dll", 1));
            store.Save(first);
            var warnings = new List<string>();

            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                PatchState state = store.Load(warnings);

                Assert.IsFalse(state.CanSave);
                Assert.AreEqual(1, warnings.Count);
                Assert.ThrowsException<InvalidOperationException>(() => store.Save(state));
            }

            Assert.AreEqual(1, store.Load(new List<string>()).Records.Count, "the unreadable file must not have been replaced");
        }

        [TestMethod]
        public void Load_InvalidRecord_IsSkippedWithWarningAndOthersKept()
        {
            temp.WriteText(@"AppData\state.json",
                "{ \"version\": 1, \"records\": [" +
                " { \"dllPath\": \"C:\\\\Games\\\\A\\\\openvr_api.dll\", \"arch\": \"x64\", \"originalSha256\": \"" + HashA + "\", \"openCompositeSha256\": \"" + HashB + "\" }," +
                " { \"dllPath\": \"C:\\\\Games\\\\B\\\\openvr_api.dll\", \"arch\": \"x64\", \"originalSha256\": \"not a hash\", \"openCompositeSha256\": \"" + HashB + "\" } ] }");
            var warnings = new List<string>();

            PatchState state = store.Load(warnings);

            Assert.AreEqual(1, state.Records.Count);
            Assert.IsNotNull(state.Find(@"C:\Games\A\openvr_api.dll"));
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings[0], @"C:\Games\B\openvr_api.dll");
        }

        [TestMethod]
        public void Save_LeavesNoTemporaryFiles()
        {
            PatchState state = store.Load(new List<string>());
            state.Put(Record(@"C:\Games\A\openvr_api.dll", 1));

            store.Save(state);
            store.Save(state);

            CollectionAssert.AreEqual(
                new[] { "state.json" },
                Directory.GetFiles(Path.GetDirectoryName(path)).Select(Path.GetFileName).ToArray());
        }

        [TestMethod]
        public void Load_FileWithByteOrderMark_Loads()
        {
            temp.WriteText(@"AppData\state.json",
                "{ \"version\": 1, \"records\": [ { \"dllPath\": \"C:\\\\Games\\\\A\\\\openvr_api.dll\", \"arch\": \"x86\", \"originalSha256\": \"" + HashA + "\", \"openCompositeSha256\": \"" + HashB + "\", \"iniCreated\": true, \"patchedUtc\": \"2026-09-29T10:15:00Z\" } ] }",
                new UTF8Encoding(true));
            var warnings = new List<string>();

            PatchState state = store.Load(warnings);

            Assert.AreEqual(0, warnings.Count, string.Join("\n", warnings));
            Assert.AreEqual(OpenCompositeArch.X86, state.Find(@"C:\Games\A\openvr_api.dll").Arch);
        }

        [TestMethod]
        public void PatchRecord_PathThatCannotBeNormalized_ThrowsArgumentException()
        {
            Assert.ThrowsException<ArgumentException>(() => Record(@"C:\Games\bad|name\openvr_api.dll", 1));
            Assert.ThrowsException<ArgumentException>(() => Record(@"Games\openvr_api.dll", 1));
            Assert.ThrowsException<ArgumentException>(() => Record(null, 1));
        }

        [TestMethod]
        public void Load_RecordWithUnnormalizablePath_IsSkippedWithWarningAndOthersKept()
        {
            temp.WriteText(@"AppData\state.json",
                "{ \"version\": 1, \"records\": [" +
                " { \"dllPath\": \"C:\\\\Games\\\\A\\\\openvr_api.dll\", \"arch\": \"x64\", \"originalSha256\": \"" + HashA + "\", \"openCompositeSha256\": \"" + HashB + "\" }," +
                " { \"dllPath\": \"C:\\\\Games\\\\bad|name\\\\openvr_api.dll\", \"arch\": \"x64\", \"originalSha256\": \"" + HashA + "\", \"openCompositeSha256\": \"" + HashB + "\" } ] }");
            var warnings = new List<string>();

            PatchState state = store.Load(warnings);

            Assert.AreEqual(1, state.Records.Count);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings[0], "bad|name");
            Assert.IsTrue(state.CanSave);
        }

        [TestMethod]
        public void Load_CorruptFileAndAsideNameTaken_UsesGuidSuffixAndKeepsBothFiles()
        {
            string taken = path + ".corrupt-20260929-101500";
            temp.WriteText(@"AppData\state.json.corrupt-20260929-101500", "older corrupt file");
            temp.WriteText(@"AppData\state.json", "{ not json");
            var warnings = new List<string>();

            PatchState state = store.Load(warnings);

            Assert.IsTrue(state.CanSave);
            Assert.IsFalse(File.Exists(path));
            Assert.AreEqual("older corrupt file", File.ReadAllText(taken));
            string[] others = Directory.GetFiles(Path.GetDirectoryName(path), "state.json.corrupt-*")
                .Where(f => !string.Equals(f, taken, StringComparison.OrdinalIgnoreCase)).ToArray();
            Assert.AreEqual(1, others.Length);
            StringAssert.StartsWith(others[0], taken + "-");
            Assert.AreEqual("{ not json", File.ReadAllText(others[0]));
            StringAssert.Contains(warnings[0], others[0]);
        }

        [TestMethod]
        public void Load_CorruptFileThatCannotBeSetAside_RefusesToSave()
        {
            temp.WriteText(@"AppData\state.json", "{ not json");
            var warnings = new List<string>();

            // Readable by others, but it cannot be renamed while this handle is open.
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                PatchState state = store.Load(warnings);

                Assert.IsFalse(state.CanSave);
                Assert.AreEqual(1, warnings.Count);
                StringAssert.Contains(warnings[0], "could not be set aside");
                Assert.ThrowsException<InvalidOperationException>(() => store.Save(state));
            }

            Assert.AreEqual("{ not json", File.ReadAllText(path));
        }

        private string TwoBadRecordsAndOneGood()
        {
            return "{ \"version\": 1, \"records\": ["
                + " { \"dllPath\": \"C:\\\\Games\\\\A\\\\openvr_api.dll\", \"arch\": \"x64\", \"originalSha256\": \"" + HashA + "\", \"openCompositeSha256\": \"" + HashB + "\" },"
                + " { \"dllPath\": \"C:\\\\Games\\\\B\\\\openvr_api.dll\", \"arch\": \"x64\", \"originalSha256\": \"not a hash\", \"openCompositeSha256\": \"" + HashB + "\" },"
                + " { \"dllPath\": \"C:\\\\Games\\\\C\\\\openvr_api.dll\", \"arch\": \"bogus\", \"originalSha256\": \"" + HashA + "\", \"openCompositeSha256\": \"" + HashB + "\" } ] }";
        }

        [TestMethod]
        public void Load_SkippedRecord_KeepsACopyOfTheFileAsItWas()
        {
            string json = TwoBadRecordsAndOneGood();
            temp.WriteText(@"AppData\state.json", json);
            var warnings = new List<string>();

            PatchState state = store.Load(warnings);

            string copy = path + ".skipped-20260929-101500";
            Assert.AreEqual(1, state.Records.Count);
            Assert.AreEqual(json, File.ReadAllText(copy));
            Assert.AreEqual(json, File.ReadAllText(path), "loading must not rewrite state.json");
            Assert.AreEqual(2, warnings.Count);
            Assert.IsTrue(warnings.All(w => w.Contains(copy)), string.Join("\n", warnings));
            Assert.AreEqual(1, Directory.GetFiles(Path.GetDirectoryName(path), "state.json.skipped-*").Length, "one copy per load, not one per record");
        }

        [TestMethod]
        public void Load_SkippedRecordAndCopyNameTaken_UsesGuidSuffix()
        {
            string taken = path + ".skipped-20260929-101500";
            temp.WriteText(@"AppData\state.json.skipped-20260929-101500", "older copy");
            temp.WriteText(@"AppData\state.json", TwoBadRecordsAndOneGood());

            PatchState state = store.Load(new List<string>());

            Assert.IsTrue(state.CanSave);
            Assert.AreEqual("older copy", File.ReadAllText(taken));
            Assert.AreEqual(2, Directory.GetFiles(Path.GetDirectoryName(path), "state.json.skipped-*").Length);
        }

        [TestMethod]
        public void KeyOf_DeviceRootedPath_MatchesThePlainPath()
        {
            Assert.AreEqual(PatchState.KeyOf(@"C:\x\openvr_api.dll"), PatchState.KeyOf(@"\\?\C:\x\openvr_api.dll"));
            Assert.AreEqual(PatchState.KeyOf(@"\\server\share\openvr_api.dll"), PatchState.KeyOf(@"\\?\UNC\server\share\openvr_api.dll"));
            var state = new PatchState(true);
            state.Put(Record(@"\\?\C:\Games\A\openvr_api.dll", 1));
            Assert.IsNotNull(state.Find(@"c:\games\a\OPENVR_API.dll"));
        }
    }
}
