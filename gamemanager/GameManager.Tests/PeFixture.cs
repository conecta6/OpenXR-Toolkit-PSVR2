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

namespace GameManager.Tests
{
    /// <summary>
    /// Builds the smallest file PeReader accepts: "MZ" at 0, e_lfanew at 0x3C, "PE\0\0" at e_lfanew,
    /// then the 16-bit Machine field. Zero-padded up to totalLength.
    /// </summary>
    public static class PeFixture
    {
        public const ushort MachineX86 = 0x014C;
        public const ushort MachineX64 = 0x8664;
        public const ushort MachineArm64 = 0xAA64;

        public static byte[] Build(ushort machine, int peOffset = 0x80, int totalLength = 0)
        {
            var bytes = new byte[Math.Max(peOffset + 24, totalLength)];
            bytes[0] = (byte)'M';
            bytes[1] = (byte)'Z';
            WriteInt32(bytes, 0x3C, peOffset);
            bytes[peOffset] = (byte)'P';
            bytes[peOffset + 1] = (byte)'E';
            bytes[peOffset + 4] = (byte)(machine & 0xFF);
            bytes[peOffset + 5] = (byte)(machine >> 8);
            return bytes;
        }

        public static void WriteInt32(byte[] bytes, int offset, int value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }
    }
}
