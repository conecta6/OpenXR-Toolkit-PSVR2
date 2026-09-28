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
using System.Security;

namespace GameManager.Core
{
    /// <summary>
    /// Walks a game's install folder and classifies it as OpenVR, probable OpenXR, or unknown.
    /// </summary>
    public static class GameClassifier
    {
        public const string OpenVrDllName = "openvr_api.dll";
        public const string OpenXrLoaderName = "openxr_loader.dll";

        public static GameClassification Classify(string installDir)
        {
            string root = PathUtil.NormalizeDirectory(installDir);
            var dlls = new List<OpenVrDll>();
            var warnings = new List<string>();
            bool hasOpenXrLoader = false;

            // The root is always walked, even when it is itself a junction (a game moved to another drive).
            var pending = new Stack<DirectoryInfo>();
            pending.Push(new DirectoryInfo(root));

            while (pending.Count > 0)
            {
                DirectoryInfo directory = pending.Pop();
                try
                {
                    foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
                    {
                        if (entry is DirectoryInfo subDirectory)
                        {
                            // Junctions and symbolic links below the root: skip, to avoid loops and leaving the game.
                            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                            {
                                warnings.Add("Skipped link: " + entry.FullName);
                            }
                            else
                            {
                                pending.Push(subDirectory);
                            }
                        }
                        else if (string.Equals(entry.Name, OpenVrDllName, StringComparison.OrdinalIgnoreCase))
                        {
                            // Reparse-point files are NOT skipped: CompactGUI / compact /exe (WOF) files are reparse points.
                            dlls.Add(new OpenVrDll(Relative(root, entry.FullName), ReadMachine(entry.FullName, warnings)));
                        }
                        else if (string.Equals(entry.Name, OpenXrLoaderName, StringComparison.OrdinalIgnoreCase))
                        {
                            hasOpenXrLoader = true;
                        }
                    }
                }
                catch (Exception e) when (e is UnauthorizedAccessException || e is IOException || e is SecurityException)
                {
                    // IOException covers PathTooLongException and DirectoryNotFoundException.
                    warnings.Add("Could not read folder " + directory.FullName + ": " + e.Message);
                }
            }

            dlls.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.RelativePath, b.RelativePath));

            GameKind kind;
            if (dlls.Count > 0)
            {
                kind = GameKind.OpenVr;
            }
            else if (hasOpenXrLoader)
            {
                kind = GameKind.OpenXrProbable;
            }
            else
            {
                kind = GameKind.Unknown;
            }
            return new GameClassification(kind, dlls, hasOpenXrLoader, warnings);
        }

        private static PeMachine ReadMachine(string path, List<string> warnings)
        {
            try
            {
                return PeReader.ReadMachine(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is SecurityException)
            {
                warnings.Add("Could not read " + path + ": " + e.Message);
                return PeMachine.Unknown;
            }
        }

        private static string Relative(string root, string fullPath)
        {
            if (fullPath.Length > root.Length && fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(root.Length).TrimStart('\\');
            }
            return fullPath;
        }
    }
}
