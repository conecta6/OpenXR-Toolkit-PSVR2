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
using System.IO;
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    [TestClass]
    public class AppDataPathsTests
    {
        [TestMethod]
        public void ForCurrentUser_IsUnderLocalAppData()
        {
            string expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenXR-Toolkit-PSVR2", "GameManager");

            AppDataPaths paths = AppDataPaths.ForCurrentUser();

            Assert.AreEqual(expected, paths.Root, true);
        }

        [TestMethod]
        public void Constructor_NormalizesInjectedRootAndDerivesPaths()
        {
            using (var temp = new TempDir())
            {
                string root = temp.PathOf("AppData");

                var paths = new AppDataPaths(root + "\\");

                Assert.AreEqual(root, paths.Root, true);
                Assert.AreEqual(Path.Combine(root, "settings.json"), paths.SettingsFile, true);
                Assert.AreEqual(Path.Combine(root, "opencomposite"), paths.OpenCompositeFolder, true);
                Assert.IsFalse(Directory.Exists(root), "building the paths must not create anything");
            }
        }
    }
}
