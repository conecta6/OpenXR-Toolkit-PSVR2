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
using System.Runtime.Serialization;
using System.Security;

namespace GameManager.Core
{
    public sealed class AppSettings
    {
        /// <summary>
        /// R13: the GPLv3 notice was shown and accepted. No acceptance, no download.
        /// </summary>
        public bool OpenCompositeLicenseAccepted { get; set; }

        public DateTime? OpenCompositeLicenseAcceptedUtc { get; set; }

        /// <summary>
        /// R14: when upstream was last checked for a new OpenComposite build.
        /// </summary>
        public DateTime? LastUpdateCheckUtc { get; set; }
    }

    /// <summary>
    /// settings.json in the app-data folder. A missing file gives defaults; an unreadable or corrupt one gives
    /// defaults plus a warning (the worst case is being asked to accept the license notice again).
    /// </summary>
    public sealed class SettingsStore
    {
        public SettingsStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path is empty.", nameof(path));
            }
            FilePath = path;
        }

        public string FilePath { get; }

        public AppSettings Load(IList<string> warnings)
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }
            SettingsDto dto;
            try
            {
                dto = JsonFile.Read<SettingsDto>(FilePath);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is SecurityException || JsonFile.IsCorruptDataError(e))
            {
                warnings.Add("Could not read settings " + FilePath + " (" + e.Message + "). Default settings are used.");
                return new AppSettings();
            }
            if (dto == null)
            {
                warnings.Add("Settings file " + FilePath + " holds no settings. Default settings are used.");
                return new AppSettings();
            }
            return new AppSettings
            {
                OpenCompositeLicenseAccepted = dto.OpenCompositeLicenseAccepted,
                OpenCompositeLicenseAcceptedUtc = JsonFile.ParseUtc(dto.OpenCompositeLicenseAcceptedUtc),
                LastUpdateCheckUtc = JsonFile.ParseUtc(dto.LastOpenCompositeCheckUtc),
            };
        }

        /// <summary>
        /// Atomic write (temporary file, then replace). I/O and permission errors pass through.
        /// </summary>
        public void Save(AppSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }
            JsonFile.WriteAtomic(FilePath, new SettingsDto
            {
                OpenCompositeLicenseAccepted = settings.OpenCompositeLicenseAccepted,
                OpenCompositeLicenseAcceptedUtc = JsonFile.FormatUtc(settings.OpenCompositeLicenseAcceptedUtc),
                LastOpenCompositeCheckUtc = JsonFile.FormatUtc(settings.LastUpdateCheckUtc),
            });
        }

        [DataContract]
        private sealed class SettingsDto
        {
            [DataMember(Name = "openCompositeLicenseAccepted")]
            public bool OpenCompositeLicenseAccepted { get; set; }

            [DataMember(Name = "openCompositeLicenseAcceptedUtc", EmitDefaultValue = false)]
            public string OpenCompositeLicenseAcceptedUtc { get; set; }

            [DataMember(Name = "lastOpenCompositeCheckUtc", EmitDefaultValue = false)]
            public string LastOpenCompositeCheckUtc { get; set; }
        }
    }
}
