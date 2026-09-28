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
using Microsoft.Win32;

namespace GameManager.Core
{
    /// <summary>
    /// Finds the Steam install folder from the registry.
    /// </summary>
    public sealed class SteamLocator
    {
        private readonly IRegistryReader registry;

        public SteamLocator(IRegistryReader registry)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>
        /// The normalized Steam folder, or null when Steam is not installed or its folder is gone.
        /// </summary>
        public string FindSteamRoot()
        {
            // Written by the Steam client for the current user, with forward slashes ("c:/program files (x86)/steam").
            string fromUser = registry.ReadString(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Valve\Steam", "SteamPath");
            string root = ExistingDirectory(fromUser);
            if (root != null)
            {
                return root;
            }

            // Written by the installer. The 32-bit view is HKLM\SOFTWARE\WOW6432Node\Valve\Steam on 64-bit Windows.
            string fromMachine = registry.ReadString(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Valve\Steam", "InstallPath");
            return ExistingDirectory(fromMachine);
        }

        private static string ExistingDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }
            try
            {
                string normalized = PathUtil.NormalizeDirectory(path);
                return Directory.Exists(normalized) ? normalized : null;
            }
            catch (Exception e) when (PathUtil.IsInvalidPathError(e))
            {
                return null;
            }
        }
    }
}
