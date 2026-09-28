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
using System.Security;

namespace GameManager.Core
{
    public static class PathUtil
    {
        /// <summary>
        /// Absolute path with backslashes and no trailing separator, except a drive root ("E:\").
        /// Throws ArgumentException for an empty path; Path.GetFullPath exceptions pass through.
        /// </summary>
        public static string NormalizeDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path is empty.", nameof(path));
            }
            string full = Path.GetFullPath(path.Trim().Replace('/', '\\'));
            string trimmed = full.TrimEnd('\\');
            if (trimmed.Length == 2 && trimmed[1] == ':')
            {
                return trimmed + "\\";
            }
            return trimmed.Length == 0 ? full : trimmed;
        }

        /// <summary>
        /// True for the exceptions Path.GetFullPath throws on a malformed or unusable path.
        /// </summary>
        public static bool IsInvalidPathError(Exception e)
        {
            return e is ArgumentException
                || e is NotSupportedException
                || e is PathTooLongException
                || e is SecurityException;
        }
    }
}
