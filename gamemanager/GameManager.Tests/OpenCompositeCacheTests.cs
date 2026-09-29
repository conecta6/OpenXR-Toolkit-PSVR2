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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    [TestClass]
    public class OpenCompositeCacheTests
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc);

        private TempDir temp;
        private AppDataPaths paths;
        private SettingsStore settings;
        private FakeHttpDownloader http;
        private OpenCompositeCache cache;

        [TestInitialize]
        public void SetUp()
        {
            temp = new TempDir();
            paths = new AppDataPaths(temp.PathOf("AppData"));
            settings = new SettingsStore(paths.SettingsFile);
            http = new FakeHttpDownloader();
            cache = new OpenCompositeCache(paths, settings, http, () => Now);
        }

        [TestCleanup]
        public void TearDown()
        {
            temp.Dispose();
        }

        private void AcceptLicense()
        {
            settings.Save(new AppSettings { OpenCompositeLicenseAccepted = true });
        }

        private string[] AllTempFiles()
        {
            return Directory.Exists(paths.OpenCompositeFolder)
                ? Directory.GetFiles(paths.OpenCompositeFolder, "*.tmp", SearchOption.AllDirectories)
                : new string[0];
        }

        [TestMethod]
        public void SourceUrl_MatchesUpstream()
        {
            Assert.AreEqual("https://znix.xyz/OpenComposite/download.php?arch=x64&branch=openxr", OpenCompositeCache.SourceUrl(OpenCompositeArch.X64));
            Assert.AreEqual("https://znix.xyz/OpenComposite/download.php?arch=x86&branch=openxr", OpenCompositeCache.SourceUrl(OpenCompositeArch.X86));
        }

        [TestMethod]
        public void AcceptLicense_IsStoredInSettings()
        {
            var warnings = new List<string>();
            Assert.IsFalse(cache.IsLicenseAccepted(warnings));

            cache.AcceptLicense();

            Assert.IsTrue(cache.IsLicenseAccepted(warnings));
            Assert.AreEqual((DateTime?)Now, settings.Load(warnings).OpenCompositeLicenseAcceptedUtc);
            Assert.AreEqual(0, warnings.Count);
        }

        [TestMethod]
        public async Task Download_LicenseNotAccepted_DoesNotTouchNetworkOrDisk()
        {
            http.Respond = url => OpenCompositeFixture.Dll(PeFixture.MachineX64, 1);

            DownloadOutcome outcome = await cache.DownloadAsync(OpenCompositeArch.X64, new List<string>(), CancellationToken.None);

            Assert.AreEqual(DownloadStatus.LicenseNotAccepted, outcome.Status);
            Assert.AreEqual(0, http.RequestedUrls.Count);
            Assert.IsFalse(Directory.Exists(paths.OpenCompositeFolder));
        }

        [TestMethod]
        public async Task Download_ValidX64_StoresDllAndRecordsHash()
        {
            AcceptLicense();
            byte[] dll = OpenCompositeFixture.Dll(PeFixture.MachineX64, 1);
            http.Respond = url => dll;
            var warnings = new List<string>();

            DownloadOutcome outcome = await cache.DownloadAsync(OpenCompositeArch.X64, warnings, CancellationToken.None);

            Assert.AreEqual(DownloadStatus.Downloaded, outcome.Status);
            CollectionAssert.AreEqual(new[] { "https://znix.xyz/OpenComposite/download.php?arch=x64&branch=openxr" }, http.RequestedUrls);
            string cached = temp.PathOf(@"AppData\opencomposite\x64\openvr_api.dll");
            Assert.AreEqual(cached, cache.DllPath(OpenCompositeArch.X64), true);
            CollectionAssert.AreEqual(dll, File.ReadAllBytes(cached));
            Assert.IsTrue(File.Exists(temp.PathOf(@"AppData\opencomposite\cache.json")));
            IReadOnlyList<CachedBuild> builds = cache.GetBuilds(warnings);
            Assert.AreEqual(1, builds.Count);
            Assert.AreEqual(OpenCompositeArch.X64, builds[0].Arch);
            Assert.AreEqual(FileHash.Sha256(cached), builds[0].Sha256);
            Assert.AreEqual((DateTime?)Now, builds[0].DownloadedUtc);
            Assert.AreEqual(OpenCompositeCache.SourceUrl(OpenCompositeArch.X64), builds[0].SourceUrl);
            Assert.IsFalse(builds[0].HasPendingUpdate);
            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual(0, AllTempFiles().Length);
        }

        [TestMethod]
        public async Task Download_X86_UsesX86UrlAndFolder()
        {
            AcceptLicense();
            http.Respond = url => OpenCompositeFixture.Dll(PeFixture.MachineX86, 1);

            DownloadOutcome outcome = await cache.DownloadAsync(OpenCompositeArch.X86, new List<string>(), CancellationToken.None);

            Assert.AreEqual(DownloadStatus.Downloaded, outcome.Status);
            CollectionAssert.AreEqual(new[] { "https://znix.xyz/OpenComposite/download.php?arch=x86&branch=openxr" }, http.RequestedUrls);
            Assert.IsTrue(File.Exists(temp.PathOf(@"AppData\opencomposite\x86\openvr_api.dll")));
            Assert.AreEqual(OpenCompositeArch.X86, cache.GetBuilds(new List<string>())[0].Arch);
        }

        [TestMethod]
        public async Task Download_HtmlPageInsteadOfDll_KeepsExistingCache()
        {
            AcceptLicense();
            http.Respond = url => OpenCompositeFixture.Dll(PeFixture.MachineX64, 1);
            await cache.DownloadAsync(OpenCompositeArch.X64, new List<string>(), CancellationToken.None);
            string before = FileHash.Sha256(cache.DllPath(OpenCompositeArch.X64));
            // A captive portal or proxy answering 200 OK with a large HTML page.
            http.Respond = url => OpenCompositeFixture.HtmlPage();
            var warnings = new List<string>();

            DownloadOutcome outcome = await cache.DownloadAsync(OpenCompositeArch.X64, warnings, CancellationToken.None);

            Assert.AreEqual(DownloadStatus.Failed, outcome.Status);
            StringAssert.Contains(outcome.Message, "not a Windows DLL");
            Assert.AreEqual(before, FileHash.Sha256(cache.DllPath(OpenCompositeArch.X64)));
            Assert.AreEqual(before, cache.GetBuilds(warnings)[0].Sha256);
            Assert.AreEqual(0, AllTempFiles().Length);
        }

        [TestMethod]
        public async Task Download_WrongArchitecture_IsRejected()
        {
            AcceptLicense();
            http.Respond = url => OpenCompositeFixture.Dll(PeFixture.MachineX86, 1);

            DownloadOutcome outcome = await cache.DownloadAsync(OpenCompositeArch.X64, new List<string>(), CancellationToken.None);

            Assert.AreEqual(DownloadStatus.Failed, outcome.Status);
            StringAssert.Contains(outcome.Message, "x86 DLL, expected x64");
            Assert.IsFalse(File.Exists(cache.DllPath(OpenCompositeArch.X64)));
            Assert.AreEqual(0, AllTempFiles().Length);
        }

        [TestMethod]
        public async Task Download_TooSmall_IsRejected()
        {
            AcceptLicense();
            http.Respond = url => PeFixture.Build(PeFixture.MachineX64);

            DownloadOutcome outcome = await cache.DownloadAsync(OpenCompositeArch.X64, new List<string>(), CancellationToken.None);

            Assert.AreEqual(DownloadStatus.Failed, outcome.Status);
            StringAssert.Contains(outcome.Message, "too small");
            Assert.IsFalse(File.Exists(cache.DllPath(OpenCompositeArch.X64)));
            Assert.AreEqual(0, AllTempFiles().Length);
        }

        [TestMethod]
        [DataRow("http")]
        [DataRow("timeout")]
        [DataRow("httpclient-timeout")]
        public async Task Download_NetworkError_FailsWithoutCaching(string failure)
        {
            AcceptLicense();
            Exception error = failure == "http"
                ? (Exception)new HttpRequestException("Response status code does not indicate success: 503 (Service Unavailable).")
                : failure == "timeout"
                    ? new TimeoutException("The server did not answer within 60 seconds.")
                    : new TaskCanceledException("A task was canceled.");
            http.Respond = url => throw error;
            var warnings = new List<string>();

            DownloadOutcome outcome = await cache.DownloadAsync(OpenCompositeArch.X64, warnings, CancellationToken.None);

            Assert.AreEqual(DownloadStatus.Failed, outcome.Status);
            StringAssert.Contains(outcome.Message, "failed");
            Assert.IsFalse(File.Exists(cache.DllPath(OpenCompositeArch.X64)));
            Assert.AreEqual(0, cache.GetBuilds(warnings).Count);
            Assert.AreEqual(0, AllTempFiles().Length);
        }

        /// <summary>
        /// F3: unlike Download_NetworkError_FailsWithoutCaching (whose fake throws before writing anything, so
        /// "no temp file left behind" would pass even if the cleanup code were deleted), this simulates a
        /// connection that drops after delivering some bytes, so a temporary file genuinely exists on disk when
        /// FetchValidatedAsync's cleanup has to remove it.
        /// </summary>
        [TestMethod]
        public async Task Download_ConnectionDropsMidTransfer_LeavesNoTempFile()
        {
            AcceptLicense();
            http.WritePartialThenThrow = stream => stream.Write(new byte[50 * 1024], 0, 50 * 1024);
            var warnings = new List<string>();

            DownloadOutcome outcome = await cache.DownloadAsync(OpenCompositeArch.X64, warnings, CancellationToken.None);

            Assert.AreEqual(DownloadStatus.Failed, outcome.Status);
            StringAssert.Contains(outcome.Message, "failed");
            Assert.IsFalse(File.Exists(cache.DllPath(OpenCompositeArch.X64)));
            Assert.AreEqual(0, AllTempFiles().Length);
        }

        /// <summary>
        /// Regression test for the fix-round-2 finding: FetchValidatedAsync's Directory.CreateDirectory (and the
        /// stale-temp-file cleanup right after it) used to run inside a try with only a finally, so a disk error
        /// there escaped DownloadAsync as an unhandled exception instead of the documented Failed outcome. A file
        /// sitting where the architecture folder should be forces Directory.CreateDirectory to throw IOException.
        /// </summary>
        [TestMethod]
        public async Task Download_ArchitectureFolderPathIsAFile_ReturnsFailedWithoutThrowingAndLeavesCacheUntouched()
        {
            AcceptLicense();
            http.Respond = url => OpenCompositeFixture.Dll(PeFixture.MachineX86, 1);
            await cache.DownloadAsync(OpenCompositeArch.X86, new List<string>(), CancellationToken.None);
            string cacheJsonBefore = File.ReadAllText(temp.PathOf(@"AppData\opencomposite\cache.json"));
            string x86HashBefore = FileHash.Sha256(cache.DllPath(OpenCompositeArch.X86));

            // Sabotage the x64 architecture folder: a file where OpenCompositeCache expects a directory.
            temp.WriteText(@"AppData\opencomposite\x64", "not a directory");
            var warnings = new List<string>();

            DownloadOutcome outcome = await cache.DownloadAsync(OpenCompositeArch.X64, warnings, CancellationToken.None);

            Assert.AreEqual(DownloadStatus.Failed, outcome.Status);
            StringAssert.Contains(outcome.Message, "failed");
            Assert.AreEqual(cacheJsonBefore, File.ReadAllText(temp.PathOf(@"AppData\opencomposite\cache.json")));
            Assert.AreEqual(x86HashBefore, FileHash.Sha256(cache.DllPath(OpenCompositeArch.X86)));
            Assert.AreEqual(0, AllTempFiles().Length);
        }

        [TestMethod]
        public async Task Download_UserCancellation_Throws()
        {
            AcceptLicense();
            http.Respond = url => OpenCompositeFixture.Dll(PeFixture.MachineX64, 1);

            await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                () => cache.DownloadAsync(OpenCompositeArch.X64, new List<string>(), new CancellationToken(true)));

            Assert.IsFalse(File.Exists(cache.DllPath(OpenCompositeArch.X64)));
            Assert.AreEqual(0, AllTempFiles().Length);
        }

        /// <summary>
        /// Unlike Download_UserCancellation_Throws (an already-canceled token, so FakeHttpDownloader throws
        /// before writing anything — the cleanup line in FetchValidatedAsync's cancellation path is never
        /// actually exercised), this writes real bytes first and cancels the caller's own token only as the
        /// simulated connection drops, so a temporary file genuinely exists on disk when the cancellation-path
        /// cleanup has to remove it.
        /// </summary>
        [TestMethod]
        public async Task Download_CanceledMidTransfer_PropagatesAndLeavesNoTempFile()
        {
            AcceptLicense();
            using (var cts = new CancellationTokenSource())
            {
                http.WritePartialThenThrow = stream => stream.Write(new byte[50 * 1024], 0, 50 * 1024);
                http.ExceptionAfterPartialWrite = () =>
                {
                    // Cancel the caller's own token first, so the cache sees a token that is genuinely
                    // canceled by the time it inspects cancellation.IsCancellationRequested, not just an
                    // OperationCanceledException thrown for its type alone.
                    cts.Cancel();
                    return new OperationCanceledException("Canceled mid-download.", cts.Token);
                };

                await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                    () => cache.DownloadAsync(OpenCompositeArch.X64, new List<string>(), cts.Token));

                Assert.IsFalse(File.Exists(cache.DllPath(OpenCompositeArch.X64)));
                Assert.AreEqual(0, AllTempFiles().Length);
            }
        }

        [TestMethod]
        public async Task Download_RemovesStaleTempFilesFromEarlierRun()
        {
            AcceptLicense();
            temp.WriteText(@"AppData\opencomposite\x64\openvr_api.dll.download-old.tmp", "partial");
            http.Respond = url => OpenCompositeFixture.Dll(PeFixture.MachineX64, 1);

            await cache.DownloadAsync(OpenCompositeArch.X64, new List<string>(), CancellationToken.None);

            Assert.AreEqual(0, AllTempFiles().Length);
        }

        [TestMethod]
        public void GetBuilds_CorruptCacheFile_ReturnsEmptyWithWarning()
        {
            temp.WriteBytes(@"AppData\opencomposite\x64\openvr_api.dll", OpenCompositeFixture.Dll(PeFixture.MachineX64, 1));
            temp.WriteText(@"AppData\opencomposite\cache.json", "not json");
            var warnings = new List<string>();

            IReadOnlyList<CachedBuild> builds = cache.GetBuilds(warnings);

            Assert.AreEqual(0, builds.Count);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings[0], "cache.json");
        }

        [TestMethod]
        public async Task GetBuilds_DllDeletedByUser_IsDroppedWithWarning()
        {
            AcceptLicense();
            http.Respond = url => OpenCompositeFixture.Dll(PeFixture.MachineX64, 1);
            await cache.DownloadAsync(OpenCompositeArch.X64, new List<string>(), CancellationToken.None);
            File.Delete(cache.DllPath(OpenCompositeArch.X64));
            var warnings = new List<string>();

            IReadOnlyList<CachedBuild> builds = cache.GetBuilds(warnings);

            Assert.AreEqual(0, builds.Count);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings[0], "missing");
        }
    }
}
