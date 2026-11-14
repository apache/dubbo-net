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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace DubboNet.Compatibility
{
    internal static class CollectionCompatibility
    {
        internal static bool RemoveExact<TKey, TValue>(
            this ConcurrentDictionary<TKey, TValue> dictionary, KeyValuePair<TKey, TValue> entry) =>
            // ConcurrentDictionary implements pair removal atomically. A separate
            // TryGetValue/TryRemove would be able to delete a newer discovery task.
            ((ICollection<KeyValuePair<TKey, TValue>>)dictionary).Remove(entry);

#if NETSTANDARD2_0
        internal static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
        {
            // Only ordinary, already caller-synchronized dictionaries use this overload.
            if (dictionary.ContainsKey(key)) return false;
            dictionary.Add(key, value);
            return true;
        }
#endif
        internal static Dictionary<string, string> Copy(
            IReadOnlyDictionary<string, string> source) =>
            source == null ? new Dictionary<string, string>(StringComparer.Ordinal) :
            source.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
    }
}
