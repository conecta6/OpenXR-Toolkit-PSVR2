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

namespace GameManager.Core
{
    /// <summary>
    /// Finds Steam libraries (libraryfolders.vdf, current and legacy formats) and the games installed in them
    /// (appmanifest_*.acf). Problems become warnings; a single bad file never stops the scan.
    /// </summary>
    public static class SteamLibraryScanner
    {
        public const int SteamVrAppId = 250820;
        public const int SteamworksRedistributablesAppId = 228980;

        public static LibraryScanResult Scan(string steamRoot)
        {
            if (steamRoot == null)
            {
                throw new ArgumentNullException(nameof(steamRoot));
            }

            var warnings = new List<string>();
            var games = new List<SteamGame>();
            foreach (string library in FindLibraries(steamRoot, warnings))
            {
                ScanLibrary(library, games, warnings);
            }
            games.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
            return new LibraryScanResult(games, warnings);
        }

        /// <summary>
        /// Existing library folders, normalized and without duplicates. The Steam folder always comes first.
        /// </summary>
        public static IReadOnlyList<string> FindLibraries(string steamRoot, IList<string> warnings)
        {
            var libraries = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddLibrary(steamRoot, libraries, seen, warnings);

            string listPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(listPath))
            {
                warnings.Add("Library list not found: " + listPath + ". Only the Steam folder was scanned.");
                return libraries;
            }

            VdfNode document;
            try
            {
                document = VdfReader.ParseFile(listPath);
            }
            catch (Exception e) when (e is VdfFormatException || e is IOException || e is UnauthorizedAccessException)
            {
                warnings.Add("Could not read " + listPath + " (" + e.Message + "). Only the Steam folder was scanned.");
                return libraries;
            }

            // "libraryfolders" (current) or "LibraryFolders" (legacy); Find ignores case.
            VdfNode list = document.Find("libraryfolders");
            if (list == null || !list.IsBlock)
            {
                warnings.Add(listPath + " has no \"libraryfolders\" block. Only the Steam folder was scanned.");
                return libraries;
            }

            foreach (VdfNode entry in list.Children)
            {
                // Libraries use numeric keys. Legacy files also hold "TimeNextStatsReport" and "ContentStatsID".
                if (!IsAllDigits(entry.Key))
                {
                    continue;
                }
                // Current format: "1" { "path" "..." }. Legacy format: "1" "...".
                string path = entry.IsBlock ? entry.GetString("path") : entry.Value;
                if (string.IsNullOrWhiteSpace(path))
                {
                    warnings.Add("Library entry \"" + entry.Key + "\" in " + listPath + " has no path.");
                    continue;
                }
                AddLibrary(path, libraries, seen, warnings);
            }
            return libraries;
        }

        private static void AddLibrary(string path, List<string> libraries, HashSet<string> seen, IList<string> warnings)
        {
            string normalized;
            try
            {
                normalized = PathUtil.NormalizeDirectory(path);
            }
            catch (Exception e) when (PathUtil.IsInvalidPathError(e))
            {
                warnings.Add("Invalid library path \"" + path + "\": " + e.Message);
                return;
            }

            if (!seen.Add(normalized))
            {
                return;
            }
            if (!Directory.Exists(normalized))
            {
                warnings.Add("Library folder not found: " + normalized);
                return;
            }
            libraries.Add(normalized);
        }

        private static void ScanLibrary(string library, List<SteamGame> games, List<string> warnings)
        {
            string steamapps = Path.Combine(library, "steamapps");
            string[] manifests;
            try
            {
                manifests = Directory.GetFiles(steamapps, "appmanifest_*.acf");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                warnings.Add("Could not list " + steamapps + ": " + e.Message);
                return;
            }
            Array.Sort(manifests, StringComparer.OrdinalIgnoreCase);

            foreach (string manifest in manifests)
            {
                // On Windows, "*.acf" also matches longer extensions such as ".acf_old".
                if (!manifest.EndsWith(".acf", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                SteamGame game = ReadManifest(manifest, library, warnings);
                if (game != null)
                {
                    games.Add(game);
                }
            }
        }

        private static SteamGame ReadManifest(string manifestPath, string library, List<string> warnings)
        {
            VdfNode document;
            try
            {
                document = VdfReader.ParseFile(manifestPath);
            }
            catch (Exception e) when (e is VdfFormatException || e is IOException || e is UnauthorizedAccessException)
            {
                warnings.Add("Skipped " + manifestPath + ": " + e.Message);
                return null;
            }

            VdfNode state = document.Find("AppState");
            if (state == null || !state.IsBlock)
            {
                warnings.Add("Skipped " + manifestPath + ": no \"AppState\" block.");
                return null;
            }

            int appId;
            if (!int.TryParse(state.GetString("appid"), NumberStyles.None, CultureInfo.InvariantCulture, out appId) || appId <= 0)
            {
                warnings.Add("Skipped " + manifestPath + ": missing or invalid appid.");
                return null;
            }
            if (appId == SteamVrAppId || appId == SteamworksRedistributablesAppId)
            {
                return null;
            }

            string name = state.GetString("name");
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "App " + appId.ToString(CultureInfo.InvariantCulture);
            }

            string installDir = state.GetString("installdir");
            if (string.IsNullOrWhiteSpace(installDir))
            {
                warnings.Add("Skipped " + name + " (" + appId + "): no installdir in " + manifestPath + ".");
                return null;
            }

            string commonDir = Path.Combine(library, "steamapps", "common");
            string gameDir;
            try
            {
                gameDir = PathUtil.NormalizeDirectory(Path.Combine(commonDir, installDir));
            }
            catch (Exception e) when (PathUtil.IsInvalidPathError(e))
            {
                warnings.Add("Skipped " + name + " (" + appId + "): invalid installdir \"" + installDir + "\": " + e.Message);
                return null;
            }

            // A corrupt or hostile installdir ("..\..\Windows", "C:\") must not send the walk outside the library.
            if (!gameDir.StartsWith(commonDir + "\\", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("Skipped " + name + " (" + appId + "): install folder \"" + installDir + "\" is outside " + commonDir + ".");
                return null;
            }
            if (!Directory.Exists(gameDir))
            {
                warnings.Add("Skipped " + name + " (" + appId + "): install folder not found: " + gameDir);
                return null;
            }
            return new SteamGame(appId, name, gameDir, library);
        }

        private static bool IsAllDigits(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            foreach (char c in text)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }
            return true;
        }
    }
}
