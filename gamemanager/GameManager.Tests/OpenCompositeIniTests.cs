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
using System.Globalization;
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    [TestClass]
    public class OpenCompositeIniTests
    {
        [TestMethod]
        public void Render_UsesADotWhateverTheWindowsLanguage()
        {
            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                // Spanish Windows writes 1,25; OpenComposite would not read it.
                CultureInfo.CurrentCulture = new CultureInfo("es-ES");

                Assert.AreEqual("supersampleRatio=1.25\r\n", OpenCompositeIni.Render(1.25));
                Assert.AreEqual("supersampleRatio=1.0\r\n", OpenCompositeIni.Render(1.0));
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        [TestMethod]
        [DataRow(0.49)]
        [DataRow(2.01)]
        [DataRow(double.NaN)]
        public void Render_OutOfRange_Throws(double ratio)
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => OpenCompositeIni.Render(ratio));
        }

        [TestMethod]
        public void AllowedKeys_IsOnlySupersampleRatio()
        {
            CollectionAssert.AreEqual(new[] { "supersampleRatio" }, new List<string>(OpenCompositeIni.AllowedKeys));
        }
    }
}
