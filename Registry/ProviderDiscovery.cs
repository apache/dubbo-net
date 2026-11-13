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
using org.apache.zookeeper;
using org.apache.zookeeper.data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace DubboNet.Registry
{
    // Narrow registry read contract: allows discovery's ZooKeeper branch to be
    // exercised offline without changing the public MyZookeeper API.
    internal interface IProviderZooKeeper
    {
        bool IsConnected { get; }
        Task<Stat> ExistsAsync(string path);
        Task<IReadOnlyList<string>> GetChildrenAsync(string path, Watcher watcher);
    }

    // Owns this client's Nacos provider subscriptions and in-flight discovery.
    // The client owns registry leases and its ZooKeeper watcher; the service driver
    // alone owns callable endpoint state. Metadata has a separate subscriber.
    internal sealed class ProviderDiscovery : IDisposable
    {
        private sealed class ZooKeeperReader : IProviderZooKeeper
        {
            private readonly MyZookeeper _client;
            internal ZooKeeperReader(MyZookeeper client) => _client = client;
            public bool IsConnected => _client.IsConnected;
            public Task<Stat> ExistsAsync(string path) => _client.ExistsAsync(path);
            public async Task<IReadOnlyList<string>> GetChildrenAsync(string path, Watcher watcher) =>
                (await _client.GetChildrenAsync(path, watcher).ConfigureAwait(false))?.Children;
        }

        private readonly IProviderZooKeeper _zookeeper;
        private readonly MyNacos _nacos;
        private readonly string _rootPath;
        private readonly Watcher _watcher;
        private readonly Func<string, Task> _onNacosChanged;
        private readonly Action _onRegistryRecovered;
        private readonly ConcurrentDictionary<(string Service, bool FullPath), Lazy<Task<DubboServiceEndPointInfos>>> _inFlight = new();
        private int _disposed;

        internal ProviderDiscovery(MyZookeeper zookeeper, MyNacos nacos, string rootPath,
            Watcher watcher, Func<string, Task> onNacosChanged, Action onRegistryRecovered)
            : this(zookeeper is null ? null : new ZooKeeperReader(zookeeper), nacos,
                rootPath, watcher, onNacosChanged, onRegistryRecovered)
        {
        }

        internal static ProviderDiscovery ForZooKeeper(IProviderZooKeeper zookeeper,
            string rootPath, Watcher watcher, Action onRegistryRecovered) =>
            new ProviderDiscovery(zookeeper, null, rootPath, watcher,
                _ => Task.CompletedTask, onRegistryRecovered);

        private ProviderDiscovery(IProviderZooKeeper zookeeper, MyNacos nacos, string rootPath,
            Watcher watcher, Func<string, Task> onNacosChanged, Action onRegistryRecovered)
        {
            if ((zookeeper is null) == (nacos is null))
                throw new ArgumentException("Exactly one Provider registry is required.");
            if (string.IsNullOrWhiteSpace(rootPath))
                throw new ArgumentException("Provider registry root cannot be empty.", nameof(rootPath));
            if (zookeeper != null && watcher is null)
                throw new ArgumentNullException(nameof(watcher));
            _zookeeper = zookeeper;
            _nacos = nacos;
            _rootPath = rootPath;
            _watcher = watcher;
            _onNacosChanged = onNacosChanged ?? throw new ArgumentNullException(nameof(onNacosChanged));
            _onRegistryRecovered = onRegistryRecovered ?? throw new ArgumentNullException(nameof(onRegistryRecovered));
        }

        internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            // Remove only our Nacos Naming subscriptions. Metadata and other
            // clients have distinct subscriber identities on the shared connection.
            _nacos?.RemoveSubscriber(this);
            // ZooKeeper watches are one-shot and have no per-watcher unsubscribe API;
            // the owning client watcher checks IsDisposed before handling callbacks.
        }

        private Task OnNacosChangedAsync(string serviceName) =>
            IsDisposed ? Task.CompletedTask : _onNacosChanged(serviceName);

        internal bool TryGetServiceNameFromProviderPath(
            string providerPath,
            out string serviceName)
        {
            serviceName = null;
            if (string.IsNullOrEmpty(providerPath)
                || !providerPath.StartsWith(_rootPath, StringComparison.Ordinal)
                || !providerPath.EndsWith(ZooKeeperRegistryPaths.ProvidersSuffix, StringComparison.Ordinal))
            {
                return false;
            }

            serviceName = providerPath.Substring(
                _rootPath.Length,
                providerPath.Length - _rootPath.Length - ZooKeeperRegistryPaths.ProvidersSuffix.Length);
            return !string.IsNullOrWhiteSpace(serviceName);
        }

        internal async Task<DubboServiceEndPointInfos> GetProviderEndPointsAsync(string serviceName, bool isFullPath = false)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            // A Lazy prevents losing GetOrAdd factories from starting duplicate SDK
            // queries or installing duplicate subscriptions under concurrent calls.
            (string Service, bool FullPath) key = (serviceName, isFullPath);
            Lazy<Task<DubboServiceEndPointInfos>> pending = _inFlight.GetOrAdd(key,
                static (request, owner) => new Lazy<Task<DubboServiceEndPointInfos>>(
                    () => owner.DiscoverAsync(request.Service, request.FullPath),
                    LazyThreadSafetyMode.ExecutionAndPublication), this);
            try
            {
                DubboServiceEndPointInfos result = await pending.Value.ConfigureAwait(false);
                // An SDK lookup may outlive disposal. Never publish its snapshot after
                // the subscription that would keep it current has been removed.
                ObjectDisposedException.ThrowIf(IsDisposed, this);
                return result;
            }
            finally
            {
                _inFlight.TryRemove(
                    new KeyValuePair<(string Service, bool FullPath), Lazy<Task<DubboServiceEndPointInfos>>>(key, pending));
            }
        }

        /// <summary>
        /// 从注册中心发现 Provider 地址；服务存在时安装更新观察器。
        /// EN: Discovers provider endpoints in the registry and installs the provider watcher when the service exists.
        /// </summary>
        /// <param name="serviceName"></param>
        /// <param name="isFullPath"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        private async Task<DubboServiceEndPointInfos> DiscoverAsync(string serviceName ,bool isFullPath = false)
        {
            if(string.IsNullOrEmpty(serviceName))
            {
                throw new ArgumentNullException(nameof(serviceName));
            }
            if (_nacos != null)
            {
                DubboServiceEndPointInfos nacosResult = new DubboServiceEndPointInfos();
                if (isFullPath)
                {
                    nacosResult.ErrorInfo =
                        $"Nacos discovery expects a Dubbo interface name instead of a ZooKeeper path: {serviceName}";
                    return nacosResult;
                }
                try
                {
                    IReadOnlyList<DubboServiceEndPointInfo> endpoints =
                        await _nacos.GetProviderEndPointsAsync(
                            serviceName,
                            _rootPath,
                            this,
                            OnNacosChangedAsync).ConfigureAwait(false);
                    nacosResult.EndPoints.AddRange(endpoints);
                    if (nacosResult.EndPoints.Count == 0)
                    {
                        nacosResult.ErrorInfo =
                            $"no healthy provider endpoint for [{serviceName}] in Nacos group " +
                            $"[{MyNacos.NormalizeGroup(_rootPath, NacosRegistryDefaults.DefaultGroup)}]";
                    }
                }
                catch (Exception exception)
                {
                    nacosResult.ErrorInfo =
                        $"can not get [{serviceName}] from Nacos: {exception.Message}";
                }
                return nacosResult;
            }
            string nowFullPath = isFullPath
                ? serviceName
                : $"{_rootPath}{serviceName}{ZooKeeperRegistryPaths.ProvidersSuffix}";
            DubboServiceEndPointInfos serviceEndPointsInfo = new DubboServiceEndPointInfos();
            Stat stat =await _zookeeper.ExistsAsync(nowFullPath);
            if (stat==null)
            {
                if(!_zookeeper.IsConnected)
                {
                    serviceEndPointsInfo.ErrorInfo = $"can not get [{serviceName}] that _zookeeper can not connect";
                }
                else if((await _zookeeper.ExistsAsync($"{_rootPath}{serviceName}"))==null)
                {
                    serviceEndPointsInfo.ErrorInfo = $"serviceName [{serviceName}] is error";
                }
                else
                {
                    serviceEndPointsInfo.ErrorInfo = $"no provider in [{serviceName}]";
                }
            }
            else if(stat.getNumChildren()<=0)
            {
                serviceEndPointsInfo.ErrorInfo = $"no provider endpoint in [{serviceName}]";
            }
            else
            {
                //注册中心之前已经断开了，现在连接上了，需要重新更新watch
                _onRegistryRecovered();

                IReadOnlyList<string> children = await _zookeeper.GetChildrenAsync(nowFullPath,_watcher).ConfigureAwait(false);
                if (children == null)
                {
                    serviceEndPointsInfo.ErrorInfo = $"GetChildrenAsync error [{serviceName}]";
                }
                else
                {
                    foreach(var child in children)
                    {
                        if (child.StartsWith("dubbo%3A%2F%2F") || child.StartsWith("tri%3A%2F%2F"))
                        {
                            string nowDubboPath = System.Web.HttpUtility.UrlDecode(child, System.Text.Encoding.UTF8);
                            Uri nowDubboUri;
                            if(Uri.TryCreate(nowDubboPath, UriKind.Absolute, out nowDubboUri))
                            {
                                if (IPAddress.TryParse(nowDubboUri.Host, out _))
                                {
                                    serviceEndPointsInfo.EndPoints.Add(DubboServiceEndPointInfo.GetDubboServiceEndPointInfo(nowDubboUri));
                                }
                                else
                                {
                                    //这里如果有使用域名或主机名称的可能性，这里可以继续解析为IP
                                    MyLogger.LogWarning($"[GetSeviceProviderEndPoints] IPAddress.TryParse error {child}");
                                    continue;
                                }
                            }
                            else
                            {
                                MyLogger.LogWarning($"[GetSeviceProviderEndPoints] Uri.TryCreate error {child}");
                                continue;
                            }
                        }
                        else
                        {
                            MyLogger.LogWarning($"[GetSeviceProviderEndPoints] childrenResult.Children formate error {child}");
                            continue;
                        }
                    }
                }
            }
            return serviceEndPointsInfo;
        }

    }
}
