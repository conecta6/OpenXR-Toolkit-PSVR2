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

using System.IO;
using System.Text;

namespace GameManager.Tests
{
    /// <summary>
    /// Builds a fake Steam install inside a TempDir: libraries, libraryfolders.vdf, appmanifest files, game folders.
    /// </summary>
    public sealed class SteamFixture
    {
        private readonly TempDir temp;

        public SteamFixture(TempDir temp)
        {
            this.temp = temp;
            SteamRoot = temp.CreateDirectory("Steam");
            Directory.CreateDirectory(Path.Combine(SteamRoot, "steamapps", "common"));
        }

        public string SteamRoot { get; }

        public string CreateLibrary(string relativePath)
        {
            string library = temp.CreateDirectory(relativePath);
            Directory.CreateDirectory(Path.Combine(library, "steamapps", "common"));
            return library;
        }

        public void WriteLibraryFolders(string content)
        {
            File.WriteAllText(Path.Combine(SteamRoot, "steamapps", "libraryfolders.vdf"), content, new UTF8Encoding(false));
        }

        /// <summary>
        /// Current format: "libraryfolders" { "0" { "path" "..." ... } "1" { ... } }.
        /// </summary>
        public static string CurrentFormat(params string[] libraryPaths)
        {
            var text = new StringBuilder();
            text.Append("\"libraryfolders\"\n{\n");
            for (int i = 0; i < libraryPaths.Length; i++)
            {
                text.Append("\t\"" + i + "\"\n\t{\n");
                text.Append("\t\t\"path\"\t\t\"" + Escape(libraryPaths[i]) + "\"\n");
                text.Append("\t\t\"label\"\t\t\"\"\n");
                text.Append("\t\t\"contentid\"\t\t\"4521\"\n");
                text.Append("\t\t\"apps\"\n\t\t{\n\t\t\t\"620980\"\t\t\"123\"\n\t\t}\n");
                text.Append("\t}\n");
            }
            text.Append("}\n");
            return text.ToString();
        }

        /// <summary>
        /// Legacy format: "LibraryFolders" { "TimeNextStatsReport" "..." "ContentStatsID" "..." "1" "D:\\Lib" }.
        /// The Steam folder itself is implicit and never listed.
        /// </summary>
        public static string LegacyFormat(params string[] extraLibraryPaths)
        {
            var text = new StringBuilder();
            text.Append("\"LibraryFolders\"\n{\n");
            text.Append("\t\"TimeNextStatsReport\"\t\t\"1690000000\"\n");
            text.Append("\t\"ContentStatsID\"\t\t\"-4541\"\n");
            for (int i = 0; i < extraLibraryPaths.Length; i++)
            {
                text.Append("\t\"" + (i + 1) + "\"\t\t\"" + Escape(extraLibraryPaths[i]) + "\"\n");
            }
            text.Append("}\n");
            return text.ToString();
        }

        public static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>
        /// Writes steamapps\appmanifest_{appId}.acf in the library and, when createFolder is true,
        /// creates steamapps\common\{installDir}. Returns the game folder path.
        /// </summary>
        public string AddGame(string library, int appId, string name, string installDir, bool createFolder = true)
        {
            string manifest =
                "\"AppState\"\n{\n" +
                "\t\"appid\"\t\t\"" + appId + "\"\n" +
                "\t\"Universe\"\t\t\"1\"\n" +
                "\t\"name\"\t\t\"" + Escape(name) + "\"\n" +
                "\t\"StateFlags\"\t\t\"4\"\n" +
                "\t\"installdir\"\t\t\"" + Escape(installDir) + "\"\n" +
                "}\n";
            File.WriteAllText(Path.Combine(library, "steamapps", "appmanifest_" + appId + ".acf"), manifest, new UTF8Encoding(false));
            string folder = Path.Combine(library, "steamapps", "common", installDir);
            if (createFolder)
            {
                Directory.CreateDirectory(folder);
            }
            return folder;
        }
    }
}
