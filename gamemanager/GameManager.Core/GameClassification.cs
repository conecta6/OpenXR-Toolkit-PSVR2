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
    public enum GameKind
    {
        /// <summary>Neither openvr_api.dll nor openxr_loader.dll was found.</summary>
        Unknown,

        /// <summary>At least one openvr_api.dll anywhere under the install folder.</summary>
        OpenVr,

        /// <summary>No openvr_api.dll, but an openxr_loader.dll.</summary>
        OpenXrProbable,
    }

    public sealed class OpenVrDll
    {
        public OpenVrDll(string relativePath, PeMachine machine)
        {
            RelativePath = relativePath;
            Machine = machine;
        }

        /// <summary>
        /// Path relative to the install folder, for example "Beat Saber_Data\Plugins\x86_64\openvr_api.dll".
        /// </summary>
        public string RelativePath { get; }

        public PeMachine Machine { get; }
    }

    public sealed class GameClassification
    {
        public GameClassification(
            GameKind kind,
            IReadOnlyList<OpenVrDll> openVrDlls,
            bool hasOpenXrLoader,
            IReadOnlyList<string> antiCheatMarkers,
            IReadOnlyList<string> warnings)
        {
            Kind = kind;
            OpenVrDlls = openVrDlls;
            HasOpenXrLoader = hasOpenXrLoader;
            AntiCheatMarkers = antiCheatMarkers;
            Warnings = warnings;
        }

        public GameKind Kind { get; }
        public IReadOnlyList<OpenVrDll> OpenVrDlls { get; }
        public bool HasOpenXrLoader { get; }

        /// <summary>
        /// Anti-cheat files and folders found, relative to the install folder, for example "EasyAntiCheat".
        /// Empty when there is none. Any entry blocks the game.
        /// </summary>
        public IReadOnlyList<string> AntiCheatMarkers { get; }

        public IReadOnlyList<string> Warnings { get; }
    }
}
