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
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    [TestClass]
    public class PeReaderTests
    {
        private TempDir temp;

        [TestInitialize]
        public void SetUp()
        {
            temp = new TempDir();
        }

        [TestCleanup]
        public void TearDown()
        {
            temp.Dispose();
        }

        private PeMachine ReadBytes(byte[] bytes)
        {
            string path = temp.WriteBytes("test.dll", bytes);
            return PeReader.ReadMachine(path);
        }

        [TestMethod]
        public void ReadMachine_X86()
        {
            Assert.AreEqual(PeMachine.X86, ReadBytes(PeFixture.Build(PeFixture.MachineX86)));
        }

        [TestMethod]
        public void ReadMachine_X64()
        {
            Assert.AreEqual(PeMachine.X64, ReadBytes(PeFixture.Build(PeFixture.MachineX64)));
        }

        [TestMethod]
        public void ReadMachine_Arm64()
        {
            Assert.AreEqual(PeMachine.Arm64, ReadBytes(PeFixture.Build(PeFixture.MachineArm64)));
        }

        [TestMethod]
        public void ReadMachine_OtherMachine_ReturnsUnknown()
        {
            Assert.AreEqual(PeMachine.Unknown, ReadBytes(PeFixture.Build(0x01C4)));
        }

        [TestMethod]
        public void ReadMachine_NotMz_ReturnsUnknown()
        {
            byte[] bytes = PeFixture.Build(PeFixture.MachineX64);
            bytes[0] = (byte)'Z';
            bytes[1] = (byte)'M';

            Assert.AreEqual(PeMachine.Unknown, ReadBytes(bytes));
        }

        [TestMethod]
        public void ReadMachine_EmptyFile_ReturnsUnknown()
        {
            Assert.AreEqual(PeMachine.Unknown, ReadBytes(new byte[0]));
        }

        [TestMethod]
        public void ReadMachine_TruncatedDosHeader_ReturnsUnknown()
        {
            byte[] full = PeFixture.Build(PeFixture.MachineX64);
            var truncated = new byte[30];
            Array.Copy(full, truncated, truncated.Length);

            Assert.AreEqual(PeMachine.Unknown, ReadBytes(truncated));
        }

        [TestMethod]
        public void ReadMachine_PeOffsetBeyondEndOfFile_ReturnsUnknown()
        {
            byte[] bytes = PeFixture.Build(PeFixture.MachineX64);
            PeFixture.WriteInt32(bytes, 0x3C, 0x10000);

            Assert.AreEqual(PeMachine.Unknown, ReadBytes(bytes));
        }

        [TestMethod]
        public void ReadMachine_NegativePeOffset_ReturnsUnknown()
        {
            byte[] bytes = PeFixture.Build(PeFixture.MachineX64);
            PeFixture.WriteInt32(bytes, 0x3C, -8);

            Assert.AreEqual(PeMachine.Unknown, ReadBytes(bytes));
        }

        [TestMethod]
        public void ReadMachine_BadPeSignature_ReturnsUnknown()
        {
            byte[] bytes = PeFixture.Build(PeFixture.MachineX64);
            bytes[0x80] = (byte)'N';

            Assert.AreEqual(PeMachine.Unknown, ReadBytes(bytes));
        }

        [TestMethod]
        public void ReadMachine_TruncatedInsideMachineField_ReturnsUnknown()
        {
            byte[] full = PeFixture.Build(PeFixture.MachineX64);
            var truncated = new byte[0x80 + 5];
            Array.Copy(full, truncated, truncated.Length);

            Assert.AreEqual(PeMachine.Unknown, ReadBytes(truncated));
        }

        [TestMethod]
        public void ReadMachine_FileOpenForWritingElsewhere_IsStillRead()
        {
            string path = temp.WriteBytes("busy.dll", PeFixture.Build(PeFixture.MachineX64));

            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
            {
                Assert.AreEqual(PeMachine.X64, PeReader.ReadMachine(path));
            }
        }

        [TestMethod]
        public void ReadMachine_MissingFile_ThrowsFileNotFound()
        {
            Assert.ThrowsException<FileNotFoundException>(() => PeReader.ReadMachine(temp.PathOf("missing.dll")));
        }
    }
}
