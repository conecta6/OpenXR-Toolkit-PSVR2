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
        public async Task CheckForUpdates_LicenseNoLongerAccepted_DoesNothing()
        {
            await CacheBuildOneAsync();
            settings.Save(new AppSettings());

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
    }
}
