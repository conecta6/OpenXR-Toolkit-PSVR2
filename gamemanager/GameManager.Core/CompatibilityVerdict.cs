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
    /// A game's compatibility as the window shows it, and whether patch actions must stay disabled.
    /// </summary>
    public sealed class CompatibilityVerdict
    {
        private CompatibilityVerdict(
            CompatibilityStatus listStatus,
            bool blocked,
            string reason,
            string notes,
            IReadOnlyList<string> uncheckedFolders)
        {
            ListStatus = listStatus;
            Blocked = blocked;
            Reason = reason;
            Notes = notes;
            UncheckedFolders = uncheckedFolders;
        }

        /// <summary>
        /// The status in compatibility.json; Untested when the game is not listed.
        /// </summary>
        public CompatibilityStatus ListStatus { get; }

        /// <summary>
        /// True when the list says anti-cheat or anti-cheat files were found. A blocked game is never patched.
        /// </summary>
        public bool Blocked { get; }

        /// <summary>
        /// Why the game is blocked; "" when it is not.
        /// </summary>
        public string Reason { get; }

        /// <summary>
        /// Notes from compatibility.json; "" when there are none.
        /// </summary>
        public string Notes { get; }

        /// <summary>
        /// R31: folders the classification walk could not look inside (skipped links, unreadable folders).
        /// Empty when the whole install folder was checked.
        /// </summary>
        public IReadOnlyList<string> UncheckedFolders { get; }

        /// <summary>
        /// R29/R31: true when some folders were not checked, so anti-cheat files there would have been missed.
        /// The game is not blocked for this, but Patch asks first (default No).
        /// </summary>
        public bool AntiCheatNotRuledOut
        {
            get { return UncheckedFolders.Count > 0; }
        }

        public static CompatibilityVerdict For(
            CompatibilityEntry listEntry,
            IReadOnlyList<string> antiCheatMarkers,
            IReadOnlyList<string> uncheckedFolders)
        {
            CompatibilityStatus status = listEntry == null ? CompatibilityStatus.Untested : listEntry.Status;
            var reasons = new List<string>(2);
            if (status == CompatibilityStatus.AntiCheat)
            {
                reasons.Add("Listed as an anti-cheat game in compatibility.json.");
            }
            if (antiCheatMarkers != null && antiCheatMarkers.Count > 0)
            {
                reasons.Add("Anti-cheat files found: " + string.Join(", ", antiCheatMarkers) + ".");
            }
            string notes = listEntry == null ? "" : listEntry.Notes;
            var folders = uncheckedFolders == null ? new List<string>() : new List<string>(uncheckedFolders);
            return new CompatibilityVerdict(status, reasons.Count > 0, string.Join(" ", reasons), notes, folders);
        }
    }
}
