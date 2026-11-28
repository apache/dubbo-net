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
using System.Threading.Tasks;

namespace DubboNet.Registry
{
    // A small, injectable policy keeps retry behavior deterministic in tests and
    // prevents backend-specific error strings from becoming retry selectors.
    internal sealed class ProviderRegistryRetryPolicy
    {
        private readonly Func<TimeSpan, Task> _delayAsync;
        private readonly Func<double> _nextJitter;
        private readonly TimeSpan _baseDelay;
        private readonly TimeSpan _maxDelay;

        internal static ProviderRegistryRetryPolicy Default => new ProviderRegistryRetryPolicy();

        internal ProviderRegistryRetryPolicy(
            int maxAttempts = 3,
            TimeSpan? baseDelay = null,
            TimeSpan? maxDelay = null,
            Func<TimeSpan, Task> delayAsync = null,
            Func<double> nextJitter = null)
        {
            if (maxAttempts <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxAttempts));

            _baseDelay = baseDelay ?? TimeSpan.FromMilliseconds(100);
            _maxDelay = maxDelay ?? TimeSpan.FromSeconds(2);
            if (_baseDelay < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(baseDelay));
            if (_maxDelay < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(maxDelay));

            MaxAttempts = maxAttempts;
            _delayAsync = delayAsync ?? Task.Delay;
            _nextJitter = nextJitter ?? NextRandomJitter;
        }

        private static readonly Random RandomJitter = new Random();
        private static readonly object RandomLock = new object();

        private static double NextRandomJitter()
        {
            lock (RandomLock)
            {
                return RandomJitter.NextDouble();
            }
        }

        internal int MaxAttempts { get; }

        internal async Task DelayBeforeRetryAsync(int retryNumber)
        {
            if (retryNumber <= 0)
                throw new ArgumentOutOfRangeException(nameof(retryNumber));
            await _delayAsync(GetDelay(retryNumber)).ConfigureAwait(false);
        }

        private TimeSpan GetDelay(int retryNumber)
        {
            double exponential = _baseDelay.TotalMilliseconds;
            for (int index = 1; index < retryNumber && exponential < _maxDelay.TotalMilliseconds;
                 index++)
            {
                exponential = Math.Min(_maxDelay.TotalMilliseconds, exponential * 2d);
            }

            exponential = Math.Min(exponential, _maxDelay.TotalMilliseconds);
            double jitter = _nextJitter();
            if (double.IsNaN(jitter) || double.IsInfinity(jitter))
                jitter = 0.5;
            jitter = Math.Max(0d, Math.Min(1d, jitter));

            // ±20% jitter, centered at the deterministic exponential delay.
            double adjusted = exponential * (0.8d + (jitter * 0.4d));
            if (adjusted <= 0d)
                return TimeSpan.Zero;
            if (adjusted >= TimeSpan.MaxValue.TotalMilliseconds)
                return TimeSpan.MaxValue;
            return TimeSpan.FromMilliseconds(adjusted);
        }
    }
}
