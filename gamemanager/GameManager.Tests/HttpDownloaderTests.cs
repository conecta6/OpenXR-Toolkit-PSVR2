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
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    /// <summary>
    /// EnableModernTls, plus the download logic exercised through a fake HttpMessageHandler (never the network):
    /// the Content-Length mismatch check (F1) and the timeout-disposes-the-response path (F2). A real connection
    /// is never used.
    /// </summary>
    [TestClass]
    public class HttpDownloaderTests
    {
        private TempDir temp;

        [TestInitialize]
        public void SetUp()
        {
            temp = new TempDir();
        }

        [TestCleanup]
        public void TearDown()
        {
            temp.Dispose();
        }

        [TestMethod]
        public void EnableModernTls_LegacyProtocolList_AddsTls12()
        {
            SecurityProtocolType original = ServicePointManager.SecurityProtocol;
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11;

                HttpDownloader.EnableModernTls();

                Assert.AreNotEqual((SecurityProtocolType)0, ServicePointManager.SecurityProtocol & SecurityProtocolType.Tls12);
                Assert.AreNotEqual((SecurityProtocolType)0, ServicePointManager.SecurityProtocol & SecurityProtocolType.Tls11, "existing protocols are kept");
            }
            finally
            {
                ServicePointManager.SecurityProtocol = original;
            }
        }

        [TestMethod]
        public void EnableModernTls_SystemDefault_IsLeftToWindows()
        {
            SecurityProtocolType original = ServicePointManager.SecurityProtocol;
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.SystemDefault;

                HttpDownloader.EnableModernTls();

                Assert.AreEqual(SecurityProtocolType.SystemDefault, ServicePointManager.SecurityProtocol);
            }
            finally
            {
                ServicePointManager.SecurityProtocol = original;
            }
        }

        /// <summary>
        /// F1: a captive portal or a dropped connection can answer 200 OK, declare a Content-Length and then
        /// deliver fewer bytes. The download must fail rather than hand a truncated file to the caller.
        /// </summary>
        [TestMethod]
        public async Task DownloadToFileAsync_FewerBytesThanContentLength_Throws()
        {
            var content = new ByteArrayContent(new byte[10]);
            content.Headers.ContentLength = 20; // declared longer than the 10 bytes actually served
            var handler = new FakeHttpMessageHandler(request => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            string dest = temp.PathOf("out.dll");

            using (var downloader = new HttpDownloader(handler, TimeSpan.FromSeconds(5)))
            {
                await Assert.ThrowsExceptionAsync<HttpRequestException>(
                    () => downloader.DownloadToFileAsync("http://fake.invalid/download", dest, CancellationToken.None));
            }
        }

        [TestMethod]
        public async Task DownloadToFileAsync_ExactContentLength_Succeeds()
        {
            var content = new ByteArrayContent(new byte[10]);
            content.Headers.ContentLength = 10;
            var handler = new FakeHttpMessageHandler(request => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            string dest = temp.PathOf("out.dll");

            using (var downloader = new HttpDownloader(handler, TimeSpan.FromSeconds(5)))
            {
                await downloader.DownloadToFileAsync("http://fake.invalid/download", dest, CancellationToken.None);
            }

            Assert.AreEqual(10, new FileInfo(dest).Length);
        }

        [TestMethod]
        public async Task DownloadToFileAsync_NonSuccessStatus_ThrowsHttpRequestException()
        {
            var handler = new FakeHttpMessageHandler(request => new HttpResponseMessage(HttpStatusCode.NotFound));
            string dest = temp.PathOf("out.dll");

            using (var downloader = new HttpDownloader(handler, TimeSpan.FromSeconds(5)))
            {
                await Assert.ThrowsExceptionAsync<HttpRequestException>(
                    () => downloader.DownloadToFileAsync("http://fake.invalid/download", dest, CancellationToken.None));
            }
        }

        /// <summary>
        /// F2: on net472 a cancellation token is not reliably observed while a body read is already blocked, so
        /// the timeout disposes the response instead, forcing the stuck read to fail. This proves the download
        /// gives up close to the (short, test-only) timeout instead of hanging, and that the response is
        /// actually disposed (the blocking stream unblocks and is marked disposed).
        /// </summary>
        [TestMethod]
        public async Task DownloadToFileAsync_ServerHangsMidBody_TimesOutAndDisposesResponse()
        {
            var blocking = new BlockingStream();
            var handler = new FakeHttpMessageHandler(request => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(blocking) });
            string dest = temp.PathOf("out.dll");
            var stopwatch = Stopwatch.StartNew();

            using (var downloader = new HttpDownloader(handler, TimeSpan.FromMilliseconds(200)))
            {
                await Assert.ThrowsExceptionAsync<TimeoutException>(
                    () => downloader.DownloadToFileAsync("http://fake.invalid/download", dest, CancellationToken.None));
            }

            stopwatch.Stop();
            Assert.IsTrue(stopwatch.ElapsedMilliseconds < 10000, "must time out promptly, not hang for the real 60 s limit");
            Assert.IsTrue(blocking.Disposed, "the response (and so the body stream) must be disposed on timeout");
        }

        [TestMethod]
        public async Task DownloadToFileAsync_CallerCancellation_ThrowsOperationCanceled()
        {
            var blocking = new BlockingStream();
            var handler = new FakeHttpMessageHandler(request => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(blocking) });
            string dest = temp.PathOf("out.dll");
            using (var cts = new CancellationTokenSource())
            using (var downloader = new HttpDownloader(handler, TimeSpan.FromSeconds(30)))
            {
                cts.CancelAfter(TimeSpan.FromMilliseconds(50));

                await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                    () => downloader.DownloadToFileAsync("http://fake.invalid/download", dest, cts.Token));
            }
        }

        /// <summary>
        /// An HttpMessageHandler that answers synchronously from a delegate: no socket, no real network.
        /// </summary>
        private sealed class FakeHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> respond;

            public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            {
                this.respond = respond;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(respond(request));
            }
        }

        /// <summary>
        /// A response body stream whose Read blocks forever until Dispose is called, simulating a server that
        /// stops answering mid-transfer. Disposing unblocks the pending read with an ObjectDisposedException,
        /// standing in for what disposing a real HttpClient response stream does mid-read.
        /// </summary>
        private sealed class BlockingStream : Stream
        {
            private readonly ManualResetEventSlim gate = new ManualResetEventSlim(false);

            public bool Disposed { get; private set; }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                gate.Wait();
                throw new ObjectDisposedException(nameof(BlockingStream));
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                gate.Set();
                base.Dispose(disposing);
            }
        }
    }
}
