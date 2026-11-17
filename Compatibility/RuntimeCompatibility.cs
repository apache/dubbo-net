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

global using DubboNet.Compatibility;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace DubboNet.Compatibility
{
    // The SDK/language version is independent of the runtime API surface. Centralize
    // the small API gaps so net8/net10 retain their native implementations.
    internal static class Guard
    {
        internal static void ThrowIfNull(object value, string parameterName)
        {
            if (value == null) throw new ArgumentNullException(parameterName);
        }
    }

    internal sealed class ReferenceIdentityComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceIdentityComparer Instance = new ReferenceIdentityComparer();
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }

    internal static class RuntimeCompatibility
    {
        internal static int ProcessId
        {
            get
            {
#if NETSTANDARD2_0
                // Process is disposable even though only its ID is required.
                using (Process process = Process.GetCurrentProcess()) return process.Id;
#else
                return Environment.ProcessId;
#endif
            }
        }

        internal static long TickCount64
        {
            get
            {
#if NETSTANDARD2_0
                // Monotonic elapsed time, not wall time or the wrapping 32-bit TickCount.
                return (long)(Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency));
#else
                return Environment.TickCount64;
#endif
            }
        }

        internal static int ClampToInt(long value) =>
            (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, value));

#if NETSTANDARD2_0
        private sealed class RandomState
        {
            internal readonly Random Random = new Random(Interlocked.Increment(ref _seed));
            internal readonly byte[] Bytes = new byte[8];
        }
        private static int _seed = Environment.TickCount;
        private static readonly ThreadLocal<RandomState> RandomStates =
            new ThreadLocal<RandomState>(() => new RandomState());
#endif

        internal static long NextInt64(long maximum)
        {
#if NETSTANDARD2_0
            if (maximum < 0) throw new ArgumentOutOfRangeException(nameof(maximum));
            if (maximum == 0) return 0;
            // Weighted load balancing can exceed Int32.MaxValue. Thread-local state
            // avoids racing Random, and rejection sampling avoids modulo/double bias.
            RandomState state = RandomStates.Value;
            ulong bound = (ulong)maximum;
            ulong threshold = unchecked(0UL - bound) % bound;
            ulong sample;
            do
            {
                state.Random.NextBytes(state.Bytes);
                sample = BitConverter.ToUInt64(state.Bytes, 0);
            } while (sample < threshold);
            return (long)(sample % bound);
#else
            return Random.Shared.NextInt64(maximum);
#endif
        }
    }
}
