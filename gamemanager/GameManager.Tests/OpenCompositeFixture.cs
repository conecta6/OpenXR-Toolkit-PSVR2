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

using System.Text;

namespace GameManager.Tests
{
    /// <summary>
    /// Synthetic bytes standing in for an upstream OpenComposite response. Shared by Task 5's
    /// OpenCompositeCacheTests and Task 6's update-check tests (F5), instead of each duplicating its own copy.
    /// </summary>
    public static class OpenCompositeFixture
    {
        /// <summary>
        /// A 200 KB synthetic DLL; variant changes the last byte, and so the hash.
        /// </summary>
        public static byte[] Dll(ushort machine, byte variant)
        {
            byte[] bytes = PeFixture.Build(machine, 0x80, 200 * 1024);
            bytes[bytes.Length - 1] = variant;
            return bytes;
        }

        /// <summary>
        /// A large HTML page: what a captive portal or proxy might answer with 200 OK instead of the DLL.
        /// </summary>
        public static byte[] HtmlPage()
        {
            return Encoding.ASCII.GetBytes("<!DOCTYPE html><html><body>" + new string(' ', 200 * 1024) + "</body></html>");
        }
    }
}
