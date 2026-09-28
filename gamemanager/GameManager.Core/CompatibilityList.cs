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

namespace GameManager.Core
{
    public enum CompatibilityStatus
    {
        /// <summary>Not in compatibility.json.</summary>
        Untested,

        Works,

        Broken,

        /// <summary>Ships anti-cheat: the game is never patched.</summary>
        AntiCheat,
    }

    public sealed class CompatibilityEntry
    {
        public CompatibilityEntry(int appId, string name, CompatibilityStatus status, string notes)
        {
            AppId = appId;
            Name = name;
            Status = status;
            Notes = notes;
        }

        public int AppId { get; }
        public string Name { get; }
        public CompatibilityStatus Status { get; }

        /// <summary>
        /// Free text, "" when there is none.
        /// </summary>
        public string Notes { get; }
    }

    /// <summary>
    /// The shipped compatibility list (compatibility.json next to the exe). It is a hint: a missing or corrupt
    /// file gives an empty list plus a warning, and anti-cheat file detection still applies.
    /// </summary>
    public sealed class CompatibilityList
    {
        public const string FileName = "compatibility.json";
        public const int SupportedVersion = 1;
        private const string Fallback = "Every game is shown as untested; anti-cheat files are still detected.";

        private readonly Dictionary<int, CompatibilityEntry> entries;

        private CompatibilityList(Dictionary<int, CompatibilityEntry> entries)
        {
            this.entries = entries;
        }

        public static CompatibilityList Empty { get; } = new CompatibilityList(new Dictionary<int, CompatibilityEntry>());

        public int Count
        {
            get { return entries.Count; }
        }

        /// <summary>
        /// The entry for an AppID, or null when the game is not listed.
        /// </summary>
        public CompatibilityEntry Find(int appId)
        {
            CompatibilityEntry entry;
            return entries.TryGetValue(appId, out entry) ? entry : null;
        }

        public static CompatibilityList Load(string path, IList<string> warnings)
        {
            if (!File.Exists(path))
            {
                warnings.Add("Compatibility list not found: " + path + ". " + Fallback);
                return Empty;
            }
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is SecurityException)
            {
                warnings.Add("Could not read " + path + ": " + e.Message + " " + Fallback);
                return Empty;
            }
            return Parse(text, path, warnings);
        }

        /// <summary>
        /// Parses compatibility.json text; source names the file in warnings. Bad entries are skipped one by one.
        /// Text that is not valid JSON, or not "version": 1, gives an empty list.
        /// </summary>
        public static CompatibilityList Parse(string json, string source, IList<string> warnings)
        {
            CompatibilityFileDto file;
            try
            {
                file = JsonFile.Parse<CompatibilityFileDto>(json);
            }
            catch (Exception e) when (JsonFile.IsCorruptDataError(e))
            {
                warnings.Add(source + " is not valid JSON (" + e.Message + "). " + Fallback);
                return Empty;
            }
            if (file == null || file.Version != SupportedVersion)
            {
                warnings.Add(source + " is not a compatibility list this version understands (expected \"version\": 1). " + Fallback);
                return Empty;
            }

            var entries = new Dictionary<int, CompatibilityEntry>();
            if (file.Games == null)
            {
                return new CompatibilityList(entries);
            }
            for (int i = 0; i < file.Games.Count; i++)
            {
                CompatibilityGameDto game = file.Games[i];
                string label = "entry " + (i + 1).ToString(CultureInfo.InvariantCulture);
                if (game == null)
                {
                    warnings.Add(source + ": " + label + " is empty and was ignored.");
                    continue;
                }
                label += " (" + (game.Name ?? "no name") + ")";
                if (game.AppId <= 0)
                {
                    warnings.Add(source + ": " + label + " has no valid appId and was ignored.");
                    continue;
                }
                CompatibilityStatus status;
                if (!TryParseStatus(game.Status, out status))
                {
                    warnings.Add(source + ": " + label + " has unknown status \"" + game.Status + "\" and was ignored.");
                    continue;
                }

                var entry = new CompatibilityEntry(game.AppId, game.Name ?? "", status, game.Notes ?? "");
                CompatibilityEntry existing;
                if (entries.TryGetValue(game.AppId, out existing))
                {
                    warnings.Add(source + ": appId " + game.AppId.ToString(CultureInfo.InvariantCulture) + " is listed more than once.");
                    // The safe answer wins: once any entry says anti-cheat, the game stays blocked.
                    if (existing.Status == CompatibilityStatus.AntiCheat || status != CompatibilityStatus.AntiCheat)
                    {
                        continue;
                    }
                }
                entries[game.AppId] = entry;
            }
            return new CompatibilityList(entries);
        }

        private static bool TryParseStatus(string text, out CompatibilityStatus status)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "works":
                    status = CompatibilityStatus.Works;
                    return true;
                case "broken":
                    status = CompatibilityStatus.Broken;
                    return true;
                case "anticheat":
                    status = CompatibilityStatus.AntiCheat;
                    return true;
                default:
                    status = CompatibilityStatus.Untested;
                    return false;
            }
        }

        [DataContract]
        private sealed class CompatibilityFileDto
        {
            [DataMember(Name = "version")]
            public int Version { get; set; }

            [DataMember(Name = "games")]
            public List<CompatibilityGameDto> Games { get; set; }
        }

        [DataContract]
        private sealed class CompatibilityGameDto
        {
            [DataMember(Name = "appId")]
            public int AppId { get; set; }

            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "status")]
            public string Status { get; set; }

            [DataMember(Name = "notes")]
            public string Notes { get; set; }
        }
    }
}
