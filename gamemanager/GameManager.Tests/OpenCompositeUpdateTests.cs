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
using System.Threading;
using System.Threading.Tasks;
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    [TestClass]
    public class OpenCompositeUpdateTests
    {
        private static readonly DateTime Start = new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc);

        private TempDir temp;
        private SettingsStore settings;
        private FakeHttpDownloader http;
        private OpenCompositeCache cache;
        private DateTime now;

        [TestInitialize]
        public void SetUp()
        {
            temp = new TempDir();
            var paths = new AppDataPaths(temp.PathOf("AppData"));
            settings = new SettingsStore(paths.SettingsFile);
            http = new FakeHttpDownloader();
            now = Start;
            cache = new OpenCompositeCache(paths, settings, http, () => now);
        }

        [TestCleanup]
        public void TearDown()
        {
            temp.Dispose();
        }

        /// <summary>
        /// License accepted and build 1 of x64 cached, as after a first download.
        /// </summary>
        private async Task<string> CacheBuildOneAsync()
        {
            settings.Save(new AppSettings { OpenCompositeLicenseAccepted = true });
            http.Respond = url => OpenCompositeFixture.Dll(PeFixture.MachineX64, 1);
            DownloadOutcome outcome = await cache.DownloadAsync(OpenCompositeArch.X64, new List<string>(), CancellationToken.None);
            Assert.AreEqual(DownloadStatus.Downloaded, outcome.Status);
            http.RequestedUrls.Clear();
            return FileHash.Sha256(cache.DllPath(OpenCompositeArch.X64));
        }

        [TestMethod]
        public void IsUpdateCheckDue_NeverChecked_IsDue()
        {
            Assert.IsTrue(OpenCompositeCache.IsUpdateCheckDue(null, Start));
        }

        [TestMethod]
        [DataRow(1.0, false)]
        [DataRow(23.9, false)]
        [DataRow(24.0, true)]
        [DataRow(72.0, true)]
        public void IsUpdateCheckDue_ByAge(double hoursSinceLastCheck, bool expected)
        {
            Assert.AreEqual(expected, OpenCompositeCache.IsUpdateCheckDue(Start.AddHours(-hoursSinceLastCheck), Start));
        }

        [TestMethod]
        public void IsUpdateCheckDue_LastCheckInFuture_IsDue()
        {
            // The clock was moved back, or settings.json was edited: check now instead of waiting days.
            Assert.IsTrue(OpenCompositeCache.IsUpdateCheckDue(Start.AddHours(2), Start));
        }

        [TestMethod]
        public async Task CheckForUpdates_SameBuild_ReportsUnchangedAndRecordsTime()
        {
            string hash = await CacheBuildOneAsync();
            now = Start.AddDays(1);
            var warnings = new List<string>();

            IReadOnlyList<DownloadOutcome> outcomes = await cache.CheckForUpdatesAsync(warnings, CancellationToken.None);

            Assert.AreEqual(1, outcomes.Count);
            Assert.AreEqual(DownloadStatus.Unchanged, outcomes[0].Status);
            Assert.AreEqual(hash, FileHash.Sha256(cache.DllPath(OpenCompositeArch.X64)));
            Assert.IsFalse(cache.GetBuilds(warnings)[0].HasPendingUpdate);
            Assert.AreEqual((DateTime?)now, settings.Load(warnings).LastUpdateCheckUtc);
            Assert.AreEqual(0, warnings.Count);
        }

        [TestMethod]
        public async Task CheckForUpdates_NewBuild_KeepsCurrentAndStoresPending()
        {
            string oldHash = await CacheBuildOneAsync();
            now = Start.AddDays(2);
            http.Respond = url => OpenCompositeFixture.Dll(PeFixture.MachineX64, 2);
            var warnings = new List<string>();

            IReadOnlyList<DownloadOutcome> outcomes = await cache.CheckForUpdatesAsync(warnings, CancellationToken.None);

            Assert.AreEqual(1, outcomes.Count);
            Assert.AreEqual(DownloadStatus.UpdateFound, outcomes[0].Status);
            Assert.AreEqual(oldHash, FileHash.Sha256(cache.DllPath(OpenCompositeArch.X64)));
            Assert.IsTrue(File.Exists(cache.PendingDllPath(OpenCompositeArch.X64)));
            CachedBuild build = cache.GetBuilds(warnings)[0];
            Assert.AreEqual(oldHash, build.Sha256);
            Assert.IsTrue(build.HasPendingUpdate);
            Assert.AreEqual(FileHash.Sha256(cache.PendingDllPath(OpenCompositeArch.X64)), build.PendingSha256);
            Assert.AreEqual((DateTime?)now, build.PendingDownloadedUtc);
            Assert.AreEqual((DateTime?)now, settings.Load(warnings).LastUpdateCheckUtc);
            Assert.AreEqual(0, warnings.Count);
        }

        [TestMethod]
        public async Task CheckForUpdates_OnlyCachedArchitecturesAreChecked()
        {
            await CacheBuildOneAsync();

            await cache.CheckForUpdatesAsync(new List<string>(), CancellationToken.None);

            CollectionAssert.AreEqual(new[] { OpenCompositeCache.SourceUrl(OpenCompositeArch.X64) }, http.RequestedUrls);
        }

        [TestMethod]
        public async Task CheckForUpdates_InvalidDownload_KeepsCacheAndDoesNotRecordTime()
        {
            string hash = await CacheBuildOneAsync();
            http.Respond = url => OpenCompositeFixture.HtmlPage();
            var warnings = new List<string>();

            IReadOnlyList<DownloadOutcome> outcomes = await cache.CheckForUpdatesAsync(warnings, CancellationToken.None);

            Assert.AreEqual(DownloadStatus.Failed, outcomes[0].Status);
            Assert.AreEqual(hash, FileHash.Sha256(cache.DllPath(OpenCompositeArch.X64)));
            Assert.IsFalse(File.Exists(cache.PendingDllPath(OpenCompositeArch.X64)));
            Assert.IsFalse(cache.GetBuilds(warnings)[0].HasPendingUpdate);
            Assert.IsNull(settings.Load(warnings).LastUpdateCheckUtc);
        }

        [TestMethod]
        public async Task CheckForUpdates_UpstreamWentBack_DropsPending()
        {
            await CacheBuildOneAsync();
            http.Respond = url => OpenCompositeFixture.Dll(PeFixture.MachineX64, 2);
            await cache.CheckForUpdatesAsync(new List<string>(), CancellationToken.None);
            http.Respond = url => OpenCompositeFixture.Dll(PeFixture.MachineX64, 1);
            var warnings = new List<string>();

            IReadOnlyList<DownloadOutcome> outcomes = await cache.CheckForUpdatesAsync(warnings, CancellationToken.None);

            Assert.AreEqual(DownloadStatus.Unchanged, outcomes[0].Status);
            Assert.IsFalse(File.Exists(cache.PendingDllPath(OpenCompositeArch.X64)));
            Assert.IsFalse(cache.GetBuilds(warnings)[0].HasPendingUpdate);
        }

        [TestMethod]
        public async Task CheckForUpdates_LicenseNoLongerAccepted_SaysSoWithoutNetwork()
        {
            await CacheBuildOneAsync();
            settings.Save(new AppSettings());

            IReadOnlyList<DownloadOutcome> outcomes = await cache.CheckForUpdatesAsync(new List<string>(), CancellationToken.None);

            // R35: never a silent no-op.
            Assert.AreEqual(1, outcomes.Count);
            Assert.AreEqual(OpenCompositeArch.X64, outcomes[0].Arch);
            Assert.AreEqual(DownloadStatus.LicenseNotAccepted, outcomes[0].Status);
            StringAssert.Contains(outcomes[0].Message, "license notice");
            Assert.AreEqual(0, http.RequestedUrls.Count);
            Assert.IsNull(settings.Load(new List<string>()).LastUpdateCheckUtc);
        }

        [TestMethod]
        public async Task CheckForUpdates_LicenseNotAcceptedAndNothingCached_ReportsNothing()
        {
            IReadOnlyList<DownloadOutcome> outcomes = await cache.CheckForUpdatesAsync(new List<string>(), CancellationToken.None);

            Assert.AreEqual(0, outcomes.Count);
            Assert.AreEqual(0, http.RequestedUrls.Count);
        }

        [TestMethod]
        public async Task CheckForUpdatesIfDue_CheckedRecently_DoesNothing()
        {
            await CacheBuildOneAsync();
            settings.Save(new AppSettings { OpenCompositeLicenseAccepted = true, LastUpdateCheckUtc = Start.AddHours(-1) });

            IReadOnlyList<DownloadOutcome> outcomes = await cache.CheckForUpdatesIfDueAsync(new List<string>(), CancellationToken.None);

            Assert.AreEqual(0, outcomes.Count);
            Assert.AreEqual(0, http.RequestedUrls.Count);
        }

        [TestMethod]
        public async Task CheckForUpdatesIfDue_LastCheckInFuture_Checks()
        {
            await CacheBuildOneAsync();
            settings.Save(new AppSettings { OpenCompositeLicenseAccepted = true, LastUpdateCheckUtc = Start.AddDays(3) });
            var warnings = new List<string>();

            IReadOnlyList<DownloadOutcome> outcomes = await cache.CheckForUpdatesIfDueAsync(warnings, CancellationToken.None);

            Assert.AreEqual(1, outcomes.Count);
            Assert.AreEqual(1, http.RequestedUrls.Count);
            Assert.AreEqual((DateTime?)Start, settings.Load(warnings).LastUpdateCheckUtc);
        }

        [TestMethod]
        public async Task AcceptPendingUpdate_PromotesNewBuild()
        {
            await CacheBuildOneAsync();
            now = Start.AddDays(2);
            http.Respond = url => OpenCompositeFixture.Dll(PeFixture.MachineX64, 2);
            await cache.CheckForUpdatesAsync(new List<string>(), CancellationToken.None);
            string newHash = FileHash.Sha256(cache.PendingDllPath(OpenCompositeArch.X64));
            now = Start.AddDays(3);
            var warnings = new List<string>();

            DownloadOutcome outcome = cache.AcceptPendingUpdate(OpenCompositeArch.X64, warnings);

            Assert.AreEqual(DownloadStatus.UpdateAccepted, outcome.Status);
            Assert.AreEqual(newHash, FileHash.Sha256(cache.DllPath(OpenCompositeArch.X64)));
            Assert.IsFalse(File.Exists(cache.PendingDllPath(OpenCompositeArch.X64)));
            CachedBuild build = cache.GetBuilds(warnings)[0];
            Assert.AreEqual(newHash, build.Sha256);
            Assert.IsFalse(build.HasPendingUpdate);
            Assert.AreEqual((DateTime?)Start.AddDays(2), build.DownloadedUtc);
            Assert.AreEqual(0, warnings.Count);
        }

        [TestMethod]
        public async Task AcceptPendingUpdate_DamagedPendingFile_IsDiscarded()
        {
            string oldHash = await CacheBuildOneAsync();
            http.Respond = url => OpenCompositeFixture.Dll(PeFixture.MachineX64, 2);
            await cache.CheckForUpdatesAsync(new List<string>(), CancellationToken.None);
            File.WriteAllBytes(cache.PendingDllPath(OpenCompositeArch.X64), OpenCompositeFixture.Dll(PeFixture.MachineX64, 3));
            var warnings = new List<string>();

            DownloadOutcome outcome = cache.AcceptPendingUpdate(OpenCompositeArch.X64, warnings);

            Assert.AreEqual(DownloadStatus.Failed, outcome.Status);
            StringAssert.Contains(outcome.Message, "damaged");
            Assert.AreEqual(oldHash, FileHash.Sha256(cache.DllPath(OpenCompositeArch.X64)));
            Assert.IsFalse(File.Exists(cache.PendingDllPath(OpenCompositeArch.X64)));
            Assert.IsFalse(cache.GetBuilds(warnings)[0].HasPendingUpdate);
        }

        [TestMethod]
        public async Task AcceptPendingUpdate_NothingPending_Fails()
        {
            await CacheBuildOneAsync();

            DownloadOutcome outcome = cache.AcceptPendingUpdate(OpenCompositeArch.X64, new List<string>());

            Assert.AreEqual(DownloadStatus.Failed, outcome.Status);
            StringAssert.Contains(outcome.Message, "No new");
        }

        /// <summary>
        /// Fix round 1, Important finding: an earlier AcceptPendingUpdate call can be interrupted (crash, or a
        /// disk error saving cache.json) right after AtomicFile.Replace already promoted the .new file to the
        /// current DLL, leaving cache.json still recording Sha256=old build / PendingSha256=new build and no
        /// .new file on disk. AcceptPendingUpdate must recognize that state from the DLL's own hash instead of
        /// reporting "nothing waiting" (the file was, in fact, already switched).
        /// </summary>
        [TestMethod]
        public async Task AcceptPendingUpdate_InterruptedAfterPromotingFile_RepairsCacheAndReportsAccepted()
        {
            await CacheBuildOneAsync();
            now = Start.AddDays(2);
            http.Respond = url => OpenCompositeFixture.Dll(PeFixture.MachineX64, 2);
            await cache.CheckForUpdatesAsync(new List<string>(), CancellationToken.None);
            string newHash = FileHash.Sha256(cache.PendingDllPath(OpenCompositeArch.X64));
            // Simulate the interruption: copy .new over the DLL (as AtomicFile.Replace would have) and delete
            // .new, but leave cache.json exactly as it was before the record was updated.
            File.Copy(cache.PendingDllPath(OpenCompositeArch.X64), cache.DllPath(OpenCompositeArch.X64), true);
            File.Delete(cache.PendingDllPath(OpenCompositeArch.X64));
            var warnings = new List<string>();

            DownloadOutcome outcome = cache.AcceptPendingUpdate(OpenCompositeArch.X64, warnings);

            Assert.AreEqual(DownloadStatus.UpdateAccepted, outcome.Status);
            Assert.AreEqual(newHash, FileHash.Sha256(cache.DllPath(OpenCompositeArch.X64)));
            CachedBuild build = cache.GetBuilds(warnings)[0];
            Assert.AreEqual(newHash, build.Sha256);
            Assert.IsFalse(build.HasPendingUpdate);
            Assert.AreEqual(0, warnings.Count);
        }

        /// <summary>
        /// Fix round 1, item 1: RecordCheckTime must not call settings.Save when settings.Load itself already
        /// warned (an unreadable or corrupt settings.json), because Save would then persist a fresh default
        /// AppSettings and silently reset OpenCompositeLicenseAccepted to false. The settings file becomes
        /// unreadable partway through the check (via a side effect of the fake download), after the license
        /// gate at the start of CheckForUpdatesAsync already passed, so this exercises RecordCheckTime's own
        /// Load call rather than the license gate.
        /// </summary>
        [TestMethod]
        public async Task CheckForUpdates_SettingsBecomeUnreadableBeforeRecordingTime_DoesNotSaveOrResetSettings()
        {
            await CacheBuildOneAsync();
            string settingsPath = settings.FilePath;
            http.Respond = url =>
            {
                File.WriteAllText(settingsPath, "not json");
                return OpenCompositeFixture.Dll(PeFixture.MachineX64, 1);
            };
            var warnings = new List<string>();

            IReadOnlyList<DownloadOutcome> outcomes = await cache.CheckForUpdatesAsync(warnings, CancellationToken.None);

            Assert.AreEqual(DownloadStatus.Unchanged, outcomes[0].Status);
            Assert.AreEqual("not json", File.ReadAllText(settingsPath));
            Assert.IsTrue(warnings.Count > 0);
        }

        /// <summary>
        /// Fix round 1, item 2: if the cache.json record for an architecture disappears between GetBuilds and
        /// CheckOneAsync's own read of the file (edited or deleted from under us), CheckOneAsync must report
        /// Failed instead of recreating the entry from an empty map, which would silently drop any other
        /// architecture's record from the file it then writes back.
        /// </summary>
        [TestMethod]
        public async Task CheckForUpdates_CacheRecordMissingMidCheck_ReturnsFailedWithoutRecreatingEntry()
        {
            await CacheBuildOneAsync();
            string cacheJsonPath = cache.CacheFilePath;
            http.Respond = url =>
            {
                File.WriteAllText(cacheJsonPath, "{}");
                return OpenCompositeFixture.Dll(PeFixture.MachineX64, 2);
            };
            var warnings = new List<string>();

            IReadOnlyList<DownloadOutcome> outcomes = await cache.CheckForUpdatesAsync(warnings, CancellationToken.None);

            Assert.AreEqual(DownloadStatus.Failed, outcomes[0].Status);
            StringAssert.Contains(outcomes[0].Message, "missing");
            Assert.AreEqual("{}", File.ReadAllText(cacheJsonPath));
        }

        /// <summary>
        /// Fix round 1, item 3(i): one architecture failing must keep the whole check from recording the
        /// last-check time, even though the other architecture succeeded.
        /// </summary>
        [TestMethod]
        public async Task CheckForUpdates_OneArchitectureFails_DoesNotRecordCheckTime()
        {
            settings.Save(new AppSettings { OpenCompositeLicenseAccepted = true });
            http.Respond = url => OpenCompositeFixture.Dll(PeFixture.MachineX64, 1);
            await cache.DownloadAsync(OpenCompositeArch.X64, new List<string>(), CancellationToken.None);
            http.Respond = url => OpenCompositeFixture.Dll(PeFixture.MachineX86, 1);
            await cache.DownloadAsync(OpenCompositeArch.X86, new List<string>(), CancellationToken.None);
            http.RequestedUrls.Clear();
            http.Respond = url => url.IndexOf("arch=x86", StringComparison.Ordinal) >= 0
                ? throw new InvalidOperationException("Simulated network failure for x86.")
                : OpenCompositeFixture.Dll(PeFixture.MachineX64, 1);
            var warnings = new List<string>();

            IReadOnlyList<DownloadOutcome> outcomes = await cache.CheckForUpdatesAsync(warnings, CancellationToken.None);

            Assert.AreEqual(2, outcomes.Count);
            Assert.AreEqual(OpenCompositeArch.X64, outcomes[0].Arch);
            Assert.AreEqual(DownloadStatus.Unchanged, outcomes[0].Status);
            Assert.AreEqual(OpenCompositeArch.X86, outcomes[1].Arch);
            Assert.AreEqual(DownloadStatus.Failed, outcomes[1].Status);
            Assert.IsNull(settings.Load(warnings).LastUpdateCheckUtc);
        }

        /// <summary>
        /// Fix round 1, item 3(ii): an invalid download must leave an already-waiting pending update completely
        /// untouched, not just the current build.
        /// </summary>
        [TestMethod]
        public async Task CheckForUpdates_InvalidDownloadWithExistingPending_LeavesPendingIntact()
        {
            await CacheBuildOneAsync();
            http.Respond = url => OpenCompositeFixture.Dll(PeFixture.MachineX64, 2);
            await cache.CheckForUpdatesAsync(new List<string>(), CancellationToken.None);
            string pendingHash = FileHash.Sha256(cache.PendingDllPath(OpenCompositeArch.X64));
            http.Respond = url => OpenCompositeFixture.HtmlPage();
            var warnings = new List<string>();

            IReadOnlyList<DownloadOutcome> outcomes = await cache.CheckForUpdatesAsync(warnings, CancellationToken.None);

            Assert.AreEqual(DownloadStatus.Failed, outcomes[0].Status);
            Assert.IsTrue(File.Exists(cache.PendingDllPath(OpenCompositeArch.X64)));
            Assert.AreEqual(pendingHash, FileHash.Sha256(cache.PendingDllPath(OpenCompositeArch.X64)));
            CachedBuild build = cache.GetBuilds(warnings)[0];
            Assert.IsTrue(build.HasPendingUpdate);
            Assert.AreEqual(pendingHash, build.PendingSha256);
        }

        /// <summary>
        /// Fix round 1, item 3(iii): license accepted but nothing cached yet must neither hit the network nor
        /// record a check time (there is nothing to check).
        /// </summary>
        [TestMethod]
        public async Task CheckForUpdates_NothingCached_NoRequestsOrCheckTime()
        {
            settings.Save(new AppSettings { OpenCompositeLicenseAccepted = true });
            var warnings = new List<string>();

            IReadOnlyList<DownloadOutcome> outcomes = await cache.CheckForUpdatesAsync(warnings, CancellationToken.None);

            Assert.AreEqual(0, outcomes.Count);
            Assert.AreEqual(0, http.RequestedUrls.Count);
            Assert.IsNull(settings.Load(warnings).LastUpdateCheckUtc);
        }
    }
}
