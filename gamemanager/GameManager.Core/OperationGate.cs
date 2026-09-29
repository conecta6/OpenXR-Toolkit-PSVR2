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
using System.Threading;

namespace GameManager.Core
{
    /// <summary>
    /// R33: one operation at a time across the whole app — patch, restore, re-patch all, update all, and the
    /// OpenComposite download, check and accept — so the cached DLL can never change while it is being copied
    /// into a game. Front ends take a lease before starting (planning included) and dispose it when done.
    /// </summary>
    public sealed class OperationGate
    {
        private readonly object sync = new object();
        private string current;

        /// <summary>
        /// Raised after the gate is taken or released, on the thread that did it.
        /// </summary>
        public event EventHandler Changed;

        public bool IsBusy
        {
            get
            {
                lock (sync)
                {
                    return current != null;
                }
            }
        }

        /// <summary>
        /// The name the running operation passed to TryEnter, or null when none is running.
        /// </summary>
        public string CurrentOperation
        {
            get
            {
                lock (sync)
                {
                    return current;
                }
            }
        }

        /// <summary>
        /// A lease to dispose when the operation ends, or null when another operation is running.
        /// </summary>
        public IDisposable TryEnter(string operation)
        {
            if (string.IsNullOrWhiteSpace(operation))
            {
                throw new ArgumentException("Operation name is empty.", nameof(operation));
            }
            lock (sync)
            {
                if (current != null)
                {
                    return null;
                }
                current = operation;
            }
            Changed?.Invoke(this, EventArgs.Empty);
            return new Lease(this);
        }

        private void Release()
        {
            lock (sync)
            {
                current = null;
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private sealed class Lease : IDisposable
        {
            private OperationGate gate;

            public Lease(OperationGate gate)
            {
                this.gate = gate;
            }

            public void Dispose()
            {
                // A second Dispose must not release an operation that started after the first one.
                OperationGate owner = Interlocked.Exchange(ref gate, null);
                owner?.Release();
            }
        }
    }
}
