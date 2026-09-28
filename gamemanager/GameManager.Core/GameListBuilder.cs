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

namespace GameManager.Core
{
    /// <summary>
    /// Locates Steam, lists its games and classifies each one. Synchronous: callers run it off the UI thread.
    /// </summary>
    public static class GameListBuilder
    {
        public static GameListResult Build(SteamLocator locator, IProgress<string> progress = null)
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
            return BuildFrom(steamRoot, progress);
        }

        public static GameListResult BuildFrom(string steamRoot, IProgress<string> progress = null)
        {
            LibraryScanResult scan = SteamLibraryScanner.Scan(steamRoot);
            var warnings = new List<string>(scan.Warnings);
            var entries = new List<GameEntry>(scan.Games.Count);
            foreach (SteamGame game in scan.Games)
            {
                progress?.Report(game.Name);
                GameClassification classification = GameClassifier.Classify(game.InstallDir);
                foreach (string warning in classification.Warnings)
                {
                    warnings.Add(game.Name + ": " + warning);
                }
                entries.Add(new GameEntry(game, classification));
            }
            return new GameListResult(steamRoot, entries, warnings);
        }
    }
}
