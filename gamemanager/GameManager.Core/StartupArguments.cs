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
using System.Text;

namespace GameManager.Core
{
    /// <summary>
    /// The command line Game Manager accepts: only "--app-data &lt;folder&gt;", passed by the elevated relaunch so the
    /// administrator copy keeps using the app data (records, OpenComposite cache, settings) of the user who started it,
    /// not the administrator account's. Pure functions, so they are tested without any elevation.
    /// </summary>
    public static class StartupArguments
    {
        public const string AppDataSwitch = "--app-data";

        /// <summary>
        /// The app-data root from the arguments, or null. Accepted only as exactly two arguments, the switch and a
        /// fully qualified folder. Anything else is ignored and explained in warning (null when there is none to give).
        /// </summary>
        public static string ParseAppData(string[] args, out string warning)
        {
            warning = null;
            if (args == null || args.Length == 0)
            {
                return null;
            }
            if (args.Length != 2 || !string.Equals(args[0], AppDataSwitch, StringComparison.Ordinal))
            {
                warning = "Ignored unknown command-line arguments (only " + AppDataSwitch + " <folder> is accepted): " + string.Join(" ", args) + ".";
                return null;
            }
            if (!PathUtil.IsFullyQualified(args[1]))
            {
                warning = "Ignored " + AppDataSwitch + ": \"" + args[1] + "\" is not a full path (like C:\\Users\\Name\\AppData\\Local\\...). The current user's own folder is used.";
                return null;
            }
            try
            {
                return PathUtil.NormalizeDirectory(args[1]);
            }
            catch (Exception e) when (PathUtil.IsInvalidPathError(e))
            {
                warning = "Ignored " + AppDataSwitch + ": \"" + args[1] + "\" is not a valid folder (" + e.Message + "). The current user's own folder is used.";
                return null;
            }
        }

        /// <summary>
        /// The command-line text that ParseAppData reads back: --app-data "folder", quoted the way Windows splits
        /// arguments (a folder ending in a backslash, like "E:\", would otherwise swallow the closing quote).
        /// </summary>
        public static string AppDataArgument(string appDataRoot)
        {
            var quoted = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in appDataRoot)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (c == '"')
                {
                    quoted.Append('\\', backslashes * 2 + 1);
                }
                else
                {
                    quoted.Append('\\', backslashes);
                }
                backslashes = 0;
                quoted.Append(c);
            }
            quoted.Append('\\', backslashes * 2);
            quoted.Append('"');
            return AppDataSwitch + " " + quoted;
        }
    }
}
