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

namespace GameManager.Core
{
    /// <summary>
    /// R20: what Game Manager did to one openvr_api.dll. Keyed by DllPath, never by AppId (R4): one game can have
    /// several DLLs (Unity ships x86 and x64), and an AppId can appear in more than one library.
    /// </summary>
    public sealed class PatchRecord
    {
        public PatchRecord(
            int appId,
            string gameName,
            string installDir,
            string dllPath,
            OpenCompositeArch arch,
            string originalSha256,
            string openCompositeSha256,
            bool iniCreated,
            DateTime patchedUtc)
        {
            if (!PathUtil.IsFullyQualified(dllPath))
            {
                throw new ArgumentException("The DLL path is not absolute: \"" + dllPath + "\".", nameof(dllPath));
            }
            try
            {
                PatchState.KeyOf(dllPath);
            }
            catch (Exception e) when (PathUtil.IsInvalidPathError(e))
            {
                throw new ArgumentException("The DLL path cannot be normalized: \"" + dllPath + "\" (" + e.Message + ").", nameof(dllPath), e);
            }
            if (!PatchState.IsSha256(originalSha256))
            {
                throw new ArgumentException("Not a SHA-256: \"" + originalSha256 + "\".", nameof(originalSha256));
            }
            if (!PatchState.IsSha256(openCompositeSha256))
            {
                throw new ArgumentException("Not a SHA-256: \"" + openCompositeSha256 + "\".", nameof(openCompositeSha256));
            }
            AppId = appId;
            GameName = gameName ?? "";
            InstallDir = installDir ?? "";
            DllPath = dllPath;
            Arch = arch;
            OriginalSha256 = originalSha256.ToLowerInvariant();
            OpenCompositeSha256 = openCompositeSha256.ToLowerInvariant();
            IniCreated = iniCreated;
            PatchedUtc = patchedUtc;
        }

        public int AppId { get; }
        public string GameName { get; }
        public string InstallDir { get; }
        public string DllPath { get; }
        public OpenCompositeArch Arch { get; }

        /// <summary>
        /// SHA-256 of the game's own DLL, kept as openvr_api.dll.bak.
        /// </summary>
        public string OriginalSha256 { get; }

        /// <summary>
        /// SHA-256 of the OpenComposite DLL written into the game folder, computed from that file (R30).
        /// </summary>
        public string OpenCompositeSha256 { get; }

        /// <summary>
        /// True when Game Manager created the opencomposite.ini next to this DLL (only then does Restore delete it).
        /// </summary>
        public bool IniCreated { get; }

        public DateTime PatchedUtc { get; }

        /// <summary>
        /// A copy with a new OpenComposite hash and time; the hash is always FileHash of the DLL actually written (R30).
        /// </summary>
        public PatchRecord WithOpenComposite(string openCompositeSha256, DateTime patchedUtc)
        {
            return new PatchRecord(AppId, GameName, InstallDir, DllPath, Arch, OriginalSha256, openCompositeSha256, IniCreated, patchedUtc);
        }
    }

    /// <summary>
    /// The records loaded from state.json, one per DLL path (case-insensitive, slashes normalized).
    /// </summary>
    public sealed class PatchState
    {
        private readonly Dictionary<string, PatchRecord> records = new Dictionary<string, PatchRecord>(StringComparer.OrdinalIgnoreCase);

        internal PatchState(bool canSave)
        {
            CanSave = canSave;
        }

        /// <summary>
        /// False when state.json exists but could not be read (for example locked by another program): saving would
        /// replace records that were never loaded, so PatchStateStore.Save refuses and no operation runs.
        /// </summary>
        public bool CanSave { get; internal set; }

        /// <summary>
        /// Sorted by DLL path.
        /// </summary>
        public IReadOnlyList<PatchRecord> Records
        {
            get
            {
                var list = new List<PatchRecord>(records.Values);
                list.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.DllPath, b.DllPath));
                return list;
            }
        }

        /// <summary>
        /// The record for this DLL, or null.
        /// </summary>
        public PatchRecord Find(string dllPath)
        {
            PatchRecord record;
            return records.TryGetValue(KeyOf(dllPath), out record) ? record : null;
        }

        /// <summary>
        /// Adds the record, replacing any record for the same DLL.
        /// </summary>
        public void Put(PatchRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }
            records[KeyOf(record.DllPath)] = record;
        }

        public bool Remove(string dllPath)
        {
            return records.Remove(KeyOf(dllPath));
        }

        /// <summary>
        /// The same key for "C:\Games\X\openvr_api.dll", "c:/games/x/OPENVR_API.DLL" and "\\?\C:\Games\X\openvr_api.dll"
        /// (the dictionary ignores case; the "\\?\" device prefix is dropped).
        /// </summary>
        public static string KeyOf(string dllPath)
        {
            return Path.GetFullPath(PathUtil.StripDevicePrefix(dllPath.Replace('/', '\\')));
        }

        /// <summary>
        /// 64 hexadecimal digits.
        /// </summary>
        public static bool IsSha256(string text)
        {
            if (text == null || text.Length != 64)
            {
                return false;
            }
            foreach (char c in text)
            {
                if (!Uri.IsHexDigit(c))
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// R20: state.json, { "version": 1, "records": [ { appId, gameName, installDir, dllPath, arch, originalSha256,
    /// openCompositeSha256, iniCreated, patchedUtc } ] }, written atomically. Load may rename an unusable file inside the app-data folder
    /// to state.json.corrupt-&lt;UTC time&gt; and start with no records; a file that cannot be read is never overwritten.
    /// </summary>
    public sealed class PatchStateStore
    {
        public const int SupportedVersion = 1;

        private readonly Func<DateTime> utcNow;

        public PatchStateStore(string path, Func<DateTime> utcNow)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path is empty.", nameof(path));
            }
            FilePath = path;
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public string FilePath { get; }

        public PatchState Load(IList<string> warnings)
        {
            if (!File.Exists(FilePath))
            {
                return new PatchState(true);
            }
            StateDto dto;
            try
            {
                dto = JsonFile.Read<StateDto>(FilePath);
            }
            catch (Exception e) when (JsonFile.IsCorruptDataError(e))
            {
                return SetAsideAndStartEmpty("it is not valid JSON: " + e.Message, warnings);
            }
            catch (Exception e) when (OpenCompositeCache.IsDiskError(e))
            {
                warnings.Add("Could not read the patch records in " + FilePath + " (" + e.Message + "). Patch, Restore and Update will not run until it can be read, so nothing overwrites it.");
                return new PatchState(false);
            }
            if (dto == null)
            {
                return SetAsideAndStartEmpty("it holds no records object", warnings);
            }
            if (dto.Version != SupportedVersion)
            {
                return SetAsideAndStartEmpty(
                    "its version is " + dto.Version.ToString(CultureInfo.InvariantCulture) + ", expected " + SupportedVersion.ToString(CultureInfo.InvariantCulture),
                    warnings);
            }

            var state = new PatchState(true);
            string skippedCopy = null;
            if (dto.Records != null)
            {
                foreach (RecordDto item in dto.Records)
                {
                    PatchRecord record = ToRecord(item);
                    if (record == null)
                    {
                        string skipped = item == null ? "null" : item.DllPath ?? "no dllPath";
                        if (skippedCopy == null && !state.CanSave)
                        {
                            warnings.Add("Skipped an invalid patch record in " + FilePath + " (" + skipped + ").");
                            continue;
                        }
                        if (skippedCopy == null)
                        {
                            // The next save drops the skipped record: keep the file as it was first.
                            try
                            {
                                skippedCopy = CopyAside(".skipped-");
                            }
                            catch (Exception e) when (OpenCompositeCache.IsDiskError(e))
                            {
                                warnings.Add("Skipped an invalid patch record in " + FilePath + " (" + skipped + ") and could not keep a copy of the file (" + e.Message + "). The valid records are shown, but Patch, Restore and Update will not run, so nothing overwrites the file.");
                                state.CanSave = false;
                                continue;
                            }
                        }
                        warnings.Add("Skipped an invalid patch record in " + FilePath + " (" + skipped + "). It will be dropped at the next save; the file as it was is kept as " + skippedCopy + ".");
                        continue;
                    }
                    if (state.Find(record.DllPath) != null)
                    {
                        warnings.Add("Two patch records for " + record.DllPath + " in " + FilePath + "; the last one is used.");
                    }
                    state.Put(record);
                }
            }
            return state;
        }

        /// <summary>
        /// Atomic write (temporary file, then replace). Throws InvalidOperationException when the state could not be
        /// loaded (CanSave is false); I/O and permission errors pass through.
        /// </summary>
        public void Save(PatchState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            if (!state.CanSave)
            {
                throw new InvalidOperationException("The patch records in " + FilePath + " could not be read, so they are not overwritten.");
            }
            var dto = new StateDto { Version = SupportedVersion, Records = new List<RecordDto>() };
            foreach (PatchRecord record in state.Records)
            {
                dto.Records.Add(new RecordDto
                {
                    AppId = record.AppId,
                    GameName = record.GameName,
                    InstallDir = record.InstallDir,
                    DllPath = record.DllPath,
                    Arch = OpenCompositeCache.ArchName(record.Arch),
                    OriginalSha256 = record.OriginalSha256,
                    OpenCompositeSha256 = record.OpenCompositeSha256,
                    IniCreated = record.IniCreated,
                    PatchedUtc = JsonFile.FormatUtc(record.PatchedUtc),
                });
            }
            JsonFile.WriteAtomic(FilePath, dto);
        }

        /// <summary>
        /// Copies state.json to state.json&lt;marker&gt;&lt;UTC time&gt; (plus a GUID suffix when that name exists) and returns the copy's path.
        /// </summary>
        private string CopyAside(string marker)
        {
            string copy = FilePath + marker + utcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            if (File.Exists(copy))
            {
                copy += "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            }
            File.Copy(FilePath, copy, false);
            return copy;
        }

        private PatchState SetAsideAndStartEmpty(string reason, IList<string> warnings)
        {
            string aside = FilePath + ".corrupt-" + utcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            if (File.Exists(aside))
            {
                aside += "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            }
            try
            {
                AtomicFile.Rename(FilePath, aside);
            }
            catch (Exception e) when (OpenCompositeCache.IsDiskError(e))
            {
                warnings.Add("The patch records in " + FilePath + " are unusable (" + reason + ") and could not be set aside (" + e.Message + "). Nothing will overwrite the file.");
                return new PatchState(false);
            }
            warnings.Add("The patch records in " + FilePath + " were unusable (" + reason + "). The file was kept as " + aside
                + " and Game Manager starts with no records: games patched before show as \"Not patched\" until you patch them again (their backups are reused).");
            return new PatchState(true);
        }

        /// <summary>
        /// Null when the fields needed to act safely (path, architecture, hashes) are missing or invalid. Other
        /// fields are lenient: a hand-edited time or name must not lose the record that makes Restore possible.
        /// </summary>
        private static PatchRecord ToRecord(RecordDto item)
        {
            if (item == null)
            {
                return null;
            }
            OpenCompositeArch arch;
            if (string.Equals(item.Arch, "x64", StringComparison.OrdinalIgnoreCase))
            {
                arch = OpenCompositeArch.X64;
            }
            else if (string.Equals(item.Arch, "x86", StringComparison.OrdinalIgnoreCase))
            {
                arch = OpenCompositeArch.X86;
            }
            else
            {
                return null;
            }
            try
            {
                return new PatchRecord(
                    item.AppId,
                    item.GameName,
                    item.InstallDir,
                    item.DllPath,
                    arch,
                    item.OriginalSha256,
                    item.OpenCompositeSha256,
                    item.IniCreated,
                    JsonFile.ParseUtc(item.PatchedUtc) ?? DateTime.MinValue);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        [DataContract]
        private sealed class StateDto
        {
            [DataMember(Name = "version", Order = 1)]
            public int Version { get; set; }

            [DataMember(Name = "records", Order = 2)]
            public List<RecordDto> Records { get; set; }
        }

        [DataContract]
        private sealed class RecordDto
        {
            [DataMember(Name = "appId", Order = 1)]
            public int AppId { get; set; }

            [DataMember(Name = "gameName", Order = 2)]
            public string GameName { get; set; }

            [DataMember(Name = "installDir", Order = 3)]
            public string InstallDir { get; set; }

            [DataMember(Name = "dllPath", Order = 4)]
            public string DllPath { get; set; }

            [DataMember(Name = "arch", Order = 5)]
            public string Arch { get; set; }

            [DataMember(Name = "originalSha256", Order = 6)]
            public string OriginalSha256 { get; set; }

            [DataMember(Name = "openCompositeSha256", Order = 7)]
            public string OpenCompositeSha256 { get; set; }

            [DataMember(Name = "iniCreated", Order = 8)]
            public bool IniCreated { get; set; }

            [DataMember(Name = "patchedUtc", Order = 9)]
            public string PatchedUtc { get; set; }
        }
    }
}
