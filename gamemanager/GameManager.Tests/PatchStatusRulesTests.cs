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
using System.Linq;
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    [TestClass]
    public class PatchStatusRulesTests
    {
        private static readonly string Original = new string('a', 64);
        private static readonly string Installed = new string('b', 64);
        private static readonly string Newer = new string('c', 64);
        private static readonly string Other = new string('d', 64);

        private static PatchRecord Record()
        {
            return new PatchRecord(1, "A", @"C:\Games\A", @"C:\Games\A\openvr_api.dll", OpenCompositeArch.X64, Original, Installed, false, DateTime.UtcNow);
        }

        /// <summary>
        /// A game with one record per status, for tests of the batch selections and texts.
        /// </summary>
        internal static GamePatchStatus Status(params PatchStatus[] recordStatuses)
        {
            var game = new SteamGame(1, "A", @"C:\Games\A", @"C:\Games");
            var classification = new GameClassification(GameKind.OpenVr, new OpenVrDll[0], false, new string[0], new string[0], new string[0]);
            var entry = new GameEntry(game, classification, CompatibilityVerdict.For(null, new string[0], new string[0]));
            var records = recordStatuses
                .Select((status, i) => new PatchRecord(1, "A", @"C:\Games\A", @"C:\Games\A\" + i + @"\openvr_api.dll", OpenCompositeArch.X64, Original, Installed, false, DateTime.UtcNow))
                .ToList();
            return new GamePatchStatus(entry, PatchStatusRules.Combine(recordStatuses), records, recordStatuses, new string[0]);
        }

        private static string Hash(string token)
        {
            switch (token)
            {
                case "original":
                    return Original;
                case "installed":
                    return Installed;
                case "newer":
                    return Newer;
                case "other":
                    return Other;
                default:
                    return null;
            }
        }

        [TestMethod]
        [DataRow(PatchStatus.UnpatchedByUpdate, "installed", "original")]
        [DataRow(PatchStatus.Patched, "installed", "installed")]
        [DataRow(PatchStatus.UpdateAvailable, "newer", "installed")]
        [DataRow(PatchStatus.Patched, "none", "installed")]
        [DataRow(PatchStatus.ChangedExternally, "installed", "other")]
        [DataRow(PatchStatus.ChangedExternally, "installed", "missing")]
        public void Evaluate_ComparesTheDllWithTheRecordAndTheCachedBuild(PatchStatus expected, string cached, string current)
        {
            Assert.AreEqual(expected, PatchStatusRules.Evaluate(Record(), Hash(current), Hash(cached)));
        }

        [TestMethod]
        public void Combine_TheStatusThatMostNeedsAttentionWins()
        {
            Assert.AreEqual(PatchStatus.ChangedExternally, PatchStatusRules.Combine(new[] { PatchStatus.Patched, PatchStatus.ChangedExternally, PatchStatus.UnpatchedByUpdate }));
            Assert.AreEqual(PatchStatus.UnpatchedByUpdate, PatchStatusRules.Combine(new[] { PatchStatus.UpdateAvailable, PatchStatus.UnpatchedByUpdate }));
            Assert.AreEqual(PatchStatus.UpdateAvailable, PatchStatusRules.Combine(new[] { PatchStatus.Patched, PatchStatus.UpdateAvailable }));
        }

        [TestMethod]
        public void Combine_NoRecords_IsNotPatched()
        {
            Assert.AreEqual(PatchStatus.NotPatched, PatchStatusRules.Combine(new PatchStatus[0]));
        }

        [TestMethod]
        public void PatchBatch_SelectsGamesByTheStatusOfAnyOfTheirDlls()
        {
            // A Unity game with one DLL put back by Steam and one on an older build is in both lists.
            GamePatchStatus unity = Status(PatchStatus.UnpatchedByUpdate, PatchStatus.UpdateAvailable);
            GamePatchStatus patched = Status(PatchStatus.Patched);
            GamePatchStatus updatable = Status(PatchStatus.UpdateAvailable);
            var all = new[] { unity, patched, updatable };

            CollectionAssert.AreEqual(new[] { unity }, PatchBatch.Unpatched(all).ToArray());
            CollectionAssert.AreEqual(new[] { unity, updatable }, PatchBatch.Updatable(all).ToArray());
        }

        [TestMethod]
        public void PatchBatch_NothingToOffer_IsEmpty()
        {
            var all = new[] { Status(PatchStatus.Patched), Status() };

            Assert.AreEqual(0, PatchBatch.Unpatched(all).Count);
            Assert.AreEqual(0, PatchBatch.Updatable(all).Count);
        }
    }
}
