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
using GameManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameManager.Tests
{
    [TestClass]
    public class OperationGateTests
    {
        [TestMethod]
        public void TryEnter_WhenFree_ReturnsLeaseAndNamesTheOperation()
        {
            var gate = new OperationGate();

            IDisposable lease = gate.TryEnter("Patch");

            Assert.IsNotNull(lease);
            Assert.IsTrue(gate.IsBusy);
            Assert.AreEqual("Patch", gate.CurrentOperation);
        }

        [TestMethod]
        public void TryEnter_WhenBusy_ReturnsNullAndKeepsTheFirstOperation()
        {
            var gate = new OperationGate();
            gate.TryEnter("Patch");

            Assert.IsNull(gate.TryEnter("OpenComposite download or check"));
            Assert.AreEqual("Patch", gate.CurrentOperation);
        }

        [TestMethod]
        public void Dispose_FreesTheGate_AndASecondDisposeDoesNotFreeALaterOperation()
        {
            var gate = new OperationGate();
            IDisposable first = gate.TryEnter("Patch");
            first.Dispose();
            Assert.IsFalse(gate.IsBusy);

            IDisposable second = gate.TryEnter("Restore");
            first.Dispose();

            Assert.IsTrue(gate.IsBusy);
            Assert.AreEqual("Restore", gate.CurrentOperation);
            second.Dispose();
            Assert.IsFalse(gate.IsBusy);
            Assert.IsNull(gate.CurrentOperation);
        }

        [TestMethod]
        public void Changed_IsRaisedOnEnterAndOnRelease_NotOnARefusedEnter()
        {
            var gate = new OperationGate();
            int raised = 0;
            gate.Changed += (sender, e) => raised++;

            IDisposable lease = gate.TryEnter("Patch");
            Assert.IsNull(gate.TryEnter("Restore"));
            lease.Dispose();

            Assert.AreEqual(2, raised);
        }
    }
}
