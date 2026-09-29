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
using System.Security.AccessControl;
using System.Security.Principal;
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    [TestClass]
    public class PatchUpdateTests
    {
        private PatchFixture fx;
        private byte[] original;
        private string dll;
        private string oc1;

        [TestInitialize]
        public void SetUp()
        {
            fx = new PatchFixture();
            original = PatchFixture.OriginalDll(PeFixture.MachineX64, 1);
            dll = fx.WriteGameFile("openvr_api.dll", original);
            oc1 = fx.CacheOpenComposite(OpenCompositeArch.X64, 1);
        }

        [TestCleanup]
        public void TearDown()
        {
            fx.Dispose();
        }

        private void Patch(GameEntry entry, PatchOptions options)
        {
            ApplyResult result = fx.Service.Apply(fx.Planner.PlanPatch(entry, options, new List<string>()), false, false);
            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
        }

        private PatchPlan PlanUpdate(GameEntry entry)
        {
            return fx.Planner.PlanUpdate(entry, new List<string>());
        }

        [TestMethod]
        public void PlanUpdate_NewBuildAccepted_ReplacesOnlyTheDllAndKeepsTheBackup()
        {
            Patch(fx.Scan(), PatchOptions.None);
            string bak = dll + ".bak";
            var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(bak, old);
            string oc2 = fx.CacheOpenComposite(OpenCompositeArch.X64, 2);

            PatchPlan plan = PlanUpdate(fx.Scan());
            ApplyResult result = fx.Service.Apply(plan, false, false);

            CollectionAssert.AreEqual(
                new[] { PatchActionKind.VerifyHash, PatchActionKind.CopyFile, PatchActionKind.VerifyHash },
                plan.AllActions.Select(a => a.Kind).ToArray());
            Assert.IsFalse(plan.AllActions.Any(a => string.Equals(a.Target, bak, StringComparison.OrdinalIgnoreCase)), "an update never touches the .bak");
            Assert.AreEqual(ApplyOutcome.Succeeded, result.Outcome, result.Message);
            Assert.AreEqual(oc2, FileHash.Sha256(dll));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(bak));
            Assert.AreEqual(old, File.GetLastWriteTimeUtc(bak));
            PatchRecord record = fx.FindRecord(dll);
            Assert.AreEqual(oc2, record.OpenCompositeSha256);
            Assert.AreEqual(FileHash.Sha256(bak), record.OriginalSha256);
        }

        [TestMethod]
        public void PlanUpdate_SteamPutTheOriginalBack_IsNotUpdated()
        {
            Patch(fx.Scan(), PatchOptions.None);
            fx.CacheOpenComposite(OpenCompositeArch.X64, 2);
            File.WriteAllBytes(dll, original);

            PatchPlan plan = PlanUpdate(fx.Scan());

            Assert.IsTrue(plan.CanRun);
            Assert.IsFalse(plan.HasWork);
            StringAssert.Contains(plan.Notes[0], "Unpatched by update");
        }

        [TestMethod]
        public void PlanUpdate_ChangedExternally_IsNotTouched()
        {
            Patch(fx.Scan(), PatchOptions.None);
            fx.CacheOpenComposite(OpenCompositeArch.X64, 2);
            File.WriteAllBytes(dll, PatchFixture.OriginalDll(PeFixture.MachineX64, 3));

            PatchPlan plan = PlanUpdate(fx.Scan());

            Assert.IsFalse(plan.HasWork);
            StringAssert.Contains(plan.Notes[0], "Changed externally");
        }

        [TestMethod]
        public void PlanUpdate_AlreadyOnTheCurrentBuild_HasNothingToDo()
        {
            Patch(fx.Scan(), PatchOptions.None);

            PatchPlan plan = PlanUpdate(fx.Scan());

            Assert.IsTrue(plan.CanRun);
            Assert.IsFalse(plan.HasWork);
            StringAssert.Contains(plan.Notes[0], "already has the current OpenComposite build");
        }

        [TestMethod]
        public void PlanUpdate_BlockedGame_IsBlocked()
        {
            Patch(fx.Scan(), PatchOptions.None);
            fx.CacheOpenComposite(OpenCompositeArch.X64, 2);

            PatchPlan plan = PlanUpdate(fx.ScanWith(CompatibilityVerdict.For(null, new[] { "EasyAntiCheat" }, new string[0])));

            Assert.IsFalse(plan.CanRun);
            StringAssert.Contains(plan.Blockers[0], "use Restore");
        }

        [TestMethod]
        public void PlanUpdate_GameWithoutRecord_IsBlocked()
        {
            fx.CacheOpenComposite(OpenCompositeArch.X64, 2);

            PatchPlan plan = PlanUpdate(fx.Scan());

            Assert.IsFalse(plan.CanRun);
            StringAssert.Contains(plan.Blockers[0], "no record");
        }

        [TestMethod]
        public void UpdateAll_UsesTheHashOfTheCachedFileNotCacheJson()
        {
            // R34: cache.json is edited to name a wrong hash for the new build; the plan and the record must use the
            // hash of the cached file itself. Were the plan to copy cache.json's value, the copy check would fail.
            Patch(fx.Scan(), PatchOptions.None);
            string oc2 = fx.CacheOpenComposite(OpenCompositeArch.X64, 2);
            string wrong = new string('0', 64);
            string json = File.ReadAllText(fx.Cache.CacheFilePath);
            Assert.IsTrue(json.Contains(oc2), "the fixture must have written the hash into cache.json");
            File.WriteAllText(fx.Cache.CacheFilePath, json.Replace(oc2, wrong));

            BatchResult result = fx.Service.ApplyAll(new[] { PlanUpdate(fx.Scan()) }, false);

            Assert.AreEqual(1, result.Results.Count, result.Message);
            Assert.AreEqual(ApplyOutcome.Succeeded, result.Results[0].Outcome, result.Message);
            Assert.AreEqual(FileHash.Sha256(dll), fx.FindRecord(dll).OpenCompositeSha256);
            Assert.AreEqual(oc2, fx.FindRecord(dll).OpenCompositeSha256);
        }

        [TestMethod]
        public void ApplyAll_SkipsPlansThatNeedConfirmationOrAreBlocked_AndRunsTheOthers()
        {
            SteamGame linked = fx.AddGame(1001, "Linked Game");
            SteamGame eac = fx.AddGame(1002, "EAC Game");
            string linkedDll = PatchFixture.WriteFileIn(linked, "openvr_api.dll", original);
            string eacDll = PatchFixture.WriteFileIn(eac, "openvr_api.dll", original);
            var warnings = new List<string>();
            var plans = new[]
            {
                fx.Planner.PlanPatch(fx.Scan(), PatchOptions.None, warnings),
                fx.Planner.PlanPatch(fx.ScanOf(linked, CompatibilityVerdict.For(null, new string[0], new[] { Path.Combine(linked.InstallDir, "Link") })), PatchOptions.None, warnings),
                fx.Planner.PlanPatch(fx.ScanOf(eac, CompatibilityVerdict.For(null, new[] { "EasyAntiCheat" }, new string[0])), PatchOptions.None, warnings),
            };

            BatchResult result = fx.Service.ApplyAll(plans, false);

            Assert.AreEqual(1, result.Results.Count);
            Assert.AreEqual(2, result.Skipped.Count);
            StringAssert.Contains(result.Message, "needs your confirmation");
            Assert.AreEqual(oc1, FileHash.Sha256(dll));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(linkedDll));
            Assert.IsFalse(File.Exists(linkedDll + ".bak"));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(eacDll));
        }

        [TestMethod]
        public void ApplyAll_Simulation_ChangesNothing()
        {
            var plans = new[] { fx.Planner.PlanPatch(fx.Scan(), PatchOptions.None, new List<string>()) };
            string before = fx.Snapshot();

            BatchResult result = fx.Service.ApplyAll(plans, true);

            Assert.AreEqual(before, fx.Snapshot());
            Assert.AreEqual(ApplyOutcome.Simulated, result.Results[0].Outcome);
            StringAssert.StartsWith(result.Message, "Simulation");
            Assert.IsFalse(result.ChangedSomething);
        }

        [TestMethod]
        public void ApplyAll_OneGameRunning_TheOthersAreStillUpdated()
        {
            SteamGame other = fx.AddGame(1001, "Other Game");
            string otherDll = PatchFixture.WriteFileIn(other, "openvr_api.dll", original);
            Patch(fx.Scan(), PatchOptions.None);
            Patch(fx.ScanOf(other, null), PatchOptions.None);
            string oc2 = fx.CacheOpenComposite(OpenCompositeArch.X64, 2);
            fx.Processes.Paths.Add(Path.Combine(fx.InstallDir, "Beat Saber.exe"));

            BatchResult result = fx.Service.ApplyAll(new[] { PlanUpdate(fx.Scan()), PlanUpdate(fx.ScanOf(other, null)) }, false);

            Assert.AreEqual(ApplyOutcome.Refused, result.Results[0].Outcome);
            Assert.AreEqual(ApplyOutcome.Succeeded, result.Results[1].Outcome, result.Message);
            Assert.AreEqual(oc1, FileHash.Sha256(dll));
            Assert.AreEqual(oc2, FileHash.Sha256(otherDll));
        }

        [TestMethod]
        public void ApplyAll_AccessDenied_StopsTheBatch()
        {
            SteamGame other = fx.AddGame(1001, "Other Game");
            string otherDll = PatchFixture.WriteFileIn(other, "openvr_api.dll", original);
            Patch(fx.Scan(), PatchOptions.None);
            Patch(fx.ScanOf(other, null), PatchOptions.None);
            fx.CacheOpenComposite(OpenCompositeArch.X64, 2);
            var plans = new[] { PlanUpdate(fx.Scan()), PlanUpdate(fx.ScanOf(other, null)) };
            var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User, FileSystemRights.WriteData, AccessControlType.Deny);
            FileSecurity security = File.GetAccessControl(dll);
            security.AddAccessRule(rule);
            File.SetAccessControl(dll, security);
            try
            {
                if (PatchFixture.CanOpenForWrite(dll))
                {
                    Assert.Inconclusive("A deny ACL did not block writing for this account.");
                }

                BatchResult result = fx.Service.ApplyAll(plans, false);

                Assert.IsTrue(result.NeedsElevation);
                Assert.AreEqual(1, result.Results.Count);
                Assert.AreEqual(oc1, FileHash.Sha256(otherDll), "the batch stops at the first permission error");
            }
            finally
            {
                security.RemoveAccessRule(rule);
                File.SetAccessControl(dll, security);
            }
        }

        [TestMethod]
        public void RepatchAll_AfterSteamPutTheOriginalBack_PatchesAgainAndKeepsTheIniRecord()
        {
            Patch(fx.Scan(), new PatchOptions(true, 1.25));
            byte[] backupBefore = File.ReadAllBytes(dll + ".bak");
            File.WriteAllBytes(dll, original);
            var warnings = new List<string>();
            IReadOnlyList<GamePatchStatus> unpatched = PatchBatch.Unpatched(fx.Planner.GetStatuses(new[] { fx.Scan() }, warnings));

            BatchResult result = fx.Service.ApplyAll(
                unpatched.Select(s => fx.Planner.PlanPatch(s.Entry, PatchOptions.None, warnings)).ToList(),
                false);

            Assert.AreEqual(1, unpatched.Count);
            Assert.AreEqual(ApplyOutcome.Succeeded, result.Results[0].Outcome, result.Message);
            Assert.AreEqual(oc1, FileHash.Sha256(dll));
            CollectionAssert.AreEqual(backupBefore, File.ReadAllBytes(dll + ".bak"));
            Assert.IsTrue(fx.FindRecord(dll).IniCreated);
            Assert.AreEqual("supersampleRatio=1.25\r\n", File.ReadAllText(Path.Combine(fx.InstallDir, "opencomposite.ini")));
        }
    }
}
