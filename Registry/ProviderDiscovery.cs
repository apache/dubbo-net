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

using DubboNet.Clients.RegistryClient;
using MyCommonHelper;
using org.apache.zookeeper;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DubboNet.Registry
{
    // Coordinates backend-neutral snapshots and invalidation notifications.
    // Callable provider state remains exclusively owned by the service driver.
    internal sealed class ProviderDiscovery : IDisposable
    {
        private sealed class ZooKeeperReader : IProviderZooKeeper
        {
            private readonly MyZookeeper _client;
            internal ZooKeeperReader(MyZookeeper client) => _client = client;
            public bool IsConnected => _client.IsConnected;
            public Task<org.apache.zookeeper.data.Stat> ExistsAsync(string path) => _client.ExistsAsync(path);
            public async Task<IReadOnlyList<string>> GetChildrenAsync(string path, Watcher watcher) =>
                (await _client.GetChildrenAsync(path, watcher).ConfigureAwait(false))?.Children;
        }

        private readonly IProviderRegistryAdapter _adapter;
        private readonly Func<ProviderRegistryChange, Task> _onChanged;
        private readonly ConcurrentDictionary<string, Lazy<Task<ProviderRegistrySnapshot>>> _inFlight =
            new ConcurrentDictionary<string, Lazy<Task<ProviderRegistrySnapshot>>>(StringComparer.Ordinal);
        private int _disposed;

        internal ProviderDiscovery(
            MyZookeeper zookeeper,
            MyNacos nacos,
            string rootPath,
            Watcher watcher,
            Func<string, Task> onRegistryChanged,
            Action onRegistryRecovered)
        {
            if ((zookeeper is null) == (nacos is null))
                throw new ArgumentException("Exactly one Provider registry is required.");
            if (string.IsNullOrWhiteSpace(rootPath))
                throw new ArgumentException("Provider registry root cannot be empty.", nameof(rootPath));
            if (onRegistryChanged == null)
                throw new ArgumentNullException(nameof(onRegistryChanged));

            _onChanged = OnRegistryChangedAsync;
            _registryChanged = onRegistryChanged;
            if (nacos != null)
            {
                _adapter = new NacosProviderRegistry(nacos, rootPath);
            }
            else
            {
                _adapter = new ZooKeeperProviderRegistry(
                    new ZooKeeperReader(zookeeper),
                    rootPath,
                    watcher ?? throw new ArgumentNullException(nameof(watcher)),
                    onRegistryRecovered ?? throw new ArgumentNullException(nameof(onRegistryRecovered)));
            }
        }

        internal static ProviderDiscovery ForZooKeeper(
            IProviderZooKeeper zookeeper,
            string rootPath,
            Watcher watcher,
            Action onRegistryRecovered,
            Func<string, Task> onRegistryChanged = null) =>
            new ProviderDiscovery(
                new ZooKeeperProviderRegistry(
                    zookeeper,
                    rootPath,
                    watcher,
                    onRegistryRecovered),
                onRegistryChanged);

        private ProviderDiscovery(
            IProviderRegistryAdapter adapter,
            Func<string, Task> onRegistryChanged)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _onChanged = OnRegistryChangedAsync;
            _registryChanged = onRegistryChanged;
        }

        private readonly Func<string, Task> _registryChanged;

        private Task OnRegistryChangedAsync(ProviderRegistryChange change)
        {
            if (IsDisposed || _registryChanged == null)
                return Task.CompletedTask;

            if (_inFlight.TryGetValue(change.ServiceName, out Lazy<Task<ProviderRegistrySnapshot>> pending))
            {
                // A one-shot notification can race the initial read. Do not let
                // coalescing turn that invalidation into the same stale snapshot;
                // evict this exact completed lookup before dispatching the refresh.
                _ = DispatchAfterLookupAsync(pending, pending.Value, change.ServiceName);
                return Task.CompletedTask;
            }

            return DispatchRegistryChangedAsync(change.ServiceName);
        }

        private async Task DispatchAfterLookupAsync(
            Lazy<Task<ProviderRegistrySnapshot>> pending,
            Task<ProviderRegistrySnapshot> lookup,
            string serviceName)
        {
            try
            {
                await lookup.ConfigureAwait(false);
                _inFlight.RemoveExact(
                    new KeyValuePair<string, Lazy<Task<ProviderRegistrySnapshot>>>(
                        serviceName,
                        pending));
                await DispatchRegistryChangedAsync(serviceName).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception exception)
            {
                MyLogger.LogWarning(
                    $"[ProviderDiscovery] change refresh for {serviceName} failed: {exception.Message}");
            }
        }

        private async Task DispatchRegistryChangedAsync(string serviceName)
        {
            if (IsDisposed) return;
            try
            {
                await _registryChanged(serviceName).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                MyLogger.LogWarning(
                    $"[ProviderDiscovery] change callback for {serviceName} failed: {exception.Message}");
            }
        }

        internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _adapter.Dispose();
        }

        internal async Task<ProviderRegistrySnapshot> GetProviderEndPointsAsync(
            string serviceName)
        {
            ThrowIfDisposed();
            Lazy<Task<ProviderRegistrySnapshot>> pending = _inFlight.GetOrAdd(
                serviceName,
                name => new Lazy<Task<ProviderRegistrySnapshot>>(
                    () => DiscoverAsync(name),
                    LazyThreadSafetyMode.ExecutionAndPublication));
            try
            {
                ProviderRegistrySnapshot result = await pending.Value.ConfigureAwait(false);
                ThrowIfDisposed();
                return result;
            }
            finally
            {
                _inFlight.RemoveExact(
                    new KeyValuePair<string, Lazy<Task<ProviderRegistrySnapshot>>>(
                        serviceName,
                        pending));
            }
        }

        private async Task<ProviderRegistrySnapshot> DiscoverAsync(string serviceName)
        {
            if (string.IsNullOrEmpty(serviceName))
                throw new ArgumentNullException(nameof(serviceName));
            return await _adapter.GetSnapshotAsync(serviceName, _onChanged)
                .ConfigureAwait(false);
        }

        private void ThrowIfDisposed()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(ProviderDiscovery));
        }
    }
}
