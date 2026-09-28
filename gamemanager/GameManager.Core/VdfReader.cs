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
using System.Text;

namespace GameManager.Core
{
    /// <summary>
    /// Parses Valve KeyValues text (.vdf, .acf): quoted or unquoted keys and values, nested { } blocks,
    /// // line comments, and the escapes \\ \" \n \t inside quotes. Unknown escapes are kept as written.
    /// </summary>
    public static class VdfReader
    {
        // Real files nest 3 or 4 levels. The limit stops a corrupt file from overflowing the stack,
        // which would kill the process instead of raising a catchable exception.
        private const int MaxDepth = 64;

        public static VdfNode ParseFile(string path)
        {
            // UTF8 decoding also honours a byte order mark if the file has one.
            string text = File.ReadAllText(path, Encoding.UTF8);
            return Parse(text);
        }

        public static VdfNode Parse(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }
            var tokenizer = new Tokenizer(text);
            return new VdfNode("", ParseBlock(tokenizer, 0));
        }

        private static List<VdfNode> ParseBlock(Tokenizer tokenizer, int depth)
        {
            var nodes = new List<VdfNode>();
            while (true)
            {
                Token key = tokenizer.Next();
                switch (key.Kind)
                {
                    case TokenKind.End:
                        if (depth > 0)
                        {
                            throw new VdfFormatException("Missing closing '}'.", key.Line);
                        }
                        return nodes;
                    case TokenKind.CloseBrace:
                        if (depth == 0)
                        {
                            throw new VdfFormatException("Unexpected '}'.", key.Line);
                        }
                        return nodes;
                    case TokenKind.OpenBrace:
                        throw new VdfFormatException("Expected a key but found '{'.", key.Line);
                }

                Token value = tokenizer.Next();
                if (value.Kind == TokenKind.String)
                {
                    nodes.Add(new VdfNode(key.Text, value.Text));
                }
                else if (value.Kind == TokenKind.OpenBrace)
                {
                    if (depth + 1 > MaxDepth)
                    {
                        throw new VdfFormatException("Blocks are nested too deeply.", value.Line);
                    }
                    nodes.Add(new VdfNode(key.Text, ParseBlock(tokenizer, depth + 1)));
                }
                else
                {
                    throw new VdfFormatException("Missing value for key \"" + key.Text + "\".", value.Line);
                }
            }
        }

        private enum TokenKind
        {
            String,
            OpenBrace,
            CloseBrace,
            End,
        }

        private struct Token
        {
            public Token(TokenKind kind, string text, int line)
            {
                Kind = kind;
                Text = text;
                Line = line;
            }

            public TokenKind Kind { get; }
            public string Text { get; }
            public int Line { get; }
        }

        private sealed class Tokenizer
        {
            private readonly string text;
            private int position;
            private int line = 1;

            public Tokenizer(string text)
            {
                this.text = text;
            }

            public Token Next()
            {
                SkipWhitespaceAndComments();
                if (position >= text.Length)
                {
                    return new Token(TokenKind.End, null, line);
                }

                char c = text[position];
                if (c == '{')
                {
                    position++;
                    return new Token(TokenKind.OpenBrace, null, line);
                }
                if (c == '}')
                {
                    position++;
                    return new Token(TokenKind.CloseBrace, null, line);
                }
                if (c == '"')
                {
                    return ReadQuoted();
                }
                return ReadUnquoted();
            }

            private void SkipWhitespaceAndComments()
            {
                while (position < text.Length)
                {
                    char c = text[position];
                    if (c == '\n')
                    {
                        line++;
                        position++;
                    }
                    else if (char.IsWhiteSpace(c))
                    {
                        position++;
                    }
                    else if (c == '/' && position + 1 < text.Length && text[position + 1] == '/')
                    {
                        while (position < text.Length && text[position] != '\n')
                        {
                            position++;
                        }
                    }
                    else
                    {
                        return;
                    }
                }
            }

            private Token ReadQuoted()
            {
                int startLine = line;
                position++; // Opening quote.
                var value = new StringBuilder();
                while (position < text.Length)
                {
                    char c = text[position++];
                    if (c == '"')
                    {
                        return new Token(TokenKind.String, value.ToString(), startLine);
                    }
                    if (c == '\\' && position < text.Length)
                    {
                        char escaped = text[position++];
                        switch (escaped)
                        {
                            case '\\':
                                value.Append('\\');
                                break;
                            case '"':
                                value.Append('"');
                                break;
                            case 'n':
                                value.Append('\n');
                                break;
                            case 't':
                                value.Append('\t');
                                break;
                            default:
                                // Not a known escape (for example a single "\" in "D:\Games"): keep it as written.
                                value.Append('\\').Append(escaped);
                                if (escaped == '\n')
                                {
                                    line++;
                                }
                                break;
                        }
                        continue;
                    }
                    if (c == '\n')
                    {
                        line++;
                    }
                    value.Append(c);
                }
                throw new VdfFormatException("Unterminated quoted string.", startLine);
            }

            private Token ReadUnquoted()
            {
                int start = position;
                while (position < text.Length)
                {
                    char c = text[position];
                    if (char.IsWhiteSpace(c) || c == '{' || c == '}' || c == '"')
                    {
                        break;
                    }
                    position++;
                }
                return new Token(TokenKind.String, text.Substring(start, position - start), line);
            }
        }
    }
}
