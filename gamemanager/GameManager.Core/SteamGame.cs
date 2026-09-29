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
    /// <summary>
    /// An installed Steam app, read from its appmanifest_*.acf.
    /// </summary>
    public sealed class SteamGame
    {
        public SteamGame(int appId, string name, string installDir, string libraryPath)
        {
            AppId = appId;
            Name = name;
            InstallDir = installDir;
            LibraryPath = libraryPath;
        }

        public int AppId { get; }
        public string Name { get; }

        /// <summary>
        /// Full path of the game folder: {library}\steamapps\common\{installdir}.
        /// </summary>
        public string InstallDir { get; }

        public string LibraryPath { get; }
    }

    public sealed class LibraryScanResult
    {
        public LibraryScanResult(IReadOnlyList<SteamGame> games, IReadOnlyList<string> warnings)
        {
            Games = games;
            Warnings = warnings;
        }

        public IReadOnlyList<SteamGame> Games { get; }
        public IReadOnlyList<string> Warnings { get; }
    }
}
