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
    /// User-facing text shared by every front end (the window now, a SteamVR overlay later).
    /// </summary>
    public static class DisplayText
    {
        public static string Kind(GameKind kind)
        {
            return kind switch
            {
                GameKind.OpenVr => "OpenVR",
                GameKind.OpenXrProbable => "OpenXR (probable)",
                _ => "Unknown",
            };
        }

        public static string Machine(PeMachine machine)
        {
            return machine switch
            {
                PeMachine.X86 => "x86",
                PeMachine.X64 => "x64",
                PeMachine.Arm64 => "ARM64",
                _ => "unknown",
            };
        }

        /// <summary>
        /// "x86: Beat Saber_Data\Plugins\x86\openvr_api.dll; x64: ..." or "" when there is none.
        /// </summary>
        public static string OpenVrDlls(IReadOnlyList<OpenVrDll> dlls)
        {
            var parts = new List<string>(dlls.Count);
            foreach (OpenVrDll dll in dlls)
            {
                parts.Add(Machine(dll.Machine) + ": " + dll.RelativePath);
            }
            return string.Join("; ", parts);
        }
    }
}
