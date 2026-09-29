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
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace GameManager.Core
{
    /// <summary>
    /// JSON through the BCL DataContractJsonSerializer, so the shipped app needs no JSON package.
    /// </summary>
    internal static class JsonFile
    {
        private static readonly DataContractJsonSerializerSettings SerializerSettings = new DataContractJsonSerializerSettings
        {
            // Dictionaries as plain JSON objects ({"x64": {...}}) instead of [{"Key": ..., "Value": ...}].
            UseSimpleDictionaryFormat = true,
        };

        /// <summary>
        /// Reads a JSON file saved as UTF-8 or UTF-16, with or without a byte order mark.
        /// I/O errors and the errors IsCorruptDataError recognizes pass through.
        /// </summary>
        public static T Read<T>(string path) where T : class
        {
            // File.ReadAllText honours byte order marks. The serializer itself throws on a UTF-8 BOM, which
            // Notepad may write, so the text is decoded here and handed over as BOM-less UTF-8.
            return Parse<T>(File.ReadAllText(path));
        }

        /// <summary>
        /// The object, or null for the JSON literal null.
        /// </summary>
        public static T Parse<T>(string text) where T : class
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(text.TrimStart('\uFEFF'));
            var serializer = new DataContractJsonSerializer(typeof(T), SerializerSettings);
            using (var stream = new MemoryStream(bytes))
            {
                return serializer.ReadObject(stream) as T;
            }
        }

        /// <summary>
        /// True for what the serializer throws on text that is not JSON or does not fit the type
        /// (for example a string where a number is expected).
        /// </summary>
        public static bool IsCorruptDataError(Exception e)
        {
            return e is SerializationException || e is XmlException;
        }

        /// <summary>
        /// Writes indented UTF-8 JSON to a temporary file next to path, flushes it to disk, then swaps it in
        /// (AtomicFile.Replace). Creates the folder. I/O and permission errors pass through.
        /// </summary>
        public static void WriteAtomic<T>(string path, T value) where T : class
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(T), SerializerSettings);
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (XmlDictionaryWriter writer = JsonReaderWriterFactory.CreateJsonWriter(stream, new UTF8Encoding(false), false, true, "  "))
                    {
                        serializer.WriteObject(writer, value);
                    }
                    stream.Flush(true);
                }
                AtomicFile.Replace(temp, path);
            }
            finally
            {
                AtomicFile.TryDelete(temp);
            }
        }

        private const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

        /// <summary>
        /// "2026-09-28T10:00:00Z", or null for null.
        /// </summary>
        public static string FormatUtc(DateTime? value)
        {
            return value?.ToUniversalTime().ToString(UtcFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The UTC time written by FormatUtc, or null when text is missing or not in that format.
        /// </summary>
        public static DateTime? ParseUtc(string text)
        {
            DateTime parsed;
            if (DateTime.TryParseExact(text, UtcFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
            {
                return parsed;
            }
            return null;
        }
    }
}
