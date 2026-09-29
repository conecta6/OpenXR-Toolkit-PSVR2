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
using System.Globalization;

namespace GameManager.Core
{
    /// <summary>
    /// R21: opencomposite.ini, written next to a patched openvr_api.dll. OpenComposite aborts on keys it does not
    /// know, so only allow-listed keys are ever written; today that is only supersampleRatio, and Render takes
    /// nothing else.
    /// </summary>
    public static class OpenCompositeIni
    {
        public const string FileName = "opencomposite.ini";
        public const double MinSupersampleRatio = 0.5;
        public const double MaxSupersampleRatio = 2.0;
        public const double DefaultSupersampleRatio = 1.0;

        public static IReadOnlyList<string> AllowedKeys { get; } = new[] { "supersampleRatio" };

        /// <summary>
        /// "supersampleRatio=1.25\r\n", always with a dot (invariant culture), whatever the Windows language.
        /// </summary>
        public static string Render(double supersampleRatio)
        {
            if (double.IsNaN(supersampleRatio) || supersampleRatio < MinSupersampleRatio || supersampleRatio > MaxSupersampleRatio)
            {
                throw new ArgumentOutOfRangeException(nameof(supersampleRatio), supersampleRatio, "supersampleRatio must be between 0.5 and 2.0.");
            }
            return AllowedKeys[0] + "=" + supersampleRatio.ToString("0.0##", CultureInfo.InvariantCulture) + "\r\n";
        }
    }
}
