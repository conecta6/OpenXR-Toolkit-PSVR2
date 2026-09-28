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
using GameManager.Core;
using Microsoft.Win32;

namespace GameManager.Tests
{
    public sealed class FakeRegistryReader : IRegistryReader
    {
        private readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public void Set(RegistryHive hive, RegistryView view, string subKey, string valueName, string value)
        {
            values[MakeKey(hive, view, subKey, valueName)] = value;
        }

        public string ReadString(RegistryHive hive, RegistryView view, string subKey, string valueName)
        {
            string value;
            return values.TryGetValue(MakeKey(hive, view, subKey, valueName), out value) ? value : null;
        }

        private static string MakeKey(RegistryHive hive, RegistryView view, string subKey, string valueName)
        {
            return hive + "|" + view + "|" + subKey + "|" + valueName;
        }
    }
}
