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
    internal static class AtomicFile
    {
        /// <summary>
        /// Moves source over destination. When destination exists, File.Replace swaps it in one step, so a reader
        /// sees the old file or the new one, never a half-written one. Both files must be on the same volume:
        /// callers always create source next to destination.
        /// </summary>
        public static void Replace(string source, string destination)
        {
            if (File.Exists(destination))
            {
                try
                {
                    File.Replace(source, destination, null);
                }
                catch (Exception) when (!File.Exists(destination) && File.Exists(source))
                {
                    // T4-1: File.Replace can, in rare cases, delete destination without finishing the swap,
                    // leaving neither the old nor the new content in place. Recover by moving source (still
                    // intact) into destination's spot instead of losing both copies.
                    File.Move(source, destination);
                }
            }
            else
            {
                File.Move(source, destination);
            }
        }

        /// <summary>
        /// Deletes a file if it exists. Never throws for I/O or permission errors: used to clean up temporary files.
        /// </summary>
        public static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
