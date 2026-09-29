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
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Forms;

namespace GameManager
{
    /// <summary>
    /// R22: Game Manager runs unelevated. When Windows denies a write it offers to restart as administrator (default
    /// No); it never elevates on its own.
    /// </summary>
    internal static class ElevatedRelaunch
    {
        private const int ErrorCancelled = 1223;

        /// <summary>
        /// True when an elevated copy was started; the caller then closes this window.
        /// </summary>
        public static bool Offer(IWin32Window owner, string reason)
        {
            DialogResult answer = MessageBox.Show(
                owner,
                reason + Environment.NewLine + Environment.NewLine
                    + "Windows did not allow Game Manager to change this game's files (games under \"Program Files\" often need administrator rights). "
                    + "Restart Game Manager as administrator and try again?",
                "Permission needed",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                return false;
            }
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true, Verb = "runas" });
                return true;
            }
            catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
            {
                // The user answered No to the Windows prompt.
                return false;
            }
            catch (Win32Exception e)
            {
                MessageBox.Show(owner, "Could not restart as administrator: " + e.Message, "Permission needed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }
    }
}
