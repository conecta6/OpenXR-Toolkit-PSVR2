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
using System.Globalization;
using System.Text;

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

        public static string Compatibility(CompatibilityVerdict verdict)
        {
            if (verdict.Blocked)
            {
                return "Anti-cheat — blocked";
            }
            return verdict.ListStatus switch
            {
                CompatibilityStatus.Works => "Works",
                CompatibilityStatus.Broken => "Broken",
                _ => "Untested",
            };
        }

        /// <summary>
        /// Tooltip text: why the game is blocked, then the "Anti-cheat not ruled out" note when some folders could
        /// not be checked (R31), then the list notes. "" when there is nothing to say.
        /// </summary>
        public static string CompatibilityDetails(CompatibilityVerdict verdict)
        {
            var parts = new List<string>(2);
            if (verdict.Reason.Length > 0)
            {
                parts.Add(verdict.Reason);
            }
            if (verdict.AntiCheatNotRuledOut)
            {
                parts.Add("Anti-cheat not ruled out: these folders could not be checked: " + string.Join(", ", verdict.UncheckedFolders) + ".");
            }
            if (verdict.Notes.Length > 0)
            {
                parts.Add("Notes: " + verdict.Notes);
            }
            return string.Join(" ", parts);
        }

        public const string OpenCompositeSourceUrl = "https://gitlab.com/znixian/OpenOVR";

        /// <summary>
        /// R13: shown before the first download. Kept in Core so a future overlay shows the same notice.
        /// </summary>
        public const string OpenCompositeLicenseNotice =
            "OpenComposite is free software written and published by its own authors, not by OpenXR Toolkit PSVR2. "
            + "It is licensed under the GNU General Public License version 3 or later (GPLv3).\r\n\r\n"
            + "Game Manager downloads the unmodified OpenComposite DLLs directly from znix.xyz, the official download "
            + "site. It does not bundle, mirror or change them.\r\n\r\n"
            + "Source code and license: " + OpenCompositeSourceUrl;

        /// <summary>
        /// "OpenComposite: x64 downloaded 2026-09-28 (new build available), x86 downloaded 2026-09-28".
        /// </summary>
        public static string OpenCompositeSummary(IReadOnlyList<CachedBuild> builds)
        {
            if (builds.Count == 0)
            {
                return "OpenComposite: not downloaded";
            }
            var parts = new List<string>(builds.Count);
            foreach (CachedBuild build in builds)
            {
                string date = build.DownloadedUtc.HasValue
                    ? build.DownloadedUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : "(date unknown)";
                string part = OpenCompositeCache.ArchName(build.Arch) + " downloaded " + date;
                if (build.HasPendingUpdate)
                {
                    part += " (new build available)";
                }
                parts.Add(part);
            }
            return "OpenComposite: " + string.Join(", ", parts);
        }

        /// <summary>
        /// "Patch Beat Saber (620980)", "Restore Beat Saber (620980)", "Update OpenComposite in Beat Saber (620980)".
        /// </summary>
        public static string OperationTitle(PatchPlan plan)
        {
            string verb = plan.Operation switch
            {
                PatchOperation.Restore => "Restore ",
                PatchOperation.Update => "Update OpenComposite in ",
                _ => "Patch ",
            };
            return verb + plan.Game.Name + " (" + plan.Game.AppId.ToString(CultureInfo.InvariantCulture) + ")";
        }

        /// <summary>
        /// R16: everything a plan would do, as shown in simulation mode: why it cannot run, the questions it asks,
        /// the numbered steps, and notes.
        /// </summary>
        public static string PlanSummary(PatchPlan plan)
        {
            var text = new StringBuilder(OperationTitle(plan));
            if (plan.Blockers.Count > 0)
            {
                AppendSection(text, "Not possible:", plan.Blockers, false);
            }
            if (plan.Confirmations.Count > 0)
            {
                AppendSection(text, "Asks first (default No):", plan.Confirmations, false);
            }
            var steps = new List<string>();
            foreach (PatchAction action in plan.AllActions)
            {
                steps.Add(action.Description);
            }
            if (steps.Count > 0)
            {
                AppendSection(text, "Steps:", steps, true);
            }
            else if (plan.CanRun && plan.HasWork)
            {
                AppendSection(text, "Steps:", new[] { "No file changes; only the patch records are updated." }, false);
            }
            else if (plan.CanRun)
            {
                text.Append(Environment.NewLine).Append(Environment.NewLine).Append("Nothing to do.");
            }
            if (plan.Notes.Count > 0)
            {
                AppendSection(text, "Notes:", plan.Notes, false);
            }
            return text.ToString();
        }

        private static void AppendSection(StringBuilder text, string title, IReadOnlyList<string> lines, bool numbered)
        {
            text.Append(Environment.NewLine).Append(Environment.NewLine).Append(title);
            for (int i = 0; i < lines.Count; i++)
            {
                text.Append(Environment.NewLine)
                    .Append(numbered ? (i + 1).ToString(CultureInfo.InvariantCulture) + ". " : "- ")
                    .Append(lines[i]);
            }
        }

        /// <summary>
        /// R23: the Status column.
        /// </summary>
        public static string StatusText(PatchStatus status)
        {
            return status switch
            {
                PatchStatus.Patched => "Patched",
                PatchStatus.UnpatchedByUpdate => "Unpatched by update",
                PatchStatus.UpdateAvailable => "Update available",
                PatchStatus.ChangedExternally => "Changed externally",
                _ => "Not patched",
            };
        }

        /// <summary>
        /// R24: the offer shown after a scan, or "" when there is nothing to offer.
        /// </summary>
        public static string PostUpdateSummary(IReadOnlyList<GamePatchStatus> statuses)
        {
            int unpatched = PatchBatch.Unpatched(statuses).Count;
            int updatable = PatchBatch.Updatable(statuses).Count;
            var parts = new List<string>(2);
            if (unpatched > 0)
            {
                parts.Add(Counted(unpatched, "game was", "games were") + " unpatched by a Steam update: click \"Re-patch all unpatched\".");
            }
            if (updatable > 0)
            {
                parts.Add(Counted(updatable, "game can", "games can") + " get the new OpenComposite build: click \"Update all\".");
            }
            return string.Join(" ", parts);
        }

        private static string Counted(int count, string one, string many)
        {
            return count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? one : many);
        }
    }
}
