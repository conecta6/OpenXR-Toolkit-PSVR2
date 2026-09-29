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
    /// R35: the warnings shown in the window, each at most once however many operations report it, in the order
    /// first seen. Transient notices ("another operation is running") never go here; they go to the status line.
    /// </summary>
    public sealed class WarningLog
    {
        private readonly List<string> items = new List<string>();
        private readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyList<string> Items
        {
            get { return items; }
        }

        /// <summary>
        /// True when the warning was new and was added; false for a blank line or one already in the log.
        /// </summary>
        public bool Add(string warning)
        {
            if (string.IsNullOrWhiteSpace(warning) || !seen.Add(warning))
            {
                return false;
            }
            items.Add(warning);
            return true;
        }
    }
}
