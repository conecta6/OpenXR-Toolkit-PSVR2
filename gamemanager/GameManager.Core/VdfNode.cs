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

namespace GameManager.Core
{
    /// <summary>
    /// One key of a Valve KeyValues document: either a string value or a block of child nodes.
    /// </summary>
    public sealed class VdfNode
    {
        private static readonly VdfNode[] noChildren = new VdfNode[0];

        private readonly List<VdfNode> children;

        public VdfNode(string key, string value)
        {
            Key = key;
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public VdfNode(string key, List<VdfNode> children)
        {
            Key = key;
            this.children = children ?? throw new ArgumentNullException(nameof(children));
        }

        public string Key { get; }

        /// <summary>
        /// The string value, or null when this node is a block.
        /// </summary>
        public string Value { get; }

        public bool IsBlock
        {
            get { return children != null; }
        }

        public IReadOnlyList<VdfNode> Children
        {
            get
            {
                if (children == null)
                {
                    return noChildren;
                }
                return children;
            }
        }

        /// <summary>
        /// First child whose key matches, ignoring case, or null.
        /// </summary>
        public VdfNode Find(string key)
        {
            if (children == null)
            {
                return null;
            }
            foreach (VdfNode child in children)
            {
                if (string.Equals(child.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }
            return null;
        }

        /// <summary>
        /// String value of the first matching child, or null when it is missing or is a block.
        /// </summary>
        public string GetString(string key)
        {
            VdfNode child = Find(key);
            if (child == null || child.IsBlock)
            {
                return null;
            }
            return child.Value;
        }
    }
}
