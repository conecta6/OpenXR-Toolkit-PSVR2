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
using System.Threading;

namespace GameManager.Core
{
    /// <summary>
    /// Locates Steam, lists its games, classifies each one and applies the compatibility list.
    /// Synchronous: callers run it off the UI thread.
    /// </summary>
    public static class GameListBuilder
    {
        public static GameListResult Build(
            SteamLocator locator,
            CompatibilityList compatibility,
            IProgress<string> progress = null,
            CancellationToken cancellation = default)
        {
            if (locator == null)
            {
                throw new ArgumentNullException(nameof(locator));
            }
            string steamRoot = locator.FindSteamRoot();
            if (steamRoot == null)
            {
                return new GameListResult(null, new GameEntry[0], new string[0]);
            }
            return BuildFrom(steamRoot, compatibility, progress, cancellation);
        }

        /// <summary>
        /// Throws OperationCanceledException when cancellation is requested; it is checked before each game.
        /// </summary>
        public static GameListResult BuildFrom(
            string steamRoot,
            CompatibilityList compatibility,
            IProgress<string> progress = null,
            CancellationToken cancellation = default)
        {
            return BuildFrom(steamRoot, compatibility, progress, cancellation, GameClassifier.Classify);
        }

        /// <summary>
        /// Test seam: classify stands in for GameClassifier.Classify.
        /// </summary>
        internal static GameListResult BuildFrom(
            string steamRoot,
            CompatibilityList compatibility,
            IProgress<string> progress,
            CancellationToken cancellation,
            Func<string, GameClassification> classify)
        {
            if (compatibility == null)
            {
                throw new ArgumentNullException(nameof(compatibility));
            }
            cancellation.ThrowIfCancellationRequested();

            LibraryScanResult scan = SteamLibraryScanner.Scan(steamRoot);
            var warnings = new List<string>(scan.Warnings);
            var entries = new List<GameEntry>(scan.Games.Count);
            foreach (SteamGame game in scan.Games)
            {
                cancellation.ThrowIfCancellationRequested();
                progress?.Report(game.Name);
                GameClassification classification = ClassifyOne(game, classify, warnings);
                foreach (string warning in classification.Warnings)
                {
                    warnings.Add(game.Name + ": " + warning);
                }
                CompatibilityVerdict verdict = CompatibilityVerdict.For(
                    compatibility.Find(game.AppId), classification.AntiCheatMarkers, classification.UncheckedFolders);
                entries.Add(new GameEntry(game, classification, verdict));
            }
            return new GameListResult(steamRoot, entries, warnings);
        }

        private static GameClassification ClassifyOne(SteamGame game, Func<string, GameClassification> classify, List<string> warnings)
        {
            try
            {
                return classify(game.InstallDir);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                // R1: one odd game must never empty the whole list. It is listed as Unknown (so it can never be
                // patched) with a warning, and the scan goes on.
                warnings.Add(game.Name + ": could not be classified (" + e.GetType().Name + ": " + e.Message + ").");
                // The walk did not finish, so nothing under the install folder was ruled out (R31).
                return new GameClassification(GameKind.Unknown, new OpenVrDll[0], false, new string[0], new[] { game.InstallDir }, new string[0]);
            }
        }
    }
}
