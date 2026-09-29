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
using System.IO;

namespace GameManager.Core
{
    /// <summary>
    /// Recognizes anti-cheat files and folders by name (R8). GameClassifier calls it for every entry of its single
    /// folder walk, so detecting anti-cheat costs no second pass over the game (R2).
    /// </summary>
    public static class AntiCheatDetector
    {
        public static bool IsMarker(string name, bool isDirectory)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            if (isDirectory)
            {
                return Is(name, "EasyAntiCheat") || Is(name, "EasyAntiCheat_EOS") || Is(name, "BattlEye");
            }

            string extension = Path.GetExtension(name);
            if (Is(name, "start_protected_game.exe"))
            {
                return true;
            }
            if (StartsWith(name, "EasyAntiCheat"))
            {
                return Is(extension, ".exe") || Is(extension, ".sys") || Is(extension, ".dll");
            }
            if (StartsWith(name, "BEService"))
            {
                return Is(extension, ".exe");
            }
            if (StartsWith(name, "BEClient"))
            {
                return Is(extension, ".dll");
            }
            return false;
        }

        private static bool Is(string text, string expected)
        {
            return string.Equals(text, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool StartsWith(string text, string prefix)
        {
            return text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
