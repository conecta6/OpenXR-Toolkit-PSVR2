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

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    [TestClass]
    public class CompatibilityListTests
    {
        private TempDir temp;

        [TestInitialize]
        public void SetUp()
        {
            temp = new TempDir();
        }

        [TestCleanup]
        public void TearDown()
        {
            temp.Dispose();
        }

        private static CompatibilityList Parse(string json, List<string> warnings)
        {
            return CompatibilityList.Parse(json, "compatibility.json", warnings);
        }

        [TestMethod]
        public void Parse_ValidFile_FindsEntriesByAppId()
        {
            var warnings = new List<string>();

            CompatibilityList list = Parse(
                "{ \"version\": 1, \"games\": [" +
                " { \"appId\": 438100, \"name\": \"VRChat\", \"status\": \"anticheat\", \"notes\": \"EasyAntiCheat\" }," +
                " { \"appId\": 620980, \"name\": \"Beat Saber\", \"status\": \"works\" }," +
                " { \"appId\": 546560, \"name\": \"Half-Life: Alyx\", \"status\": \"broken\", \"notes\": \"Input\", \"extra\": 5 } ] }",
                warnings);

            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual(3, list.Count);
            CompatibilityEntry vrChat = list.Find(438100);
            Assert.AreEqual("VRChat", vrChat.Name);
            Assert.AreEqual(CompatibilityStatus.AntiCheat, vrChat.Status);
            Assert.AreEqual("EasyAntiCheat", vrChat.Notes);
            Assert.AreEqual(CompatibilityStatus.Works, list.Find(620980).Status);
            Assert.AreEqual("", list.Find(620980).Notes);
            Assert.AreEqual(CompatibilityStatus.Broken, list.Find(546560).Status);
            Assert.IsNull(list.Find(1));
        }

        [TestMethod]
        public void Parse_StatusIsCaseInsensitiveAndTrimmed()
        {
            var warnings = new List<string>();

            CompatibilityList list = Parse(
                "{ \"version\": 1, \"games\": [" +
                " { \"appId\": 1, \"name\": \"A\", \"status\": \" Works \" }," +
                " { \"appId\": 2, \"name\": \"B\", \"status\": \"BROKEN\" }," +
                " { \"appId\": 3, \"name\": \"C\", \"status\": \"AntiCheat\" } ] }",
                warnings);

            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual(CompatibilityStatus.Works, list.Find(1).Status);
            Assert.AreEqual(CompatibilityStatus.Broken, list.Find(2).Status);
            Assert.AreEqual(CompatibilityStatus.AntiCheat, list.Find(3).Status);
        }

        [TestMethod]
        public void Parse_BadEntries_AreSkippedWithWarnings()
        {
            var warnings = new List<string>();

            CompatibilityList list = Parse(
                "{ \"version\": 1, \"games\": [ null," +
                " { \"appId\": 0, \"name\": \"No id\", \"status\": \"works\" }," +
                " { \"appId\": 5, \"name\": \"Odd\", \"status\": \"playable\" }," +
                " { \"appId\": 6, \"name\": \"No status\" }," +
                " { \"appId\": 7, \"name\": \"Good\", \"status\": \"works\" } ] }",
                warnings);

            Assert.AreEqual(1, list.Count);
            Assert.IsNotNull(list.Find(7));
            Assert.AreEqual(4, warnings.Count);
            StringAssert.Contains(warnings[0], "entry 1");
            StringAssert.Contains(warnings[1], "No id");
            StringAssert.Contains(warnings[2], "playable");
            StringAssert.Contains(warnings[3], "No status");
        }

        [TestMethod]
        public void Parse_DuplicateAppId_AntiCheatWins()
        {
            var warnings = new List<string>();

            CompatibilityList list = Parse(
                "{ \"version\": 1, \"games\": [" +
                " { \"appId\": 10, \"name\": \"A\", \"status\": \"works\" }," +
                " { \"appId\": 10, \"name\": \"A\", \"status\": \"anticheat\" }," +
                " { \"appId\": 11, \"name\": \"B\", \"status\": \"anticheat\" }," +
                " { \"appId\": 11, \"name\": \"B\", \"status\": \"works\" } ] }",
                warnings);

            Assert.AreEqual(CompatibilityStatus.AntiCheat, list.Find(10).Status);
            Assert.AreEqual(CompatibilityStatus.AntiCheat, list.Find(11).Status);
            Assert.AreEqual(2, warnings.Count);
            StringAssert.Contains(warnings[0], "more than once");
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("not json")]
        [DataRow("{ \"version\": 1, \"games\": [ { \"appId\": 1")]
        [DataRow("{ \"version\": 1, \"games\": [ { \"appId\": \"abc\", \"status\": \"works\" } ] }")]
        public void Parse_InvalidJson_ReturnsEmptyWithWarning(string json)
        {
            var warnings = new List<string>();

            CompatibilityList list = Parse(json, warnings);

            Assert.AreEqual(0, list.Count);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings[0], "not valid JSON");
            StringAssert.Contains(warnings[0], "anti-cheat files are still detected");
        }

        [TestMethod]
        [DataRow("null")]
        [DataRow("[1, 2]")]
        [DataRow("{ \"games\": [] }")]
        [DataRow("{ \"version\": 2, \"games\": [] }")]
        public void Parse_WrongVersionOrShape_ReturnsEmptyWithWarning(string json)
        {
            var warnings = new List<string>();

            CompatibilityList list = Parse(json, warnings);

            Assert.AreEqual(0, list.Count);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings[0], "\"version\": 1");
        }

        [TestMethod]
        [DataRow("{ \"version\": 1 }")]
        [DataRow("{ \"version\": 1, \"games\": null }")]
        public void Parse_NoGames_IsEmptyWithoutWarning(string json)
        {
            var warnings = new List<string>();

            CompatibilityList list = Parse(json, warnings);

            Assert.AreEqual(0, list.Count);
            Assert.AreEqual(0, warnings.Count);
        }

        [TestMethod]
        public void Load_MissingFile_ReturnsEmptyWithWarning()
        {
            var warnings = new List<string>();

            CompatibilityList list = CompatibilityList.Load(temp.PathOf("compatibility.json"), warnings);

            Assert.AreEqual(0, list.Count);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings[0], "not found");
        }

        [TestMethod]
        public void Load_LockedFile_ReturnsEmptyWithWarning()
        {
            string path = temp.WriteText("compatibility.json", "{ \"version\": 1, \"games\": [] }");
            var warnings = new List<string>();

            CompatibilityList list;
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                list = CompatibilityList.Load(path, warnings);
            }

            Assert.AreEqual(0, list.Count);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings[0], "Could not read");
        }

        [TestMethod]
        [DataRow(65001)]
        [DataRow(1200)]
        public void Load_FileWithByteOrderMark_StillLoads(int codePage)
        {
            // 65001 = UTF-8 with BOM (Notepad's "UTF-8 with BOM"), 1200 = UTF-16 LE with BOM (Notepad's "Unicode").
            string path = temp.WriteText(
                "compatibility.json",
                "{ \"version\": 1, \"games\": [ { \"appId\": 438100, \"name\": \"VRChat\", \"status\": \"anticheat\" } ] }",
                Encoding.GetEncoding(codePage));
            byte first = File.ReadAllBytes(path)[0];
            Assert.IsTrue(first == 0xEF || first == 0xFF, "the fixture must start with a byte order mark");
            var warnings = new List<string>();

            CompatibilityList list = CompatibilityList.Load(path, warnings);

            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual(CompatibilityStatus.AntiCheat, list.Find(438100).Status);
        }

        [TestMethod]
        public void ShippedFile_LoadsCleanlyAndBlocksVrChat()
        {
            string path = Path.Combine(Path.GetDirectoryName(typeof(CompatibilityListTests).Assembly.Location), CompatibilityList.FileName);
            var warnings = new List<string>();

            CompatibilityList list = CompatibilityList.Load(path, warnings);

            CollectionAssert.AreEqual(new string[0], warnings);
            Assert.AreEqual(29, list.Count);
            Assert.AreEqual(CompatibilityStatus.AntiCheat, list.Find(438100).Status);
            Assert.AreEqual(CompatibilityStatus.Works, list.Find(620980).Status);
            Assert.AreEqual(CompatibilityStatus.Broken, list.Find(546560).Status);
            // R7: every entry cites its source.
            Assert.AreEqual(29, Regex.Matches(File.ReadAllText(path), "OpenComposite compatibility sheet").Count);
        }
    }
}
