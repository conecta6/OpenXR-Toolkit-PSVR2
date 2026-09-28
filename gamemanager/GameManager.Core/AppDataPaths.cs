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

namespace GameManager.Core
{
    /// <summary>
    /// Where Game Manager keeps its own files. Injectable, so tests use a temporary folder and never the real
    /// %LOCALAPPDATA%. Only Program.cs calls ForCurrentUser.
    /// </summary>
    public sealed class AppDataPaths
    {
        public AppDataPaths(string root)
        {
            Root = PathUtil.NormalizeDirectory(root);
        }

        /// <summary>
        /// %LOCALAPPDATA%\OpenXR-Toolkit-PSVR2\GameManager. Nothing is created until something is saved.
        /// </summary>
        public static AppDataPaths ForCurrentUser()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return new AppDataPaths(Path.Combine(localAppData, "OpenXR-Toolkit-PSVR2", "GameManager"));
        }

        public string Root { get; }

        public string SettingsFile
        {
            get { return Path.Combine(Root, "settings.json"); }
        }

        /// <summary>
        /// Holds x64\openvr_api.dll, x86\openvr_api.dll and cache.json.
        /// </summary>
        public string OpenCompositeFolder
        {
            get { return Path.Combine(Root, "opencomposite"); }
        }
    }
}
