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

namespace GameManager.Core
{
    /// <summary>
    /// R23: when Patch and Restore are enabled. In Core, so every front end applies the same rules.
    /// </summary>
    public static class PatchAvailability
    {
        /// <summary>
        /// An OpenVR game that is not blocked. "Anti-cheat not ruled out" still allows it: Patch then asks first.
        /// </summary>
        public static bool CanPatch(GameEntry entry)
        {
            return entry != null && !entry.Compatibility.Blocked && entry.Classification.Kind == GameKind.OpenVr;
        }

        /// <summary>
        /// Any game Game Manager has a patch record for (a blocked one too, R36), because restoring removes
        /// OpenComposite and so lowers the anti-cheat risk instead of raising it.
        /// </summary>
        public static bool CanRestore(GamePatchStatus status)
        {
            return status != null && status.Records.Count > 0;
        }
    }
}
