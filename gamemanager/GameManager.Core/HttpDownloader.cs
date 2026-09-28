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
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GameManager.Core
{
    /// <summary>
    /// The real IHttpDownloader, over HttpClient. Only EnableModernTls and the exception-mapping paths that do
    /// not need a real connection are unit-tested (with a fake HttpMessageHandler): unit tests never use the
    /// network.
    /// </summary>
    public sealed class HttpDownloader : IHttpDownloader, IDisposable
    {
        /// <summary>
        /// R12: the whole download, headers and body, must finish within this time.
        /// </summary>
        public static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(60);

        private const int BufferSize = 81920;

        private readonly HttpClient client;
        private readonly TimeSpan timeout;

        public HttpDownloader()
            // znix.xyz answers with a 302 to opencomposite.znix.xyz (checked 2026-09-28): redirects are followed.
            : this(new HttpClientHandler { AllowAutoRedirect = true }, DownloadTimeout)
        {
        }

        /// <summary>
        /// Test seam: a fake HttpMessageHandler and a short timeout, so tests exercise the real download logic
        /// (status checks, the Content-Length check, the timeout) without a real connection or a 60 s wait.
        /// </summary>
        internal HttpDownloader(HttpMessageHandler handler, TimeSpan timeout)
        {
            this.timeout = timeout;
            // HttpClient.Timeout would not cover the body when reading with ResponseHeadersRead, so the limit is
            // enforced by a linked cancellation token instead.
            client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenXR-Toolkit-PSVR2-GameManager/1.0");
        }

        public async Task DownloadToFileAsync(string url, string destinationPath, CancellationToken cancellation)
        {
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeoutCts.CancelAfter(timeout);
                try
                {
                    using (HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false))
                    {
                        // F2: on net472 a cancellation token is not reliably observed while a body read is
                        // already blocked in the underlying (synchronous) socket read, so the timeout also
                        // disposes the response, which forces that read to fail instead of hanging past it.
                        using (timeoutCts.Token.Register(() => response.Dispose()))
                        {
                            response.EnsureSuccessStatusCode();
                            long? expectedLength = response.Content.Headers.ContentLength;
                            long written;
                            using (Stream body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            using (var file = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                written = await CopyAsync(body, file, timeoutCts.Token).ConfigureAwait(false);
                            }
                            // F1: a captive portal or a dropped connection can answer 200 OK and then deliver
                            // fewer bytes than it declared; catching that here means a truncated file is never
                            // handed to the caller as if it were complete.
                            if (expectedLength.HasValue && written != expectedLength.Value)
                            {
                                throw new HttpRequestException("The server declared " + expectedLength.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                    + " bytes but delivered " + written.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
                            }
                        }
                    }
                }
                catch (Exception) when (cancellation.IsCancellationRequested)
                {
                    throw new OperationCanceledException("The download was canceled.", cancellation);
                }
                catch (Exception e) when (timeoutCts.IsCancellationRequested)
                {
                    throw new TimeoutException("The server did not answer within " + timeout.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + " seconds.", e);
                }
            }
        }

        private static async Task<long> CopyAsync(Stream source, Stream destination, CancellationToken cancellation)
        {
            var buffer = new byte[BufferSize];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer, 0, read, cancellation).ConfigureAwait(false);
                total += read;
            }
            return total;
        }

        public void Dispose()
        {
            client.Dispose();
        }

        /// <summary>
        /// Call once at startup (R12). With SystemDefault (the .NET 4.7+ default for this target) Windows already
        /// negotiates the best protocol it supports (TLS 1.2 or 1.3), and OR-ing flags into it would pin a fixed
        /// list, so it is left alone. Only a machine forced into a legacy list gets TLS 1.2 added. TLS 1.3 is not
        /// added by hand: requesting it where Windows lacks it can break the handshake.
        /// </summary>
        public static void EnableModernTls()
        {
            if (ServicePointManager.SecurityProtocol != SecurityProtocolType.SystemDefault)
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
        }
    }
}
