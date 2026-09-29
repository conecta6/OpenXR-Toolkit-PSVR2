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
    /// R23: the Status column.
    /// </summary>
    public enum PatchStatus
    {
        /// <summary>No patch record for this game.</summary>
        NotPatched,

        /// <summary>The DLL is the OpenComposite build Game Manager installed, and no newer build is cached.</summary>
        Patched,

        /// <summary>R24: the DLL is the game's original again (a Steam update put it back).</summary>
        UnpatchedByUpdate,

        /// <summary>R24: the DLL is the build Game Manager installed, and the accepted cached build differs.</summary>
        UpdateAvailable,

        /// <summary>R24: the DLL is missing, unreadable, or neither the original nor the installed build.</summary>
        ChangedExternally,
    }

    public static class PatchStatusRules
    {
        /// <summary>
        /// R24 for one record. currentSha256 is null when the DLL is missing. cachedSha256 is FileHash of the accepted
        /// cached OpenComposite DLL of the record's architecture (R34), or null when none is cached.
        /// </summary>
        public static PatchStatus Evaluate(PatchRecord record, string currentSha256, string cachedSha256)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }
            if (currentSha256 == null)
            {
                return PatchStatus.ChangedExternally;
            }
            if (Same(currentSha256, record.OriginalSha256))
            {
                return PatchStatus.UnpatchedByUpdate;
            }
            if (Same(currentSha256, record.OpenCompositeSha256))
            {
                return cachedSha256 != null && !Same(cachedSha256, record.OpenCompositeSha256)
                    ? PatchStatus.UpdateAvailable
                    : PatchStatus.Patched;
            }
            return PatchStatus.ChangedExternally;
        }

        /// <summary>
        /// A game's status from its DLLs' statuses: the one that most needs attention wins (Changed externally, then
        /// Unpatched by update, then Update available, then Patched). No records: Not patched.
        /// </summary>
        public static PatchStatus Combine(IEnumerable<PatchStatus> statuses)
        {
            PatchStatus result = PatchStatus.NotPatched;
            foreach (PatchStatus status in statuses)
            {
                if (Rank(status) > Rank(result))
                {
                    result = status;
                }
            }
            return result;
        }

        private static int Rank(PatchStatus status)
        {
            switch (status)
            {
                case PatchStatus.ChangedExternally:
                    return 4;
                case PatchStatus.UnpatchedByUpdate:
                    return 3;
                case PatchStatus.UpdateAvailable:
                    return 2;
                case PatchStatus.Patched:
                    return 1;
                default:
                    return 0;
            }
        }

        private static bool Same(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// One game's patch status for the window: the combined status, and per DLL the record, its status and a line
    /// for the tooltip.
    /// </summary>
    public sealed class GamePatchStatus
    {
        public GamePatchStatus(
            GameEntry entry,
            PatchStatus status,
            IReadOnlyList<PatchRecord> records,
            IReadOnlyList<PatchStatus> recordStatuses,
            IReadOnlyList<string> details)
        {
            Entry = entry ?? throw new ArgumentNullException(nameof(entry));
            Status = status;
            Records = records ?? new PatchRecord[0];
            RecordStatuses = recordStatuses ?? new PatchStatus[0];
            Details = details ?? new string[0];
        }

        public GameEntry Entry { get; }
        public PatchStatus Status { get; }
        public IReadOnlyList<PatchRecord> Records { get; }

        /// <summary>
        /// The status of each record, in the same order as Records.
        /// </summary>
        public IReadOnlyList<PatchStatus> RecordStatuses { get; }

        /// <summary>
        /// "Game_Data\Plugins\x86\openvr_api.dll: Patched", one per record.
        /// </summary>
        public IReadOnlyList<string> Details { get; }
    }

    /// <summary>
    /// R24/R25: the games each batch button acts on.
    /// </summary>
    public static class PatchBatch
    {
        /// <summary>
        /// "Re-patch all unpatched": games with at least one DLL a Steam update put back to the original.
        /// </summary>
        public static IReadOnlyList<GamePatchStatus> Unpatched(IReadOnlyList<GamePatchStatus> statuses)
        {
            return Having(statuses, PatchStatus.UnpatchedByUpdate);
        }

        /// <summary>
        /// "Update all": games with at least one DLL on an older build than the accepted cached one.
        /// </summary>
        public static IReadOnlyList<GamePatchStatus> Updatable(IReadOnlyList<GamePatchStatus> statuses)
        {
            return Having(statuses, PatchStatus.UpdateAvailable);
        }

        private static IReadOnlyList<GamePatchStatus> Having(IReadOnlyList<GamePatchStatus> statuses, PatchStatus wanted)
        {
            var result = new List<GamePatchStatus>();
            foreach (GamePatchStatus status in statuses)
            {
                foreach (PatchStatus recordStatus in status.RecordStatuses)
                {
                    if (recordStatus == wanted)
                    {
                        result.Add(status);
                        break;
                    }
                }
            }
            return result;
        }
    }
}
