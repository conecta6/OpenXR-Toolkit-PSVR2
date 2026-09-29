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

using System.Collections.Generic;

namespace GameManager.Core
{
    public sealed class GameEntry
    {
        public GameEntry(SteamGame game, GameClassification classification, CompatibilityVerdict compatibility)
        {
            Game = game;
            Classification = classification;
            Compatibility = compatibility;
        }

        public SteamGame Game { get; }
        public GameClassification Classification { get; }
        public CompatibilityVerdict Compatibility { get; }
    }

    public sealed class GameListResult
    {
        public GameListResult(string steamRoot, IReadOnlyList<GameEntry> entries, IReadOnlyList<string> warnings)
        {
            SteamRoot = steamRoot;
            Entries = entries;
            Warnings = warnings;
        }

        /// <summary>
        /// The Steam folder that was scanned, or null when Steam was not found.
        /// </summary>
        public string SteamRoot { get; }

        public IReadOnlyList<GameEntry> Entries { get; }
        public IReadOnlyList<string> Warnings { get; }
    }
}
