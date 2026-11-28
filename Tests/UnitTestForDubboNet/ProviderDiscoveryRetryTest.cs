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

using DubboNet.Clients.DataModle;
using DubboNet.Registry;

namespace UnitTestForDubboNet;

public sealed class ProviderDiscoveryRetryTest
{
    private const string ServiceName = "com.example.Service";

    private sealed class ScriptedAdapter : IProviderRegistryAdapter
    {
        private readonly Func<int, ProviderRegistrySnapshot> _responseFactory;

        internal ScriptedAdapter(Func<int, ProviderRegistrySnapshot> responseFactory) =>
            _responseFactory = responseFactory;

        internal int Calls { get; private set; }
        public void Dispose() { }

        public Task<ProviderRegistrySnapshot> GetSnapshotAsync(
            string serviceName,
            Func<ProviderRegistryChange, Task> onChanged)
        {
            Calls++;
            return Task.FromResult(_responseFactory(Calls));
        }
    }

    [Fact]
    public async Task TransientFailuresRetryWithBoundedExponentialJitterBeforeSuccess()
    {
        var adapter = new ScriptedAdapter(attempt => attempt < 3
            ? Snapshot(ProviderRegistrySnapshotState.Unavailable, "temporary outage")
            : Snapshot(ProviderRegistrySnapshotState.Available));
        var delays = new List<TimeSpan>();
        double[] jitterValues = { 0.0, 0.999 };
        int jitterCalls = 0;
        var policy = new ProviderRegistryRetryPolicy(
            maxAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(100),
            maxDelay: TimeSpan.FromMilliseconds(250),
            delayAsync: delay =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
            nextJitter: () => jitterValues[Math.Min(jitterCalls++, jitterValues.Length - 1)]);
        using var discovery = ProviderDiscovery.ForAdapter(adapter, _ => Task.CompletedTask, policy);

        ProviderRegistrySnapshot result = await discovery.GetProviderEndPointsAsync(ServiceName);

        Assert.Equal(ProviderRegistrySnapshotState.Available, result.State);
        Assert.Equal(3, adapter.Calls);
        Assert.Equal(2, delays.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(80), delays[0]);
        Assert.InRange(delays[1], TimeSpan.FromMilliseconds(239), TimeSpan.FromMilliseconds(240));
    }

    [Fact]
    public async Task EmptyAndPermanentErrorsAreSentinelsNotRetrySignals()
    {
        foreach (ProviderRegistrySnapshotState state in new[]
                 {
                     ProviderRegistrySnapshotState.Empty,
                     ProviderRegistrySnapshotState.InvalidRequest,
                     ProviderRegistrySnapshotState.PermissionDenied
                 })
        {
            var adapter = new ScriptedAdapter(_ => Snapshot(state, "diagnostic detail"));
            int delayCount = 0;
            var policy = new ProviderRegistryRetryPolicy(
                maxAttempts: 4,
                delayAsync: _ =>
                {
                    delayCount++;
                    return Task.CompletedTask;
                },
                nextJitter: () => 0.5);
            using var discovery = ProviderDiscovery.ForAdapter(adapter, _ => Task.CompletedTask, policy);

            ProviderRegistrySnapshot result = await discovery.GetProviderEndPointsAsync(ServiceName);

            Assert.Equal(state, result.State);
            Assert.True(result.IsUsable == (state == ProviderRegistrySnapshotState.Empty));
            Assert.False(result.IsRetryable);
            Assert.Equal(1, adapter.Calls);
            Assert.Equal(0, delayCount);
        }
    }

    [Fact]
    public async Task ExhaustedTransientRetriesReturnTheLastUnavailableSnapshot()
    {
        var adapter = new ScriptedAdapter(attempt =>
            Snapshot(ProviderRegistrySnapshotState.Unavailable, $"outage {attempt}"));
        var policy = new ProviderRegistryRetryPolicy(
            maxAttempts: 3,
            delayAsync: _ => Task.CompletedTask,
            nextJitter: () => 0.5);
        using var discovery = ProviderDiscovery.ForAdapter(adapter, _ => Task.CompletedTask, policy);

        ProviderRegistrySnapshot result = await discovery.GetProviderEndPointsAsync(ServiceName);

        Assert.Equal(ProviderRegistrySnapshotState.Unavailable, result.State);
        Assert.Equal("outage 3", result.ErrorInfo);
        Assert.True(result.IsRetryable);
        Assert.False(result.IsUsable);
        Assert.Equal(3, adapter.Calls);
    }

    [Fact]
    public async Task DisposalDuringBackoffStopsFurtherRegistryReads()
    {
        var adapter = new ScriptedAdapter(_ =>
            Snapshot(ProviderRegistrySnapshotState.Unavailable, "temporary outage"));
        ProviderDiscovery? discovery = null;
        var policy = new ProviderRegistryRetryPolicy(
            maxAttempts: 3,
            delayAsync: _ =>
            {
                discovery!.Dispose();
                return Task.CompletedTask;
            },
            nextJitter: () => 0.5);
        discovery = ProviderDiscovery.ForAdapter(adapter, _ => Task.CompletedTask, policy);

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => discovery.GetProviderEndPointsAsync(ServiceName));

        Assert.Equal(1, adapter.Calls);
    }

    private static ProviderRegistrySnapshot Snapshot(
        ProviderRegistrySnapshotState state,
        string? errorInfo = null) =>
        new(ServiceName, Array.Empty<DubboServiceEndPointInfo>(), state, errorInfo);
}
