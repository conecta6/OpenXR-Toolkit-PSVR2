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
using System.IO;
using System.Runtime.Serialization;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace GameManager.Core
{
    /// <summary>
    /// The OpenComposite builds upstream publishes. There is no ARM64 build (R11).
    /// </summary>
    public enum OpenCompositeArch
    {
        X64,
        X86,
    }

    public sealed class CachedBuild
    {
        public CachedBuild(
            OpenCompositeArch arch,
            string dllPath,
            string sha256,
            DateTime? downloadedUtc,
            string sourceUrl,
            string pendingSha256,
            DateTime? pendingDownloadedUtc)
        {
            Arch = arch;
            DllPath = dllPath;
            Sha256 = sha256;
            DownloadedUtc = downloadedUtc;
            SourceUrl = sourceUrl;
            PendingSha256 = pendingSha256;
            PendingDownloadedUtc = pendingDownloadedUtc;
        }

        public OpenCompositeArch Arch { get; }
        public string DllPath { get; }

        /// <summary>
        /// SHA-256 of the cached DLL as recorded in cache.json (lowercase hex).
        /// </summary>
        public string Sha256 { get; }

        public DateTime? DownloadedUtc { get; }
        public string SourceUrl { get; }

        /// <summary>
        /// SHA-256 of a newer upstream build waiting as openvr_api.dll.new, or null.
        /// </summary>
        public string PendingSha256 { get; }

        public DateTime? PendingDownloadedUtc { get; }

        public bool HasPendingUpdate
        {
            get { return PendingSha256 != null; }
        }
    }

    public enum DownloadStatus
    {
        /// <summary>DownloadAsync stored a new cached DLL.</summary>
        Downloaded,

        /// <summary>Update check: upstream still serves the cached build.</summary>
        Unchanged,

        /// <summary>Update check: a different build was stored as openvr_api.dll.new.</summary>
        UpdateFound,

        /// <summary>AcceptPendingUpdate made the waiting build the current one.</summary>
        UpdateAccepted,

        /// <summary>Nothing was downloaded: the license notice has not been accepted.</summary>
        LicenseNotAccepted,

        /// <summary>Network error, invalid file or disk error; Message says which.</summary>
        Failed,
    }

    public sealed class DownloadOutcome
    {
        public DownloadOutcome(OpenCompositeArch arch, DownloadStatus status, string message)
        {
            Arch = arch;
            Status = status;
            Message = message;
        }

        public OpenCompositeArch Arch { get; }
        public DownloadStatus Status { get; }
        public string Message { get; }
    }

    /// <summary>
    /// The per-user OpenComposite cache (R11): opencomposite\{x64|x86}\openvr_api.dll plus opencomposite\cache.json,
    /// a map { "x64": { sha256, downloadedUtc, sourceUrl, pendingSha256, pendingDownloadedUtc }, ... }.
    /// A download goes to a temporary file next to the cached DLL. IHttpDownloader itself rejects a body shorter
    /// than the server's declared Content-Length (F1), which catches most truncated transfers before the file
    /// even reaches this class; what does arrive is then checked here (size, PE header, architecture) and only
    /// then replaces the cached DLL, so a failed or invalid download never replaces a good one (R12).
    /// </summary>
    public sealed class OpenCompositeCache
    {
        /// <summary>
        /// R12: 100 KB or less is an error page, not the ~2.5 MB DLL.
        /// </summary>
        public const long MinimumDllSize = 100 * 1024;

        private const string DllName = "openvr_api.dll";
        private const string PendingSuffix = ".new";
        private const string TempPattern = DllName + ".download-*.tmp";

        private readonly AppDataPaths paths;
        private readonly SettingsStore settings;
        private readonly IHttpDownloader http;
        private readonly Func<DateTime> utcNow;

        public OpenCompositeCache(AppDataPaths paths, SettingsStore settings, IHttpDownloader http, Func<DateTime> utcNow)
        {
            this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.http = http ?? throw new ArgumentNullException(nameof(http));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public static IReadOnlyList<OpenCompositeArch> AllArchitectures { get; } = new[] { OpenCompositeArch.X64, OpenCompositeArch.X86 };

        public static string ArchName(OpenCompositeArch arch)
        {
            return arch == OpenCompositeArch.X64 ? "x64" : "x86";
        }

        public static string SourceUrl(OpenCompositeArch arch)
        {
            return "https://znix.xyz/OpenComposite/download.php?arch=" + ArchName(arch) + "&branch=openxr";
        }

        public static PeMachine ExpectedMachine(OpenCompositeArch arch)
        {
            return arch == OpenCompositeArch.X64 ? PeMachine.X64 : PeMachine.X86;
        }

        public string CacheFilePath
        {
            get { return Path.Combine(paths.OpenCompositeFolder, "cache.json"); }
        }

        public string DllPath(OpenCompositeArch arch)
        {
            return Path.Combine(ArchFolder(arch), DllName);
        }

        public string PendingDllPath(OpenCompositeArch arch)
        {
            return DllPath(arch) + PendingSuffix;
        }

        private string ArchFolder(OpenCompositeArch arch)
        {
            return Path.Combine(paths.OpenCompositeFolder, ArchName(arch));
        }

        public bool IsLicenseAccepted(IList<string> warnings)
        {
            return settings.Load(warnings).OpenCompositeLicenseAccepted;
        }

        /// <summary>
        /// Records that the user accepted the GPLv3 notice (R13). I/O errors pass through.
        /// </summary>
        public void AcceptLicense()
        {
            AppSettings current = settings.Load(new List<string>());
            current.OpenCompositeLicenseAccepted = true;
            current.OpenCompositeLicenseAcceptedUtc = utcNow();
            settings.Save(current);
        }

        /// <summary>
        /// Recorded builds whose DLL is on disk, x64 first. A recorded build whose DLL is gone gives a warning.
        /// </summary>
        public IReadOnlyList<CachedBuild> GetBuilds(IList<string> warnings)
        {
            Dictionary<string, CacheEntryDto> file = ReadCacheFile(warnings);
            var builds = new List<CachedBuild>();
            foreach (OpenCompositeArch arch in AllArchitectures)
            {
                CacheEntryDto entry;
                if (!file.TryGetValue(ArchName(arch), out entry))
                {
                    continue;
                }
                if (!File.Exists(DllPath(arch)))
                {
                    warnings.Add("The cached OpenComposite " + ArchName(arch) + " DLL is missing (" + DllPath(arch) + "). Download OpenComposite again.");
                    continue;
                }
                bool pending = entry.PendingSha256 != null && File.Exists(PendingDllPath(arch));
                builds.Add(new CachedBuild(
                    arch,
                    DllPath(arch),
                    entry.Sha256,
                    JsonFile.ParseUtc(entry.DownloadedUtc),
                    entry.SourceUrl,
                    pending ? entry.PendingSha256 : null,
                    pending ? JsonFile.ParseUtc(entry.PendingDownloadedUtc) : null));
            }
            return builds;
        }

        /// <summary>
        /// Downloads the latest build of one architecture and makes it the cached one; a waiting update is dropped.
        /// Throws OperationCanceledException only when cancellation is requested; every other problem is a
        /// Failed outcome. A network error or an invalid file never touches the cache.
        /// </summary>
        public async Task<DownloadOutcome> DownloadAsync(OpenCompositeArch arch, IList<string> warnings, CancellationToken cancellation)
        {
            if (!IsLicenseAccepted(warnings))
            {
                return new DownloadOutcome(arch, DownloadStatus.LicenseNotAccepted, "OpenComposite was not downloaded: its license notice has not been accepted.");
            }
            FetchResult fetched = await FetchValidatedAsync(arch, cancellation).ConfigureAwait(false);
            if (fetched.Error != null)
            {
                return Failed(arch, fetched.Error);
            }
            try
            {
                string hash = FileHash.Sha256(fetched.TempPath);
                AtomicFile.Replace(fetched.TempPath, DllPath(arch));
                AtomicFile.TryDelete(PendingDllPath(arch));
                Dictionary<string, CacheEntryDto> file = ReadCacheFile(warnings);
                file[ArchName(arch)] = new CacheEntryDto
                {
                    Sha256 = hash,
                    DownloadedUtc = JsonFile.FormatUtc(utcNow()),
                    SourceUrl = SourceUrl(arch),
                };
                WriteCacheFile(file);
                return new DownloadOutcome(arch, DownloadStatus.Downloaded, "Downloaded OpenComposite " + ArchName(arch) + " (SHA-256 " + hash + ").");
            }
            catch (Exception e) when (IsDiskError(e))
            {
                return Failed(arch, "Could not store OpenComposite " + ArchName(arch) + ": " + e.Message);
            }
            finally
            {
                AtomicFile.TryDelete(fetched.TempPath);
            }
        }

        /// <summary>
        /// Null when the file looks like an OpenComposite DLL of that architecture, else the reason it does not.
        /// </summary>
        public static string Validate(string path, OpenCompositeArch arch)
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return "the file is missing.";
            }
            if (info.Length <= MinimumDllSize)
            {
                return "the file is too small (" + info.Length.ToString(CultureInfo.InvariantCulture) + " bytes) to be OpenComposite.";
            }
            PeMachine machine;
            try
            {
                machine = PeReader.ReadMachine(path);
            }
            catch (Exception e) when (IsDiskError(e))
            {
                return "the file could not be read (" + e.Message + ").";
            }
            if (machine == PeMachine.Unknown)
            {
                return "the file is not a Windows DLL (for example an error page from a proxy or a captive portal).";
            }
            if (machine != ExpectedMachine(arch))
            {
                return "the file is a " + DisplayText.Machine(machine) + " DLL, expected " + ArchName(arch) + ".";
            }
            return null;
        }

        /// <summary>
        /// Downloads to a new temporary file next to the cached DLL (same volume, so the later replace is a rename)
        /// and validates it. Returns the temporary path (kept on disk for the caller), or an error with no file
        /// left behind. Cleanup is a try/finally keyed on whether the fetch and validation actually succeeded,
        /// not on a list of exception types: whatever the downloader throws is either the caller's own
        /// cancellation (propagated as-is) or turned into a Failed outcome, and either way the temporary file
        /// never survives this method unless it is the one being handed back.
        /// </summary>
        private async Task<FetchResult> FetchValidatedAsync(OpenCompositeArch arch, CancellationToken cancellation)
        {
            string folder = ArchFolder(arch);
            string temp = Path.Combine(folder, DllName + ".download-" + Guid.NewGuid().ToString("N") + ".tmp");
            bool keepTemp = false;
            try
            {
                Directory.CreateDirectory(folder);
                foreach (string stale in Directory.GetFiles(folder, TempPattern))
                {
                    // Left behind by a run that was killed mid-download.
                    AtomicFile.TryDelete(stale);
                }
                try
                {
                    await http.DownloadToFileAsync(SourceUrl(arch), temp, cancellation).ConfigureAwait(false);
                }
                catch (Exception e) when (!(e is OperationCanceledException && cancellation.IsCancellationRequested))
                {
                    // Any failure that is not the caller's own cancellation (network error, timeout, a Content-
                    // Length mismatch, or anything else IHttpDownloader might throw) is reported as Failed; the
                    // caller's cancellation propagates unchanged instead (documented on DownloadAsync).
                    return new FetchResult(null, "Download of OpenComposite " + ArchName(arch) + " failed: " + e.Message);
                }

                string problem = Validate(temp, arch);
                if (problem != null)
                {
                    return new FetchResult(null, "The downloaded OpenComposite " + ArchName(arch) + " file was rejected: " + problem + " Nothing was changed.");
                }
                keepTemp = true;
                return new FetchResult(temp, null);
            }
            finally
            {
                if (!keepTemp)
                {
                    AtomicFile.TryDelete(temp);
                }
            }
        }

        private Dictionary<string, CacheEntryDto> ReadCacheFile(IList<string> warnings)
        {
            var entries = new Dictionary<string, CacheEntryDto>(StringComparer.OrdinalIgnoreCase);
            string path = CacheFilePath;
            if (!File.Exists(path))
            {
                return entries;
            }
            try
            {
                Dictionary<string, CacheEntryDto> file = JsonFile.Read<Dictionary<string, CacheEntryDto>>(path);
                if (file != null)
                {
                    foreach (KeyValuePair<string, CacheEntryDto> pair in file)
                    {
                        if (pair.Value != null && !string.IsNullOrEmpty(pair.Value.Sha256))
                        {
                            entries[pair.Key] = pair.Value;
                        }
                    }
                }
            }
            catch (Exception e) when (IsDiskError(e) || JsonFile.IsCorruptDataError(e))
            {
                warnings.Add("Could not read " + path + " (" + e.Message + "). The OpenComposite cache is treated as empty.");
            }
            return entries;
        }

        private void WriteCacheFile(Dictionary<string, CacheEntryDto> entries)
        {
            JsonFile.WriteAtomic(CacheFilePath, entries);
        }

        private static DownloadOutcome Failed(OpenCompositeArch arch, string message)
        {
            return new DownloadOutcome(arch, DownloadStatus.Failed, message);
        }

        private static bool IsDiskError(Exception e)
        {
            return e is IOException || e is UnauthorizedAccessException || e is SecurityException;
        }

        private sealed class FetchResult
        {
            public FetchResult(string tempPath, string error)
            {
                TempPath = tempPath;
                Error = error;
            }

            public string TempPath { get; }
            public string Error { get; }
        }

        [DataContract]
        private sealed class CacheEntryDto
        {
            [DataMember(Name = "sha256")]
            public string Sha256 { get; set; }

            [DataMember(Name = "downloadedUtc")]
            public string DownloadedUtc { get; set; }

            [DataMember(Name = "sourceUrl")]
            public string SourceUrl { get; set; }

            [DataMember(Name = "pendingSha256", EmitDefaultValue = false)]
            public string PendingSha256 { get; set; }

            [DataMember(Name = "pendingDownloadedUtc", EmitDefaultValue = false)]
            public string PendingDownloadedUtc { get; set; }
        }
    }
}
