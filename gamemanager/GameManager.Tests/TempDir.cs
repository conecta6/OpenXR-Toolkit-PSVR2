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
using System.Diagnostics;
using System.IO;
using System.Text;

namespace GameManager.Tests
{
    /// <summary>
    /// A unique folder under %TEMP%\GameManagerTests, deleted by Dispose.
    /// </summary>
    public sealed class TempDir : IDisposable
    {
        private readonly List<string> links = new List<string>();

        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "GameManagerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string PathOf(string relativePath)
        {
            return Path.Combine(Root, relativePath);
        }

        public string CreateDirectory(string relativePath)
        {
            string path = PathOf(relativePath);
            Directory.CreateDirectory(path);
            return path;
        }

        public string WriteText(string relativePath, string content)
        {
            return WriteText(relativePath, content, new UTF8Encoding(false));
        }

        public string WriteText(string relativePath, string content, Encoding encoding)
        {
            string path = PathOf(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content, encoding);
            return path;
        }

        public string WriteBytes(string relativePath, byte[] content)
        {
            string path = PathOf(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, content);
            return path;
        }

        /// <summary>
        /// Creates a directory junction with "mklink /J", which needs no admin rights.
        /// Returns false when the junction could not be created.
        /// </summary>
        public bool TryCreateJunction(string relativeLink, string targetPath)
        {
            string link = PathOf(relativeLink);
            Directory.CreateDirectory(Path.GetDirectoryName(link));
            int exitCode = RunHidden("cmd.exe", "/c mklink /J \"" + link + "\" \"" + targetPath + "\"");
            bool created = exitCode == 0
                && Directory.Exists(link)
                && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0;
            if (created)
            {
                links.Add(link);
            }
            return created;
        }

        public static int RunHidden(string fileName, string arguments)
        {
            var info = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (Process process = Process.Start(info))
            {
                process.WaitForExit();
                return process.ExitCode;
            }
        }

        public void Dispose()
        {
            // Remove junctions first, so the recursive delete can never follow one.
            foreach (string link in links)
            {
                try
                {
                    if (Directory.Exists(link))
                    {
                        Directory.Delete(link, false);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            try
            {
                Directory.Delete(Root, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
