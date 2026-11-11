/*
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements. See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License. You may obtain a copy of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;

namespace DubboNet.Compatibility
{
    internal static class StringCompatibility
    {
#if NETSTANDARD2_0
        internal static bool Contains(this string value, string search, StringComparison comparison) =>
            value.IndexOf(search, comparison) >= 0;
        internal static bool Contains(this string value, char search, StringComparison comparison) =>
            value.IndexOf(search.ToString(), comparison) >= 0;
        internal static string[] Split(this string value, string separator, StringSplitOptions options = StringSplitOptions.None) =>
            value.Split(new[] { separator }, options);
        internal static string[] Split(this string value, char separator, StringSplitOptions options) =>
            value.Split(new[] { separator }, options);
        internal static string[] Split(this string value, char separator, int count) =>
            value.Split(new[] { separator }, count);
#endif
        internal static string[] SplitTrimmed(string value, string separator, int count = int.MaxValue)
        {
#if NETSTANDARD2_0
            // Standard lacks TrimEntries. Trim BEFORE testing emptiness and don't
            // consume the result limit for blank entries; preserve the final remainder.
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0) return Array.Empty<string>();
            var parts = new List<string>();
            int start = 0;
            while (start <= value.Length)
            {
                if (parts.Count == count - 1)
                {
                    string remainder = value.Substring(start).Trim();
                    if (remainder.Length != 0) parts.Add(remainder);
                    break;
                }
                int end = value.IndexOf(separator, start, StringComparison.Ordinal);
                string part = (end < 0 ? value.Substring(start) : value.Substring(start, end - start)).Trim();
                if (part.Length != 0) parts.Add(part);
                if (end < 0) break;
                start = end + separator.Length;
                // Remove blank entries even when the result limit was just reached.
                if (parts.Count == count - 1)
                {
                    while (start <= value.Length)
                    {
                        int next = value.IndexOf(separator, start, StringComparison.Ordinal);
                        if (next < 0 || value.Substring(start, next - start).Trim().Length != 0) break;
                        start = next + separator.Length;
                    }
                }
            }
            return parts.ToArray();
#else
            return value.Split(new[] { separator }, count,
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
#endif
        }
    }
}
