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
using System.Linq;
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    [TestClass]
    public class PatchStatusTests
    {
        private PatchFixture fx;
        private byte[] original;
        private string dll;

        [TestInitialize]
        public void SetUp()
        {
            fx = new PatchFixture();
            original = PatchFixture.OriginalDll(PeFixture.MachineX64, 1);
            dll = fx.WriteGameFile("openvr_api.dll", original);
            fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
        }

        [TestCleanup]
        public void TearDown()
        {
            fx.Dispose();
        }

        private void Patch()
        {
            ApplyResult result = fx.Service.Apply(fx.Planner.PlanPatch(fx.Scan(), PatchOptions.None, new List<string>()), false, false);
            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
        }

        private GamePatchStatus StatusOf(List<string> warnings)
        {
            return fx.Planner.GetStatuses(new[] { fx.Scan() }, warnings)[0];
        }

        [TestMethod]
        public void GetStatuses_NoRecord_IsNotPatched()
        {
            var warnings = new List<string>();

            GamePatchStatus status = StatusOf(warnings);

            Assert.AreEqual(PatchStatus.NotPatched, status.Status);
            Assert.AreEqual(0, status.Records.Count);
            Assert.AreEqual(0, warnings.Count, string.Join("\n", warnings));
        }

        [TestMethod]
        public void GetStatuses_AfterPatch_IsPatched()
        {
            Patch();
            var warnings = new List<string>();

            GamePatchStatus status = StatusOf(warnings);

            Assert.AreEqual(PatchStatus.Patched, status.Status);
            Assert.AreEqual(1, status.Records.Count);
            Assert.AreEqual(0, warnings.Count, string.Join("\n", warnings));
        }

        [TestMethod]
        public void GetStatuses_SteamPutTheOriginalBack_IsUnpatchedByUpdate()
        {
            Patch();
            File.WriteAllBytes(dll, original);

            Assert.AreEqual(PatchStatus.UnpatchedByUpdate, StatusOf(new List<string>()).Status);
        }

        [TestMethod]
        public void GetStatuses_NewCachedBuildAccepted_IsUpdateAvailable()
        {
            Patch();
            fx.CacheOpenComposite(OpenCompositeArch.X64, 2);

            Assert.AreEqual(PatchStatus.UpdateAvailable, StatusOf(new List<string>()).Status);
        }

        [TestMethod]
        public void GetStatuses_CacheJsonLagsBehindTheCachedFile_UsesTheFileHash()
        {
            // R34: cache.json still names build 1, but the cached file is build 2.
            Patch();
            File.WriteAllBytes(fx.Cache.DllPath(OpenCompositeArch.X64), OpenCompositeFixture.Dll(PeFixture.MachineX64, 2));

            Assert.AreEqual(PatchStatus.UpdateAvailable, StatusOf(new List<string>()).Status);
        }

        [TestMethod]
        public void GetStatuses_DllChangedByAnotherProgram_IsChangedExternallyWithAWarning()
        {
            Patch();
            File.WriteAllBytes(dll, PatchFixture.OriginalDll(PeFixture.MachineX64, 3));
            var warnings = new List<string>();

            GamePatchStatus status = StatusOf(warnings);

            Assert.AreEqual(PatchStatus.ChangedExternally, status.Status);
            Assert.AreEqual(1, warnings.Count, string.Join("\n", warnings));
            StringAssert.Contains(warnings[0], "neither the original");
        }

        [TestMethod]
        public void GetStatuses_DllMissing_IsChangedExternallyAndTheRecordIsKept()
        {
            Patch();
            File.Delete(dll);
            var warnings = new List<string>();

            GamePatchStatus status = StatusOf(warnings);

            Assert.AreEqual(PatchStatus.ChangedExternally, status.Status);
            Assert.IsTrue(warnings.Any(w => w.Contains("is missing")), string.Join("\n", warnings));
            Assert.IsNotNull(fx.FindRecord(dll));
        }

        [TestMethod]
        public void GetStatuses_RecordForAGameNotInTheList_WarnsAndKeepsIt()
        {
            Patch();
            var warnings = new List<string>();

            IReadOnlyList<GamePatchStatus> statuses = fx.Planner.GetStatuses(new GameEntry[0], warnings);

            Assert.AreEqual(0, statuses.Count);
            Assert.AreEqual(1, warnings.Count, string.Join("\n", warnings));
            StringAssert.Contains(warnings[0], "not in the Steam list");
            Assert.IsNotNull(fx.FindRecord(dll));
        }
    }
}
