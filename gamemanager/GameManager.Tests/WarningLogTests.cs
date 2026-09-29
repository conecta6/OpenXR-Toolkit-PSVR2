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
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    [TestClass]
    public class WarningLogTests
    {
        [TestMethod]
        public void Add_SameWarningTwice_IsKeptOnce()
        {
            var log = new WarningLog();

            Assert.IsTrue(log.Add("OpenComposite: offline"));
            Assert.IsFalse(log.Add("OpenComposite: offline"));

            Assert.AreEqual(1, log.Items.Count);
        }

        [TestMethod]
        public void Add_BlankWarning_IsIgnored()
        {
            var log = new WarningLog();

            Assert.IsFalse(log.Add(null));
            Assert.IsFalse(log.Add("   "));

            Assert.AreEqual(0, log.Items.Count);
        }

        [TestMethod]
        public void RemoveWhere_RemovesMatchingWarnings_AndTheyCanBeAddedAgain()
        {
            var log = new WarningLog();
            log.Add("a");
            log.Add("license: not accepted");
            log.Add("b");

            Assert.AreEqual(1, log.RemoveWhere(w => w.StartsWith("license", System.StringComparison.Ordinal)));
            Assert.AreEqual(0, log.RemoveWhere(w => w == "nothing"));

            CollectionAssert.AreEqual(new[] { "a", "b" }, new List<string>(log.Items));
            Assert.IsTrue(log.Add("license: not accepted"));
            CollectionAssert.AreEqual(new[] { "a", "b", "license: not accepted" }, new List<string>(log.Items));
        }

        [TestMethod]
        public void IsLicenseNotAcceptedMessage_MatchesTheCacheMessagesOnly()
        {
            using (var fx = new PatchFixture())
            {
                // Settings were never saved, so the license is not accepted.
                DownloadOutcome download = fx.Cache.DownloadAsync(OpenCompositeArch.X64, new List<string>(), System.Threading.CancellationToken.None).GetAwaiter().GetResult();

                Assert.AreEqual(DownloadStatus.LicenseNotAccepted, download.Status);
                Assert.IsTrue(OpenCompositeCache.IsLicenseNotAcceptedMessage(download.Message));
                Assert.IsFalse(OpenCompositeCache.IsLicenseNotAcceptedMessage("OpenComposite: offline"));
                Assert.IsFalse(OpenCompositeCache.IsLicenseNotAcceptedMessage(null));
            }
        }

        [TestMethod]
        public void Items_KeepTheOrderFirstSeen()
        {
            var log = new WarningLog();
            log.Add("b");
            log.Add("a");
            log.Add("b");

            CollectionAssert.AreEqual(new[] { "b", "a" }, new List<string>(log.Items));
        }
    }
}
