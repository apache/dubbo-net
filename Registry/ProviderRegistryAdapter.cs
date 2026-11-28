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
using DubboNet.Clients.RegistryClient;
using MyCommonHelper;
using Nacos.V2.Exceptions;
using org.apache.zookeeper;
using org.apache.zookeeper.data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace DubboNet.Registry
{
    internal interface IProviderRegistryAdapter : IDisposable
    {
        Task<ProviderRegistrySnapshot> GetSnapshotAsync(
            string serviceName,
            Func<ProviderRegistryChange, Task> onChanged);
    }

    internal enum ProviderRegistrySnapshotState
    {
        Available,
        Empty,
        Unavailable,
        InvalidRequest,
        PermissionDenied
    }

    internal enum ProviderRegistryChangeKind
    {
        SnapshotInvalidated
    }

    internal sealed class ProviderRegistryChange
    {
        internal ProviderRegistryChange(
            string serviceName,
            ProviderRegistryChangeKind kind = ProviderRegistryChangeKind.SnapshotInvalidated,
            long? revision = null)
        {
            ServiceName = serviceName;
            Kind = kind;
            Revision = revision;
        }

        internal string ServiceName { get; }
        internal ProviderRegistryChangeKind Kind { get; }
        internal long? Revision { get; }
    }

    internal sealed class ProviderRegistrySnapshot
    {
        internal ProviderRegistrySnapshot(
            string serviceName,
            IReadOnlyList<DubboServiceEndPointInfo> endPoints,
            ProviderRegistrySnapshotState state,
            string errorInfo = null,
            long? revision = null)
        {
            ServiceName = serviceName;
            EndPoints = endPoints ?? Array.Empty<DubboServiceEndPointInfo>();
            State = state;
            ErrorInfo = errorInfo;
            Revision = revision;
        }

        internal string ServiceName { get; }
        internal IReadOnlyList<DubboServiceEndPointInfo> EndPoints { get; }
        internal ProviderRegistrySnapshotState State { get; }
        internal string ErrorInfo { get; }
        internal long? Revision { get; }
    }

    // Narrow ZooKeeper read surface. The adapter owns URL normalization and
    // converts one-shot child watches into backend-neutral invalidations.
    internal interface IProviderZooKeeper
    {
        bool IsConnected { get; }
        Task<Stat> ExistsAsync(string path);
        Task<IReadOnlyList<string>> GetChildrenAsync(string path, Watcher watcher);
    }

    internal interface IProviderChangeWatch
    {
        Task NotifyChangedAsync(string path);
    }

    internal sealed class ZooKeeperProviderRegistry : Watcher, IProviderRegistryAdapter, IProviderChangeWatch
    {
        private sealed class WatchRegistration
        {
            internal string ServiceName { get; set; }
            internal Func<ProviderRegistryChange, Task> Callback { get; set; }
        }

        private readonly IProviderZooKeeper _zookeeper;
        private readonly string _rootPath;
        private readonly Watcher _connectionWatcher;
        private readonly Action _onRegistryRecovered;
        private readonly object _syncRoot = new object();
        private readonly Dictionary<string, WatchRegistration> _watchers =
            new Dictionary<string, WatchRegistration>(StringComparer.Ordinal);
        private bool _disposed;

        internal ZooKeeperProviderRegistry(
            IProviderZooKeeper zookeeper,
            string rootPath,
            Watcher connectionWatcher,
            Action onRegistryRecovered)
        {
            _zookeeper = zookeeper ?? throw new ArgumentNullException(nameof(zookeeper));
            _rootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
            _connectionWatcher = connectionWatcher ?? throw new ArgumentNullException(nameof(connectionWatcher));
            _onRegistryRecovered = onRegistryRecovered ?? throw new ArgumentNullException(nameof(onRegistryRecovered));
        }

        public override Task process(WatchedEvent @event)
        {
            if (@event.getState() == Event.KeeperState.SyncConnected
                && (@event.get_Type() == Event.EventType.NodeChildrenChanged
                    || @event.get_Type() == Event.EventType.NodeDeleted))
            {
                return NotifyChangedAsync(@event.getPath());
            }
            return _connectionWatcher.process(@event);
        }

        public async Task NotifyChangedAsync(string path)
        {
            WatchRegistration registration;
            lock (_syncRoot)
            {
                if (_disposed || string.IsNullOrEmpty(path)
                    || !_watchers.TryGetValue(path, out registration))
                    return;
            }
            await registration.Callback(
                new ProviderRegistryChange(registration.ServiceName)).ConfigureAwait(false);
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_disposed) return;
                _disposed = true;
                _watchers.Clear();
            }
        }

        public async Task<ProviderRegistrySnapshot> GetSnapshotAsync(
            string serviceName,
            Func<ProviderRegistryChange, Task> onChanged)
        {
            ThrowIfDisposed();
            if (onChanged == null) throw new ArgumentNullException(nameof(onChanged));
            if (string.IsNullOrWhiteSpace(serviceName))
                return Failure(serviceName, ProviderRegistrySnapshotState.InvalidRequest,
                    "Provider service name cannot be empty.");

            string fullPath = $"{_rootPath}{serviceName}{ZooKeeperRegistryPaths.ProvidersSuffix}";
            string servicePathForLookup = $"{_rootPath}{serviceName}";
            bool previouslyWatched = IsWatched(fullPath) || IsWatched(servicePathForLookup);
            try
            {
                Stat stat = await _zookeeper.ExistsAsync(fullPath).ConfigureAwait(false);
                if (stat == null)
                {
                    if (!_zookeeper.IsConnected)
                        return Failure(serviceName, ProviderRegistrySnapshotState.Unavailable,
                            $"can not get [{serviceName}] that _zookeeper can not connect");

                    bool serviceExists =
                        await _zookeeper.ExistsAsync(servicePathForLookup).ConfigureAwait(false) != null;
                    if (!serviceExists)
                    {
                        return previouslyWatched
                            ? Failure(serviceName, ProviderRegistrySnapshotState.Empty,
                                $"no provider in [{serviceName}]")
                            : Failure(serviceName, ProviderRegistrySnapshotState.InvalidRequest,
                                $"serviceName [{serviceName}] is error");
                    }

                    // Keep a watch on the service node while the providers node is
                    // absent, so a later providers-node creation invalidates the
                    // empty snapshot. The child watch is one-shot in ZooKeeper.
                    RegisterWatch(servicePathForLookup, serviceName, onChanged);
                    IReadOnlyList<string> serviceChildren = await _zookeeper
                        .GetChildrenAsync(servicePathForLookup, this).ConfigureAwait(false);
                    if (serviceChildren == null)
                        return Failure(serviceName, ProviderRegistrySnapshotState.Unavailable,
                            $"GetChildrenAsync error [{serviceName}]");

                    // The providers node may have been created between Exists and
                    // GetChildren. Re-check it if the atomic child read observed it.
                    if (!serviceChildren.Contains(RegistryCategories.Providers))
                        return Failure(serviceName, ProviderRegistrySnapshotState.Empty,
                            $"no provider in [{serviceName}]");

                    stat = await _zookeeper.ExistsAsync(fullPath).ConfigureAwait(false);
                    if (stat == null)
                        return Failure(serviceName, ProviderRegistrySnapshotState.Empty,
                            $"no provider in [{serviceName}]");
                }

                if (stat.getNumChildren() > 0)
                    _onRegistryRecovered();
                RegisterWatch(fullPath, serviceName, onChanged);
                IReadOnlyList<string> children =
                    await _zookeeper.GetChildrenAsync(fullPath, this).ConfigureAwait(false);
                if (children == null)
                    return Failure(serviceName, ProviderRegistrySnapshotState.Unavailable,
                        $"GetChildrenAsync error [{serviceName}]");

                List<DubboServiceEndPointInfo> endPoints = new List<DubboServiceEndPointInfo>();
                foreach (string child in children)
                {
                    if (!child.StartsWith("dubbo%3A%2F%2F", StringComparison.Ordinal)
                        && !child.StartsWith("tri%3A%2F%2F", StringComparison.Ordinal))
                    {
                        MyLogger.LogWarning($"[GetSeviceProviderEndPoints] childrenResult.Children formate error {child}");
                        continue;
                    }

                    string providerUrl = System.Web.HttpUtility.UrlDecode(child, System.Text.Encoding.UTF8);
                    if (!Uri.TryCreate(providerUrl, UriKind.Absolute, out Uri endpointUri))
                    {
                        MyLogger.LogWarning($"[GetSeviceProviderEndPoints] Uri.TryCreate error {child}");
                        continue;
                    }
                    if (!IPAddress.TryParse(endpointUri.Host, out _))
                    {
                        MyLogger.LogWarning($"[GetSeviceProviderEndPoints] IPAddress.TryParse error {child}");
                        continue;
                    }
                    endPoints.Add(DubboServiceEndPointInfo.GetDubboServiceEndPointInfo(endpointUri));
                }

                return new ProviderRegistrySnapshot(
                    serviceName,
                    endPoints,
                    endPoints.Count == 0
                        ? ProviderRegistrySnapshotState.Empty
                        : ProviderRegistrySnapshotState.Available,
                    endPoints.Count == 0 && children.Count == 0
                        ? $"no provider endpoint in [{serviceName}]"
                        : null);
            }
            catch (KeeperException.BadArgumentsException exception)
            {
                return Failure(serviceName, ProviderRegistrySnapshotState.InvalidRequest,
                    $"can not get [{serviceName}] from ZooKeeper: {exception.Message}");
            }
            catch (KeeperException.NoAuthException exception)
            {
                return Failure(serviceName, ProviderRegistrySnapshotState.PermissionDenied,
                    $"can not get [{serviceName}] from ZooKeeper: {exception.Message}");
            }
            catch (KeeperException.AuthFailedException exception)
            {
                return Failure(serviceName, ProviderRegistrySnapshotState.PermissionDenied,
                    $"can not get [{serviceName}] from ZooKeeper: {exception.Message}");
            }
            catch (Exception exception)
            {
                return Failure(serviceName, ProviderRegistrySnapshotState.Unavailable,
                    $"can not get [{serviceName}] from ZooKeeper: {exception.Message}");
            }
        }

        private void RegisterWatch(
            string path,
            string serviceName,
            Func<ProviderRegistryChange, Task> callback)
        {
            lock (_syncRoot)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(ZooKeeperProviderRegistry));
                _watchers[path] = new WatchRegistration
                {
                    ServiceName = serviceName,
                    Callback = callback
                };
            }
        }

        private bool IsWatched(string path)
        {
            lock (_syncRoot)
            {
                return _watchers.ContainsKey(path);
            }
        }

        private void ThrowIfDisposed()
        {
            lock (_syncRoot)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(ZooKeeperProviderRegistry));
            }
        }

        private static ProviderRegistrySnapshot Failure(
            string serviceName,
            ProviderRegistrySnapshotState state,
            string errorInfo) =>
            new ProviderRegistrySnapshot(
                serviceName,
                Array.Empty<DubboServiceEndPointInfo>(),
                state,
                errorInfo);
    }

    internal sealed class NacosProviderRegistry : IProviderRegistryAdapter
    {
        private readonly MyNacos _nacos;
        private readonly string _group;
        private volatile bool _disposed;

        internal NacosProviderRegistry(MyNacos nacos, string group)
        {
            _nacos = nacos ?? throw new ArgumentNullException(nameof(nacos));
            _group = group ?? throw new ArgumentNullException(nameof(group));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _nacos.RemoveSubscriber(this);
        }

        public async Task<ProviderRegistrySnapshot> GetSnapshotAsync(
            string serviceName,
            Func<ProviderRegistryChange, Task> onChanged)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NacosProviderRegistry));
            if (onChanged == null) throw new ArgumentNullException(nameof(onChanged));
            try
            {
                IReadOnlyList<DubboServiceEndPointInfo> endPoints =
                    await _nacos.GetProviderEndPointsAsync(
                        serviceName,
                        _group,
                        this,
                        service => onChanged(new ProviderRegistryChange(service))).ConfigureAwait(false);
                if (endPoints == null || endPoints.Count == 0)
                    return Failure(serviceName, ProviderRegistrySnapshotState.Empty,
                        $"no healthy provider endpoint for [{serviceName}] in Nacos group " +
                        $"[{MyNacos.NormalizeGroup(_group, NacosRegistryDefaults.DefaultGroup)}]");
                return new ProviderRegistrySnapshot(
                    serviceName,
                    endPoints,
                    ProviderRegistrySnapshotState.Available);
            }
            catch (NacosException exception)
            {
                ProviderRegistrySnapshotState state = exception.ErrorCode == NacosException.NO_RIGHT
                    ? ProviderRegistrySnapshotState.PermissionDenied
                    : exception.ErrorCode == NacosException.INVALID_PARAM
                        || exception.ErrorCode == NacosException.CLIENT_INVALID_PARAM
                        ? ProviderRegistrySnapshotState.InvalidRequest
                        : ProviderRegistrySnapshotState.Unavailable;
                return Failure(serviceName, state,
                    $"can not get [{serviceName}] from Nacos: {exception.Message}");
            }
            catch (Exception exception)
            {
                return Failure(serviceName, ProviderRegistrySnapshotState.Unavailable,
                    $"can not get [{serviceName}] from Nacos: {exception.Message}");
            }
        }

        private static ProviderRegistrySnapshot Failure(
            string serviceName,
            ProviderRegistrySnapshotState state,
            string errorInfo) =>
            new ProviderRegistrySnapshot(
                serviceName,
                Array.Empty<DubboServiceEndPointInfo>(),
                state,
                errorInfo);
    }
}
