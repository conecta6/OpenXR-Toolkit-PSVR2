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
    public class PathUtilTests
    {
        [TestMethod]
        public void NormalizeDirectory_ForwardSlashesAndTrailingSlash_AreNormalized()
        {
            Assert.AreEqual(@"e:\SteamLibrary", PathUtil.NormalizeDirectory("e:/SteamLibrary/"));
        }

        [TestMethod]
        public void NormalizeDirectory_DriveRoot_KeepsItsBackslash()
        {
            Assert.AreEqual(@"E:\", PathUtil.NormalizeDirectory("E:/"));
            Assert.AreEqual(@"E:\", PathUtil.NormalizeDirectory(@"E:\"));
        }

        [TestMethod]
        public void NormalizeDirectory_DotSegments_AreResolved()
        {
            Assert.AreEqual(@"C:\Games\b", PathUtil.NormalizeDirectory(@"C:\Games\a\..\b\\"));
        }

        [TestMethod]
        public void NormalizeDirectory_Empty_Throws()
        {
            Assert.ThrowsException<ArgumentException>(() => PathUtil.NormalizeDirectory("  "));
        }

        [TestMethod]
        [DataRow("Games")]
        [DataRow(@"..\Games")]
        [DataRow("D:Games")]
        [DataRow("D:")]
        [DataRow(@"\Games")]
        public void NormalizeDirectory_NotFullyQualified_Throws(string path)
        {
            // R3: these would otherwise be resolved against the process's current folder or drive.
            Assert.ThrowsException<ArgumentException>(() => PathUtil.NormalizeDirectory(path));
        }

        [TestMethod]
        [DataRow(@"\\?\C:\Games\x", @"C:\Games\x")]
        [DataRow(@"\\?\UNC\server\share\x", @"\\server\share\x")]
        [DataRow(@"\\?\unc\server\share", @"\\server\share")]
        [DataRow(@"C:\Games\x", @"C:\Games\x")]
        [DataRow(@"\\server\share", @"\\server\share")]
        [DataRow("", "")]
        public void StripDevicePrefix_DropsOnlyTheDevicePrefix(string path, string expected)
        {
            Assert.AreEqual(expected, PathUtil.StripDevicePrefix(path));
        }

        [TestMethod]
        public void StripDevicePrefix_Null_ReturnsNull()
        {
            Assert.IsNull(PathUtil.StripDevicePrefix(null));
        }

        [TestMethod]
        public void NormalizeDirectory_UncPath_IsKept()
        {
            Assert.AreEqual(@"\\server\share\SteamLibrary", PathUtil.NormalizeDirectory(@"\\server\share\SteamLibrary\"));
        }

        [TestMethod]
        [DataRow(@"C:\Games\A\bin\game.exe", @"C:\Games\A", true)]
        [DataRow(@"c:\games\a\game.exe", @"C:\Games\A\", true)]
        [DataRow(@"C:\Games\A2\game.exe", @"C:\Games\A", false)]
        [DataRow(@"C:\Games\A", @"C:\Games\A", false)]
        [DataRow("game.exe", @"C:\Games\A", false)]
        [DataRow(@"E:\game.exe", @"E:\", true)]
        public void IsUnder_MatchesOnlyPathsInsideTheFolder(string path, string folder, bool expected)
        {
            Assert.AreEqual(expected, PathUtil.IsUnder(path, folder));
        }

        [TestMethod]
        [DataRow(@"C:\Games\A\bin\game.exe", @"C:\Games\A", @"bin\game.exe")]
        [DataRow(@"c:\games\a\OPENVR_API.DLL", @"C:\Games\A\", "OPENVR_API.DLL")]
        [DataRow(@"E:\game.exe", @"E:\", "game.exe")]
        [DataRow(@"C:\Games\A2\game.exe", @"C:\Games\A", @"C:\Games\A2\game.exe")]
        [DataRow(@"C:\Games\A", @"C:\Games\A", @"C:\Games\A")]
        [DataRow(@"D:\Other\x.dll", @"C:\Games\A", @"D:\Other\x.dll")]
        public void Relative_ShortensOnlyRealSubPaths(string path, string root, string expected)
        {
            Assert.AreEqual(expected, PathUtil.Relative(root, path));
        }
    }
}
