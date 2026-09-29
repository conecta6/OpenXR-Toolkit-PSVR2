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

using System.Text;
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    [TestClass]
    public class VdfReaderTests
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

        [TestMethod]
        public void Parse_NestedBlocks_BuildsTree()
        {
            VdfNode root = VdfReader.Parse("\"AppState\"\n{\n\t\"appid\"\t\t\"620980\"\n\t\"UserConfig\"\n\t{\n\t\t\"language\"\t\t\"english\"\n\t}\n}\n");

            VdfNode state = root.Find("AppState");
            Assert.IsNotNull(state);
            Assert.IsTrue(state.IsBlock);
            Assert.AreEqual("620980", state.GetString("appid"));
            Assert.AreEqual("english", state.Find("UserConfig").GetString("language"));
        }

        [TestMethod]
        public void Find_IsCaseInsensitive()
        {
            VdfNode root = VdfReader.Parse(@"""LibraryFolders"" { ""Path"" ""x"" }");

            Assert.AreEqual("x", root.Find("libraryfolders").GetString("PATH"));
        }

        [TestMethod]
        public void Parse_EscapedBackslashesAndQuotes_AreUnescaped()
        {
            VdfNode root = VdfReader.Parse(@"""path"" ""C:\\Program Files (x86)\\Steam"" ""quote"" ""say \""hi\""""");

            Assert.AreEqual(@"C:\Program Files (x86)\Steam", root.GetString("path"));
            Assert.AreEqual("say \"hi\"", root.GetString("quote"));
        }

        [TestMethod]
        public void Parse_NewlineAndTabEscapes_AreUnescaped()
        {
            VdfNode root = VdfReader.Parse(@"""text"" ""a\nb\tc""");

            Assert.AreEqual("a\nb\tc", root.GetString("text"));
        }

        [TestMethod]
        public void Parse_UnknownEscape_IsKeptLiteral()
        {
            VdfNode root = VdfReader.Parse(@"""path"" ""D:\Games""");

            Assert.AreEqual(@"D:\Games", root.GetString("path"));
        }

        [TestMethod]
        public void Parse_LineComments_AreIgnored()
        {
            VdfNode root = VdfReader.Parse("// header comment\n\"a\" \"1\" // trailing comment\n// \"b\" \"2\"\n");

            Assert.AreEqual(1, root.Children.Count);
            Assert.AreEqual("1", root.GetString("a"));
        }

        [TestMethod]
        public void Parse_UnquotedTokens_AreSupported()
        {
            VdfNode root = VdfReader.Parse("AppState\n{\n\tappid 620980\n\tname \"Beat Saber\"\n}");

            Assert.AreEqual("620980", root.Find("AppState").GetString("appid"));
            Assert.AreEqual("Beat Saber", root.Find("AppState").GetString("name"));
        }

        [TestMethod]
        public void Parse_EmptyValueAndEmptyBlock_AreKept()
        {
            VdfNode root = VdfReader.Parse(@"""label"" """" ""apps"" { }");

            Assert.AreEqual("", root.GetString("label"));
            Assert.IsTrue(root.Find("apps").IsBlock);
            Assert.AreEqual(0, root.Find("apps").Children.Count);
        }

        [TestMethod]
        public void GetString_OnBlockOrMissingKey_ReturnsNull()
        {
            VdfNode root = VdfReader.Parse(@"""apps"" { ""1"" ""2"" }");

            Assert.IsNull(root.GetString("apps"));
            Assert.IsNull(root.GetString("missing"));
            Assert.IsNull(root.Find("apps").Find("1").Find("anything"));
        }

        [TestMethod]
        public void Parse_EmptyText_ReturnsEmptyRoot()
        {
            VdfNode root = VdfReader.Parse("");

            Assert.IsTrue(root.IsBlock);
            Assert.AreEqual(0, root.Children.Count);
        }

        [TestMethod]
        public void Parse_UnterminatedString_ThrowsWithLine()
        {
            var e = Assert.ThrowsException<VdfFormatException>(() => VdfReader.Parse("\"a\" \"1\"\n\"b\" \"2"));

            Assert.AreEqual(2, e.Line);
        }

        [TestMethod]
        public void Parse_MissingClosingBrace_Throws()
        {
            Assert.ThrowsException<VdfFormatException>(() => VdfReader.Parse(@"""a"" { ""b"" ""1"""));
        }

        [TestMethod]
        public void Parse_UnexpectedClosingBrace_Throws()
        {
            Assert.ThrowsException<VdfFormatException>(() => VdfReader.Parse(@"""a"" ""1"" }"));
        }

        [TestMethod]
        public void Parse_KeyWithoutValue_Throws()
        {
            Assert.ThrowsException<VdfFormatException>(() => VdfReader.Parse(@"""a"" { ""b"" }"));
        }

        [TestMethod]
        public void Parse_BlockWhereKeyExpected_Throws()
        {
            Assert.ThrowsException<VdfFormatException>(() => VdfReader.Parse(@"{ ""a"" ""1"" }"));
        }

        [TestMethod]
        public void Parse_TooDeeplyNested_ThrowsInsteadOfCrashing()
        {
            var text = new StringBuilder();
            for (int i = 0; i < 10000; i++)
            {
                text.Append("\"k\" { ");
            }

            Assert.ThrowsException<VdfFormatException>(() => VdfReader.Parse(text.ToString()));
        }

        [TestMethod]
        public void ParseFile_Utf8WithBom_ReadsNonAsciiText()
        {
            string name = "\u6771\u65b9 Pok\u00e9mon";
            string path = temp.WriteText("app.acf", "\"AppState\" { \"name\" \"" + name + "\" }", new UTF8Encoding(true));

            Assert.AreEqual(name, VdfReader.ParseFile(path).Find("AppState").GetString("name"));
        }
    }
}
