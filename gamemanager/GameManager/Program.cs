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
using System.Windows.Forms;
using GameManager.Core;

namespace GameManager
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application. Runs unelevated (no manifest, so asInvoker); the elevated
        /// relaunch of R22 is only ever offered, from ElevatedRelaunch.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            HttpDownloader.EnableModernTls();

            string compatibilityPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CompatibilityList.FileName);
            string argumentWarning;
            string appDataOverride = StartupArguments.ParseAppData(args, out argumentWarning);
            if (argumentWarning != null)
            {
                MessageBox.Show(argumentWarning, "Game Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            AppDataPaths appData = appDataOverride != null ? new AppDataPaths(appDataOverride) : AppDataPaths.ForCurrentUser();
            Func<DateTime> utcNow = () => DateTime.UtcNow;
            using (var http = new HttpDownloader())
            {
                var openComposite = new OpenCompositeCache(appData, new SettingsStore(appData.SettingsFile), http, utcNow);
                var state = new PatchStateStore(appData.StateFile, utcNow);
                var planner = new PatchPlanner(openComposite, state, utcNow);
                var patchService = new PatchService(state, new RunningGameGuard(new WindowsProcessImageSource()), utcNow);
                Application.Run(new MainForm(new SteamLocator(new WindowsRegistryReader()), compatibilityPath, openComposite, planner, patchService, appData.Root));
            }
        }
    }
}
