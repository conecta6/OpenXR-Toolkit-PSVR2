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
        /// left behind. Cleanup is a try/finally keyed on whether the fetch and validation actually succeeded.
        /// A failure at any stage — preparing the architecture folder, the download itself, or validation —
        /// becomes a Failed outcome, except the caller's own cancellation, which propagates unchanged (documented
        /// on DownloadAsync).
        /// </summary>
        private async Task<FetchResult> FetchValidatedAsync(OpenCompositeArch arch, CancellationToken cancellation)
        {
            string folder = ArchFolder(arch);
            string temp = Path.Combine(folder, DllName + ".download-" + Guid.NewGuid().ToString("N") + ".tmp");
            bool keepTemp = false;
            try
            {
                try
                {
                    Directory.CreateDirectory(folder);
                    foreach (string stale in Directory.GetFiles(folder, TempPattern))
                    {
                        // Left behind by a run that was killed mid-download.
                        AtomicFile.TryDelete(stale);
                    }
                }
                catch (Exception e) when (IsDiskError(e))
                {
                    return new FetchResult(null, "Download of OpenComposite " + ArchName(arch) + " failed: " + e.Message);
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

        /// <summary>
        /// R14: at startup, upstream is checked at most once per this interval.
        /// </summary>
        public static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(24);

        public static bool IsUpdateCheckDue(DateTime? lastCheckUtc, DateTime nowUtc)
        {
            if (lastCheckUtc == null)
            {
                return true;
            }
            // A last check "in the future" means the clock was moved back or the file was edited: check now,
            // rather than wait until that date.
            if (lastCheckUtc.Value > nowUtc)
            {
                return true;
            }
            return nowUtc - lastCheckUtc.Value >= UpdateCheckInterval;
        }

        /// <summary>
        /// The startup check: runs CheckForUpdatesAsync only when IsUpdateCheckDue; otherwise returns no outcome.
        /// </summary>
        public async Task<IReadOnlyList<DownloadOutcome>> CheckForUpdatesIfDueAsync(IList<string> warnings, CancellationToken cancellation)
        {
            AppSettings current = settings.Load(warnings);
            if (!IsUpdateCheckDue(current.LastUpdateCheckUtc, utcNow()))
            {
                return new DownloadOutcome[0];
            }
            return await CheckForUpdatesAsync(warnings, cancellation).ConfigureAwait(false);
        }

        /// <summary>
        /// Downloads the latest build of every cached architecture (never one that was not cached) to a temporary
        /// file and compares its SHA-256 with the cached file's. A different build waits as openvr_api.dll.new until
        /// AcceptPendingUpdate. The check time is recorded only when every architecture was checked without error,
        /// so a failed check is retried at the next startup.
        /// </summary>
        public async Task<IReadOnlyList<DownloadOutcome>> CheckForUpdatesAsync(IList<string> warnings, CancellationToken cancellation)
        {
            var outcomes = new List<DownloadOutcome>();
            if (!IsLicenseAccepted(warnings))
            {
                return outcomes;
            }
            IReadOnlyList<CachedBuild> builds = GetBuilds(warnings);
            foreach (CachedBuild build in builds)
            {
                outcomes.Add(await CheckOneAsync(build, warnings, cancellation).ConfigureAwait(false));
            }
            if (builds.Count > 0 && outcomes.TrueForAll(o => o.Status != DownloadStatus.Failed))
            {
                RecordCheckTime(warnings);
            }
            return outcomes;
        }

        /// <summary>
        /// Makes the waiting build the cached one. The .new file is checked again first (PE, architecture, and the
        /// hash recorded when it was downloaded); a damaged one is discarded. Games are updated by phase 6.
        /// </summary>
        public DownloadOutcome AcceptPendingUpdate(OpenCompositeArch arch, IList<string> warnings)
        {
            Dictionary<string, CacheEntryDto> file = ReadCacheFile(warnings);
            string pendingPath = PendingDllPath(arch);
            CacheEntryDto entry;
            if (!file.TryGetValue(ArchName(arch), out entry) || entry.PendingSha256 == null)
            {
                return Failed(arch, "No new OpenComposite " + ArchName(arch) + " build is waiting.");
            }
            if (!File.Exists(pendingPath))
            {
                // The .new file can be gone with cache.json still recording a pending update when an earlier
                // AcceptPendingUpdate call was interrupted right after AtomicFile.Replace promoted it (a crash,
                // or a disk error while saving the record) — the DLL is already the new build, but the file below
                // never learned that. Recognize that state by the cached DLL's own hash instead of reporting
                // "nothing waiting" and leaving the stale pending record (and a possibly wrong Sha256) behind.
                try
                {
                    if (string.Equals(FileHash.Sha256(DllPath(arch)), entry.PendingSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        entry.Sha256 = entry.PendingSha256;
                        entry.DownloadedUtc = entry.PendingDownloadedUtc;
                        entry.SourceUrl = SourceUrl(arch);
                        ClearPending(file, entry, arch);
                        return new DownloadOutcome(arch, DownloadStatus.UpdateAccepted, "Now using the new OpenComposite " + ArchName(arch) + " build.");
                    }
                }
                catch (Exception e) when (IsDiskError(e))
                {
                    return Failed(arch, "Could not switch to the new OpenComposite " + ArchName(arch) + " build: " + e.Message);
                }
                return Failed(arch, "No new OpenComposite " + ArchName(arch) + " build is waiting.");
            }
            try
            {
                string problem = Validate(pendingPath, arch);
                string actual = problem == null ? FileHash.Sha256(pendingPath) : null;
                if (problem != null || !string.Equals(actual, entry.PendingSha256, StringComparison.OrdinalIgnoreCase))
                {
                    ClearPending(file, entry, arch);
                    return Failed(arch, "The waiting OpenComposite " + ArchName(arch) + " build was damaged and was discarded. Check for an update again.");
                }
                AtomicFile.Replace(pendingPath, DllPath(arch));
                // The DLL is now the new build regardless of what happens next: a failure from here on must not
                // be reported as "could not switch", since it already did.
                try
                {
                    entry.Sha256 = actual;
                    entry.DownloadedUtc = entry.PendingDownloadedUtc;
                    entry.SourceUrl = SourceUrl(arch);
                    ClearPending(file, entry, arch);
                }
                catch (Exception e) when (IsDiskError(e))
                {
                    return Failed(arch, "Switched to the new OpenComposite " + ArchName(arch) + " build, but could not save the record (" + e.Message + "). This will be repaired the next time an update is accepted.");
                }
                return new DownloadOutcome(arch, DownloadStatus.UpdateAccepted, "Now using the new OpenComposite " + ArchName(arch) + " build.");
            }
            catch (Exception e) when (IsDiskError(e))
            {
                return Failed(arch, "Could not switch to the new OpenComposite " + ArchName(arch) + " build: " + e.Message);
            }
        }

        private async Task<DownloadOutcome> CheckOneAsync(CachedBuild build, IList<string> warnings, CancellationToken cancellation)
        {
            OpenCompositeArch arch = build.Arch;
            FetchResult fetched = await FetchValidatedAsync(arch, cancellation).ConfigureAwait(false);
            if (fetched.Error != null)
            {
                return Failed(arch, fetched.Error);
            }
            try
            {
                string latest = FileHash.Sha256(fetched.TempPath);
                // Compared with the file itself, not only cache.json, so a stale record can never hide an update.
                string current = FileHash.Sha256(build.DllPath);
                Dictionary<string, CacheEntryDto> file = ReadCacheFile(warnings);
                CacheEntryDto entry;
                if (!file.TryGetValue(ArchName(arch), out entry))
                {
                    // The record for this architecture disappeared from cache.json since GetBuilds read it
                    // (edited or deleted from under us). Recreating it here would write back only what
                    // ReadCacheFile sees right now, silently dropping any other architecture's record that
                    // still belongs in the same file.
                    return Failed(arch, "The OpenComposite " + ArchName(arch) + " cache record is missing; download it again.");
                }
                entry.Sha256 = current;

                if (string.Equals(latest, current, StringComparison.OrdinalIgnoreCase))
                {
                    // Upstream still serves the cached build; a waiting build it no longer serves is dropped.
                    ClearPending(file, entry, arch);
                    return new DownloadOutcome(arch, DownloadStatus.Unchanged, "OpenComposite " + ArchName(arch) + " is up to date.");
                }

                AtomicFile.Replace(fetched.TempPath, PendingDllPath(arch));
                entry.PendingSha256 = latest;
                entry.PendingDownloadedUtc = JsonFile.FormatUtc(utcNow());
                WriteCacheFile(file);
                return new DownloadOutcome(arch, DownloadStatus.UpdateFound, "A new OpenComposite " + ArchName(arch) + " build is available.");
            }
            catch (Exception e) when (IsDiskError(e))
            {
                return Failed(arch, "Could not store the new OpenComposite " + ArchName(arch) + " build: " + e.Message);
            }
            finally
            {
                AtomicFile.TryDelete(fetched.TempPath);
            }
        }

        private void RecordCheckTime(IList<string> warnings)
        {
            try
            {
                int warningsBeforeLoad = warnings.Count;
                AppSettings current = settings.Load(warnings);
                if (warnings.Count > warningsBeforeLoad)
                {
                    // Load already warned (an unreadable or corrupt settings.json gives defaults instead of
                    // throwing): saving now would persist those defaults and silently reset
                    // OpenCompositeLicenseAccepted. Leave the file alone; the next check retries.
                    return;
                }
                current.LastUpdateCheckUtc = utcNow();
                settings.Save(current);
            }
            catch (Exception e) when (IsDiskError(e))
            {
                warnings.Add("Could not save the OpenComposite check time: " + e.Message);
            }
        }

        /// <summary>
        /// Clears any waiting build for one architecture: deletes openvr_api.dll.new (a no-op if it is already
        /// gone, as after AcceptPendingUpdate promotes it), blanks the pending fields, and persists cache.json.
        /// The single place every "drop the pending update" path (an unchanged check, a damaged .new file, or a
        /// successful accept) goes through, per F5.
        /// </summary>
        private void ClearPending(Dictionary<string, CacheEntryDto> file, CacheEntryDto entry, OpenCompositeArch arch)
        {
            AtomicFile.TryDelete(PendingDllPath(arch));
            entry.PendingSha256 = null;
            entry.PendingDownloadedUtc = null;
            WriteCacheFile(file);
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
