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
using System.Drawing;
using System.Windows.Forms;
using GameManager.Core;

namespace GameManager
{
    /// <summary>
    /// R13: the GPLv3 notice shown before the first OpenComposite download. DialogResult.OK means accepted.
    /// </summary>
    public sealed class LicenseNoticeForm : Form
    {
        public LicenseNoticeForm()
        {
            Text = "OpenComposite license";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 240);

            string notice = DisplayText.OpenCompositeLicenseNotice;
            var text = new LinkLabel
            {
                Text = notice,
                Location = new Point(16, 16),
                Size = new Size(528, 160),
                UseMnemonic = false,
            };
            text.LinkArea = new LinkArea(notice.IndexOf(DisplayText.OpenCompositeSourceUrl, StringComparison.Ordinal), DisplayText.OpenCompositeSourceUrl.Length);
            text.LinkClicked += OnLinkClicked;

            var accept = new Button { Text = "Accept and download", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Bottom,
                AutoSize = true,
                Padding = new Padding(8),
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(accept);

            Controls.Add(text);
            Controls.Add(buttons);
            // Escape declines. There is no default AcceptButton: accepting takes a deliberate click.
            CancelButton = cancel;
        }

        private static void OnLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                Process.Start(DisplayText.OpenCompositeSourceUrl);
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
            {
                // No default browser: the address stays readable in the dialog.
            }
        }
    }
}
