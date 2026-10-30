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

using DubboNet.DubboService;
using DubboNet.DubboService.DataModle;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DubboNet.Clients.LoadBalancing
{
    // One reservation for one actual dispatch, not one for each public overload or discovery pass.
    internal sealed class LoadBalancedInvocation : IDisposable
    {
        internal IDubboActuatorSuite Suite { get; }
        internal RequestLoadStatistics Statistics { get; }
        private readonly Func<double> _clock;
        private readonly Action _release;
        private readonly double _started;
        private int _completed;

        internal LoadBalancedInvocation(IDubboActuatorSuite suite, RequestLoadStatistics statistics,
            Func<double> clock, Action release)
        {
            Suite = suite;
            Statistics = statistics;
            _clock = clock;
            _release = release;
            _started = clock();
            statistics.Begin();
        }

        internal async Task<DubboRequestResult> InvokeAsync(Func<Task<DubboRequestResult>> invoke)
        {
            DubboRequestResult result = null;
            try
            {
                result = await invoke().ConfigureAwait(false);
                return result;
            }
            finally { Complete(result?.QuerySuccess == true); }
        }

        internal void Complete(bool success)
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0) return;
            double finished = _clock();
            try { Statistics.End(finished, finished - _started, success); }
            finally { _release(); }
        }

        public void Dispose() => Complete(false);
    }
}
