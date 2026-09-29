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
using System.Runtime.InteropServices;
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    [TestClass]
    public class StartupArgumentsTests
    {
        [TestMethod]
        public void ParseAppData_NoArguments_ReturnsNullWithoutWarning()
        {
            string warning;

            Assert.IsNull(StartupArguments.ParseAppData(new string[0], out warning));
            Assert.IsNull(warning);
            Assert.IsNull(StartupArguments.ParseAppData(null, out warning));
            Assert.IsNull(warning);
        }

        [TestMethod]
        public void ParseAppData_SwitchAndFullPath_ReturnsTheNormalizedPath()
        {
            string warning;

            string path = StartupArguments.ParseAppData(new[] { "--app-data", @"C:\Users\Ana\AppData\Local\OpenXR-Toolkit-PSVR2\GameManager\" }, out warning);

            Assert.AreEqual(@"C:\Users\Ana\AppData\Local\OpenXR-Toolkit-PSVR2\GameManager", path);
            Assert.IsNull(warning);
        }

        [DataTestMethod]
        [DataRow(@"GameManager")]
        [DataRow(@"D:GameManager")]
        [DataRow(@"\GameManager")]
        [DataRow(@"")]
        public void ParseAppData_PathNotFullyQualified_IsIgnoredWithWarning(string value)
        {
            string warning;

            Assert.IsNull(StartupArguments.ParseAppData(new[] { "--app-data", value }, out warning));
            StringAssert.Contains(warning, "Ignored --app-data");
        }

        [TestMethod]
        public void ParseAppData_OnlyTheSwitch_IsIgnoredWithWarning()
        {
            string warning;

            Assert.IsNull(StartupArguments.ParseAppData(new[] { "--app-data" }, out warning));
            StringAssert.Contains(warning, "only --app-data <folder> is accepted");
        }

        [TestMethod]
        public void ParseAppData_UnknownOrExtraArguments_AreIgnoredWithWarning()
        {
            string warning;

            Assert.IsNull(StartupArguments.ParseAppData(new[] { "--other", @"C:\x" }, out warning));
            StringAssert.Contains(warning, "--other");
            Assert.IsNull(StartupArguments.ParseAppData(new[] { "--app-data", @"C:\x", "--extra" }, out warning));
            StringAssert.Contains(warning, "--extra");
            Assert.IsNull(StartupArguments.ParseAppData(new[] { @"--app-data=C:\x" }, out warning));
            Assert.IsNotNull(warning);
        }

        [TestMethod]
        public void ParseAppData_InvalidCharacters_IsIgnoredWithWarning()
        {
            string warning;

            Assert.IsNull(StartupArguments.ParseAppData(new[] { "--app-data", @"C:\bad|name" }, out warning));
            StringAssert.Contains(warning, "Ignored --app-data");
        }

        [DataTestMethod]
        [DataRow(@"C:\Users\Ana\AppData\Local\OpenXR-Toolkit-PSVR2\GameManager")]
        [DataRow(@"C:\Users\Ana Maria\AppData\Local\GameManager")]
        [DataRow(@"E:\")]
        [DataRow(@"\\server\share\Game Manager")]
        [DataRow(@"C:\dir with space\")]
        public void AppDataArgument_SplitsBackIntoTheSameTwoArgumentsWindowsSeesAndParses(string root)
        {
            string commandLine = "GameManager.exe " + StartupArguments.AppDataArgument(root);

            string[] args = SplitLikeWindows(commandLine);

            Assert.AreEqual(3, args.Length, commandLine);
            Assert.AreEqual("--app-data", args[1]);
            Assert.AreEqual(root, args[2]);
            string warning;
            Assert.AreEqual(PathUtil.NormalizeDirectory(root), StartupArguments.ParseAppData(new[] { args[1], args[2] }, out warning));
            Assert.IsNull(warning);
        }

        /// <summary>
        /// The real Windows command-line splitter, so the quoting is checked against what the relaunched process sees.
        /// </summary>
        private static string[] SplitLikeWindows(string commandLine)
        {
            int count;
            IntPtr argv = CommandLineToArgvW(commandLine, out count);
            Assert.AreNotEqual(IntPtr.Zero, argv);
            try
            {
                var result = new string[count];
                for (int i = 0; i < count; i++)
                {
                    result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size));
                }
                return result;
            }
            finally
            {
                LocalFree(argv);
            }
        }

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string commandLine, out int argumentCount);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
