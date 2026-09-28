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

namespace GameManager.Core
{
    public enum PeMachine
    {
        Unknown,
        X86,
        X64,
        Arm64,
    }

    /// <summary>
    /// Reads the Machine field of a PE file (DOS header, e_lfanew at 0x3C, "PE\0\0", Machine).
    /// Only the header bytes are read.
    /// </summary>
    public static class PeReader
    {
        private const int DosHeaderSize = 64;
        private const int PeOffsetField = 0x3C;
        private const int SignatureAndMachineSize = 6;

        /// <summary>
        /// Unknown for any content that is not a valid PE header. I/O errors (missing file, access denied,
        /// file locked by another process) are not hidden: they throw, so the caller can report them.
        /// </summary>
        public static PeMachine ReadMachine(string path)
        {
            // Share everything so a file that Steam or a game has open is still readable.
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                return ReadMachine(stream);
            }
        }

        private static PeMachine ReadMachine(FileStream stream)
        {
            var dosHeader = new byte[DosHeaderSize];
            if (!ReadExactly(stream, dosHeader))
            {
                return PeMachine.Unknown;
            }
            if (dosHeader[0] != 'M' || dosHeader[1] != 'Z')
            {
                return PeMachine.Unknown;
            }

            int peOffset = dosHeader[PeOffsetField]
                | dosHeader[PeOffsetField + 1] << 8
                | dosHeader[PeOffsetField + 2] << 16
                | dosHeader[PeOffsetField + 3] << 24;
            if (peOffset < 0 || (long)peOffset + SignatureAndMachineSize > stream.Length)
            {
                return PeMachine.Unknown;
            }

            stream.Seek(peOffset, SeekOrigin.Begin);
            var peHeader = new byte[SignatureAndMachineSize];
            if (!ReadExactly(stream, peHeader))
            {
                return PeMachine.Unknown;
            }
            if (peHeader[0] != 'P' || peHeader[1] != 'E' || peHeader[2] != 0 || peHeader[3] != 0)
            {
                return PeMachine.Unknown;
            }

            int machine = peHeader[4] | peHeader[5] << 8;
            switch (machine)
            {
                case 0x014C:
                    return PeMachine.X86;
                case 0x8664:
                    return PeMachine.X64;
                case 0xAA64:
                    return PeMachine.Arm64;
                default:
                    return PeMachine.Unknown;
            }
        }

        private static bool ReadExactly(Stream stream, byte[] buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = stream.Read(buffer, total, buffer.Length - total);
                if (read == 0)
                {
                    return false;
                }
                total += read;
            }
            return true;
        }
    }
}
