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
using System.Linq;
using System.Threading;
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    /// <summary>
    /// A temporary Steam library with one game folder ("Beat Saber", AppID 620980), an app-data folder with an
    /// OpenComposite cache filled through the fake downloader, a state store and a simulated process list.
    /// Never touches a real game, the real %LOCALAPPDATA% or the network.
    /// </summary>
    public sealed class PatchFixture : IDisposable
    {
        public static readonly DateTime Start = new DateTime(2026, 9, 29, 10, 15, 0, DateTimeKind.Utc);

        public PatchFixture()
        {
            Temp = new TempDir();
            Now = Start;
            Paths = new AppDataPaths(Temp.PathOf("AppData"));
            Settings = new SettingsStore(Paths.SettingsFile);
            Http = new FakeHttpDownloader();
            Cache = new OpenCompositeCache(Paths, Settings, Http, () => Now);
            StateStore = new PatchStateStore(Paths.StateFile, () => Now);
            Planner = new PatchPlanner(Cache, StateStore, () => Now);
            Processes = new FakeProcessImageSource();
            Guard = new RunningGameGuard(Processes, directory => null);
            Service = new PatchService(StateStore, Guard, () => Now);
            InstallDir = PathUtil.NormalizeDirectory(Temp.CreateDirectory(@"Library\steamapps\common\Beat Saber"));
        }

        public TempDir Temp { get; }
        public DateTime Now { get; set; }
        public AppDataPaths Paths { get; }
        public SettingsStore Settings { get; }
        public FakeHttpDownloader Http { get; }
        public OpenCompositeCache Cache { get; }
        public PatchStateStore StateStore { get; }
        public PatchPlanner Planner { get; }
        public FakeProcessImageSource Processes { get; }
        public RunningGameGuard Guard { get; }
        public PatchService Service { get; }
        public string InstallDir { get; }

        public SteamGame Game
        {
            get { return new SteamGame(620980, "Beat Saber", InstallDir, Temp.PathOf("Library")); }
        }

        /// <summary>
        /// A game's own openvr_api.dll: a small valid PE of that machine; variant changes the content (and hash).
        /// </summary>
        public static byte[] OriginalDll(ushort machine, byte variant)
        {
            byte[] bytes = PeFixture.Build(machine, 0x80, 4096);
            bytes[bytes.Length - 1] = variant;
            return bytes;
        }

        public string WriteGameFile(string relativePath, byte[] content)
        {
            string path = Path.Combine(InstallDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, content);
            return path;
        }

        /// <summary>
        /// Accepts the license and "downloads" OpenComposite for arch through the fake downloader
        /// (OpenCompositeFixture.Dll with this variant). Returns the SHA-256 of the cached file.
        /// </summary>
        public string CacheOpenComposite(OpenCompositeArch arch, byte variant)
        {
            Settings.Save(new AppSettings { OpenCompositeLicenseAccepted = true });
            ushort machine = arch == OpenCompositeArch.X64 ? PeFixture.MachineX64 : PeFixture.MachineX86;
            Http.Respond = url => OpenCompositeFixture.Dll(machine, variant);
            DownloadOutcome outcome = Cache.DownloadAsync(arch, new List<string>(), CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual(DownloadStatus.Downloaded, outcome.Status, outcome.Message);
            return FileHash.Sha256(Cache.DllPath(arch));
        }

        /// <summary>
        /// The game as a fresh scan sees it: classified from disk, not listed in compatibility.json.
        /// </summary>
        public GameEntry Scan()
        {
            GameClassification classification = GameClassifier.Classify(InstallDir);
            return new GameEntry(Game, classification, CompatibilityVerdict.For(null, classification.AntiCheatMarkers, classification.UncheckedFolders));
        }

        /// <summary>
        /// The game classified from disk, with a chosen verdict (blocked, or anti-cheat not ruled out).
        /// </summary>
        public GameEntry ScanWith(CompatibilityVerdict verdict)
        {
            return new GameEntry(Game, GameClassifier.Classify(InstallDir), verdict);
        }

        public PatchRecord FindRecord(string dllPath)
        {
            return StateStore.Load(new List<string>()).Find(dllPath);
        }

        /// <summary>
        /// Path and SHA-256 of every file under the temporary folder, to prove that nothing changed.
        /// </summary>
        public string Snapshot()
        {
            IEnumerable<string> lines = Directory.GetFiles(Temp.Root, "*", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(p => p + " " + FileHash.Sha256(p));
            return string.Join("\n", lines);
        }

        /// <summary>
        /// Another game folder in the same temporary library.
        /// </summary>
        public SteamGame AddGame(int appId, string name)
        {
            string folder = PathUtil.NormalizeDirectory(Temp.CreateDirectory(@"Library\steamapps\common\" + name));
            return new SteamGame(appId, name, folder, Temp.PathOf("Library"));
        }

        public static string WriteFileIn(SteamGame game, string relativePath, byte[] content)
        {
            string path = Path.Combine(game.InstallDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, content);
            return path;
        }

        /// <summary>
        /// Any game classified from disk; verdict null means "as a scan would see it".
        /// </summary>
        public GameEntry ScanOf(SteamGame game, CompatibilityVerdict verdict)
        {
            GameClassification classification = GameClassifier.Classify(game.InstallDir);
            return new GameEntry(game, classification, verdict ?? CompatibilityVerdict.For(null, classification.AntiCheatMarkers, classification.UncheckedFolders));
        }

        public static bool CanOpenForWrite(string path)
        {
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    return true;
                }
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            Temp.Dispose();
        }
    }
}
