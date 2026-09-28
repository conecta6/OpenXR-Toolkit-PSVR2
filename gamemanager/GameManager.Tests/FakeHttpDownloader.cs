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
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GameManager.Core;

namespace GameManager.Tests
{
    /// <summary>
    /// Serves bytes from Respond instead of the network. Unit tests never use the real network.
    /// </summary>
    public sealed class FakeHttpDownloader : IHttpDownloader
    {
        public List<string> RequestedUrls { get; } = new List<string>();

        /// <summary>
        /// The bytes served for a URL, or throw to simulate a failure. Defaults to a 404.
        /// </summary>
        public Func<string, byte[]> Respond { get; set; } = url => throw new HttpRequestException("Response status code does not indicate success: 404 (Not Found).");

        /// <summary>
        /// F3: when set, used instead of Respond. Writes some bytes to destinationPath and then throws, so a
        /// dropped connection mid-download can be simulated with a temporary file genuinely on disk when the
        /// exception propagates — the "no temporary file left behind" tests would otherwise pass trivially,
        /// because Respond throwing before any write leaves nothing to clean up in the first place.
        /// </summary>
        public Action<Stream> WritePartialThenThrow { get; set; }

        public Task DownloadToFileAsync(string url, string destinationPath, CancellationToken cancellation)
        {
            RequestedUrls.Add(url);
            cancellation.ThrowIfCancellationRequested();
            if (WritePartialThenThrow != null)
            {
                using (var stream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    WritePartialThenThrow(stream);
                }
                throw new HttpRequestException("The connection was closed before the response completed.");
            }
            File.WriteAllBytes(destinationPath, Respond(url));
            return Task.CompletedTask;
        }
    }
}
