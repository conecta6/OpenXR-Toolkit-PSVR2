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
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GameManager.Core
{
    /// <summary>
    /// The executable paths of running processes. A seam, so tests can simulate a running game.
    /// </summary>
    public interface IProcessImageSource
    {
        IReadOnlyList<string> GetRunningImagePaths();
    }

    /// <summary>
    /// R19: every process's executable path through QueryFullProcessImageName with
    /// PROCESS_QUERY_LIMITED_INFORMATION. Processes it cannot open (system processes, elevated processes of other
    /// users) are skipped.
    /// </summary>
    public sealed class WindowsProcessImageSource : IProcessImageSource
    {
        public IReadOnlyList<string> GetRunningImagePaths()
        {
            var paths = new List<string>();
            var buffer = new StringBuilder(NativeMethods.MaxLongPath);
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    string path = QueryImagePath(process.Id, buffer);
                    if (path != null)
                    {
                        paths.Add(path);
                    }
                }
            }
            return paths;
        }

        /// <summary>
        /// The full Win32 path of a process's executable, or null when the process cannot be opened or queried.
        /// </summary>
        internal static string QueryImagePath(int processId)
        {
            return QueryImagePath(processId, new StringBuilder(NativeMethods.MaxLongPath));
        }

        private static string QueryImagePath(int processId, StringBuilder buffer)
        {
            IntPtr handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
            if (handle == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                buffer.Clear();
                int size = buffer.Capacity;
                return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }
    }

    public enum GuardVerdict
    {
        Clear,

        /// <summary>A running process's executable is under the game's install folder.</summary>
        GameRunning,

        /// <summary>A target DLL is open in another program (the game, Steam, an anti-virus scan).</summary>
        FileInUse,

        /// <summary>Windows denies write access to a target DLL (R22: an elevated relaunch may help).</summary>
        AccessDenied,

        /// <summary>
        /// A target cannot be examined or written for a reason elevation does not fix: it changed since the scan,
        /// its path is too long or is a folder, or it is read-only.
        /// </summary>
        FileProblem,
    }

    public sealed class GuardResult
    {
        private GuardResult(GuardVerdict verdict, string message)
        {
            Verdict = verdict;
            Message = message;
        }

        public static GuardResult Ok { get; } = new GuardResult(GuardVerdict.Clear, "");

        public GuardVerdict Verdict { get; }

        /// <summary>
        /// Why the guard refused; "" when clear.
        /// </summary>
        public string Message { get; }

        public bool IsClear
        {
            get { return Verdict == GuardVerdict.Clear; }
        }

        /// <summary>
        /// R22: Windows denied write access; running Game Manager as administrator may help.
        /// </summary>
        public bool NeedsElevation
        {
            get { return Verdict == GuardVerdict.AccessDenied; }
        }

        internal static GuardResult Refuse(GuardVerdict verdict, string message)
        {
            return new GuardResult(verdict, message);
        }
    }

    /// <summary>
    /// R19: before any write into a game folder, refuses when a running process's executable is under the game's
    /// install folder (or under the folder a junction install folder points to), and when a target DLL cannot be
    /// opened for exclusive write. Writes nothing.
    /// </summary>
    public sealed class RunningGameGuard
    {
        private readonly IProcessImageSource processes;
        private readonly Func<string, string> resolveFinalPath;

        public RunningGameGuard(IProcessImageSource processes)
            : this(processes, TryResolveFinalPath)
        {
        }

        /// <summary>
        /// Test seam: resolveFinalPath stands in for TryResolveFinalPath.
        /// </summary>
        internal RunningGameGuard(IProcessImageSource processes, Func<string, string> resolveFinalPath)
        {
            this.processes = processes ?? throw new ArgumentNullException(nameof(processes));
            this.resolveFinalPath = resolveFinalPath ?? throw new ArgumentNullException(nameof(resolveFinalPath));
        }

        public GuardResult Check(string installDir, IEnumerable<string> targetFiles)
        {
            if (targetFiles == null)
            {
                throw new ArgumentNullException(nameof(targetFiles));
            }
            var folders = new List<string> { PathUtil.NormalizeDirectory(installDir) };
            // A game moved to another drive and linked back: its processes run from the link's target.
            string finalPath = resolveFinalPath(folders[0]);
            if (finalPath != null && PathUtil.IsFullyQualified(finalPath))
            {
                string normalizedFinal = PathUtil.NormalizeDirectory(finalPath);
                if (!string.Equals(normalizedFinal, folders[0], StringComparison.OrdinalIgnoreCase))
                {
                    folders.Add(normalizedFinal);
                }
            }

            foreach (string image in processes.GetRunningImagePaths())
            {
                foreach (string folder in folders)
                {
                    if (image != null && PathUtil.IsUnder(image, folder))
                    {
                        return GuardResult.Refuse(GuardVerdict.GameRunning, "The game is running (" + image + "). Close it and try again.");
                    }
                }
            }

            foreach (string file in targetFiles)
            {
                if (!PathUtil.IsFullyQualified(file))
                {
                    throw new ArgumentException("A target path is not absolute: \"" + file + "\".", nameof(targetFiles));
                }
                GuardResult refusal = ProbeTarget(file);
                if (refusal != null)
                {
                    return refusal;
                }
            }
            return GuardResult.Ok;
        }

        /// <summary>
        /// Null when the target can be opened for exclusive write, or does not exist yet in a folder that does (a
        /// restore of a deleted DLL, a new opencomposite.ini). Anything that cannot be examined is refused.
        /// </summary>
        private static GuardResult ProbeTarget(string file)
        {
            try
            {
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(file);
                }
                catch (FileNotFoundException)
                {
                    return null;
                }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    return GuardResult.Refuse(GuardVerdict.FileProblem, file + " is a folder, not a file. Game Manager will not touch it.");
                }
                try
                {
                    // Opening for write changes nothing. FileShare.None fails while any other handle is open (the
                    // game, Steam, an anti-virus scan), and a DLL loaded by a process cannot be opened for write.
                    using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                    }
                }
                catch (UnauthorizedAccessException e)
                {
                    if ((attributes & FileAttributes.ReadOnly) != 0)
                    {
                        return GuardResult.Refuse(GuardVerdict.FileProblem, file + " is marked read-only. Clear the read-only attribute in its Properties and try again.");
                    }
                    return GuardResult.Refuse(GuardVerdict.AccessDenied, "Windows does not allow Game Manager to change " + file + " (" + e.Message + ").");
                }
                return null;
            }
            catch (UnauthorizedAccessException e)
            {
                return GuardResult.Refuse(GuardVerdict.AccessDenied, "Windows does not allow Game Manager to look at " + file + " (" + e.Message + ").");
            }
            catch (PathTooLongException)
            {
                return GuardResult.Refuse(GuardVerdict.FileProblem, "The path is too long for Game Manager to change: " + file);
            }
            catch (DirectoryNotFoundException)
            {
                return GuardResult.Refuse(GuardVerdict.FileProblem, "The folder of " + file + " is gone. The game folder changed since the scan: refresh the list and try again.");
            }
            catch (FileNotFoundException)
            {
                return GuardResult.Refuse(GuardVerdict.FileProblem, file + " disappeared. It changed since the scan: refresh the list and try again.");
            }
            catch (IOException e)
            {
                return GuardResult.Refuse(GuardVerdict.FileInUse, file + " is in use by another program (" + e.Message + "). Close the game, and wait for Steam if it is updating it, then try again.");
            }
            catch (Exception e) when (PathUtil.IsInvalidPathError(e))
            {
                return GuardResult.Refuse(GuardVerdict.FileProblem, "Game Manager cannot examine " + file + " (" + e.Message + ").");
            }
        }

        /// <summary>
        /// The folder a junction or symbolic link finally points to (GetFinalPathNameByHandle), without the
        /// "\\?\" prefix; the folder itself when it is not a link; null when it cannot be opened.
        /// </summary>
        internal static string TryResolveFinalPath(string directory)
        {
            using (SafeFileHandle handle = NativeMethods.CreateFile(
                directory,
                NativeMethods.FileReadAttributes,
                NativeMethods.FileShareAll,
                IntPtr.Zero,
                NativeMethods.OpenExisting,
                NativeMethods.FileFlagBackupSemantics,
                IntPtr.Zero))
            {
                if (handle.IsInvalid)
                {
                    return null;
                }
                var buffer = new StringBuilder(NativeMethods.MaxLongPath);
                uint length = NativeMethods.GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
                if (length == 0 || length >= buffer.Capacity)
                {
                    return null;
                }
                return PathUtil.StripDevicePrefix(buffer.ToString(0, (int)length));
            }
        }
    }

    internal static class NativeMethods
    {
        public const uint ProcessQueryLimitedInformation = 0x1000;
        public const uint FileReadAttributes = 0x80;
        public const uint FileShareAll = 0x7;
        public const uint OpenExisting = 3;
        public const uint FileFlagBackupSemantics = 0x02000000;
        public const int MaxLongPath = 32767;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder exeName, ref int size);

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder filePath, uint filePathLength, uint flags);
    }
}
