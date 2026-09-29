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

using System.Drawing;
using System.Windows.Forms;

namespace GameManager
{
    /// <summary>
    /// A scrollable, read-only text window for plans (simulation mode) and results: both can list many files.
    /// </summary>
    public sealed class PlanTextDialog : Form
    {
        public PlanTextDialog(string title, string text)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            ClientSize = new Size(760, 420);
            MinimumSize = new Size(420, 240);

            var box = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Text = text,
            };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Bottom,
                AutoSize = true,
                Padding = new Padding(8),
            };
            buttons.Controls.Add(ok);
            Controls.Add(box);
            Controls.Add(buttons);
            AcceptButton = ok;
            CancelButton = ok;
        }

        public static void ShowText(IWin32Window owner, string title, string text)
        {
            using (var dialog = new PlanTextDialog(title, text))
            {
                dialog.ShowDialog(owner);
            }
        }
    }
}
