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

using DubboNet.Clients;
using DubboNet.Clients.DataModle;
using DubboNet.Clients.RegistryClient;
using DubboNet.DubboService;
using DubboNet.Registry;
using org.apache.zookeeper;
using org.apache.zookeeper.data;

namespace UnitTestForDubboNet
{
    public class ZooKeeperRegistryPathContractTest
    {
        private sealed class FakeProviderZooKeeper : IProviderZooKeeper
        {
            public bool IsConnected { get; set; } = true;
            public Dictionary<string, Stat> Nodes { get; } = new();
            public List<string> ExistingPaths { get; } = new();
            public List<string>? Children { get; set; } = new();
            public Watcher? ReceivedWatcher { get; private set; }
            public Func<string, Task<IReadOnlyList<string>>>? ChildrenReader { get; set; }
            public string? LastChildrenPath { get; private set; }
            public int ChildrenReads { get; private set; }

            public Task<Stat> ExistsAsync(string path)
            {
                ExistingPaths.Add(path);
                Nodes.TryGetValue(path, out Stat? stat);
                return Task.FromResult(stat!);
            }

            public Task<IReadOnlyList<string>> GetChildrenAsync(string path, Watcher watcher)
            {
                ChildrenReads++;
                LastChildrenPath = path;
                ReceivedWatcher = watcher;
                return ChildrenReader != null
                    ? ChildrenReader(path)
                    : Task.FromResult<IReadOnlyList<string>>(Children!);
            }
        }

        [Fact]
        public async Task ZooKeeperDiscovery_MissingServiceReportsExistingError()
        {
            var zooKeeper = new FakeProviderZooKeeper();
            using var discovery = ProviderDiscovery.ForZooKeeper(zooKeeper, "/apps/",
                new DubboClientZookeeperWatcher(null!), () => { });

            var result = await discovery.GetProviderEndPointsAsync("com.foo.Service");

            Assert.Equal(ProviderRegistrySnapshotState.InvalidRequest, result.State);
            Assert.Equal("serviceName [com.foo.Service] is error", result.ErrorInfo);
            Assert.Equal(new[] { "/apps/com.foo.Service/providers", "/apps/com.foo.Service" },
                zooKeeper.ExistingPaths);
            Assert.Equal(0, zooKeeper.ChildrenReads);
        }

        [Fact]
        public async Task ZooKeeperDiscovery_EmptyProvidersReportsNoEndpoint()
        {
            var zooKeeper = new FakeProviderZooKeeper();
            zooKeeper.Nodes["/apps/com.foo.Service/providers"] = NodeWithChildren(0);
            using var discovery = ProviderDiscovery.ForZooKeeper(zooKeeper, "/apps/",
                new DubboClientZookeeperWatcher(null!), () => { });

            var result = await discovery.GetProviderEndPointsAsync("com.foo.Service");

            Assert.Equal(ProviderRegistrySnapshotState.Empty, result.State);
            Assert.Equal("no provider endpoint in [com.foo.Service]", result.ErrorInfo);
            Assert.Empty(result.EndPoints);
            Assert.Equal(1, zooKeeper.ChildrenReads);
            Assert.IsAssignableFrom<IProviderChangeWatch>(zooKeeper.ReceivedWatcher);
        }

        [Fact]
        public async Task ZooKeeperDiscovery_ExistingProvidersPassesWatcherAndSignalsRecovery()
        {
            var zooKeeper = new FakeProviderZooKeeper();
            zooKeeper.Nodes["/apps/com.foo.Service/providers"] = NodeWithChildren(1);
            zooKeeper.Children!.Add(Uri.EscapeDataString(
                "dubbo://127.0.0.1:20880/com.foo.Service?application=demo"));
            var watcher = new DubboClientZookeeperWatcher(null!);
            int recoveries = 0;
            using var discovery = ProviderDiscovery.ForZooKeeper(zooKeeper, "/apps/", watcher,
                () => recoveries++);

            var result = await discovery.GetProviderEndPointsAsync("com.foo.Service");

            Assert.Equal(ProviderRegistrySnapshotState.Available, result.State);
            Assert.Null(result.ErrorInfo);
            Assert.Single(result.EndPoints);
            Assert.Equal("127.0.0.1:20880", result.EndPoints[0].EndPoint.ToString());
            Assert.IsAssignableFrom<IProviderChangeWatch>(zooKeeper.ReceivedWatcher);
            Assert.Equal(1, zooKeeper.ChildrenReads);
            Assert.Equal(1, recoveries);
        }

        [Fact]
        public async Task ZooKeeperDiscovery_ChangeInvalidatesSnapshotAndTriggersFullReread()
        {
            var zooKeeper = new FakeProviderZooKeeper();
            zooKeeper.Nodes["/apps/com.foo.Service/providers"] = NodeWithChildren(1);
            zooKeeper.Children!.Add(Uri.EscapeDataString(
                "dubbo://127.0.0.1:20880/com.foo.Service?application=demo"));
            ProviderDiscovery? discovery = null;
            int changeNotifications = 0;
            var refreshed = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            discovery = ProviderDiscovery.ForZooKeeper(
                zooKeeper,
                "/apps/",
                new DubboClientZookeeperWatcher(null!),
                () => { },
                async serviceName =>
                {
                    Assert.Equal("com.foo.Service", serviceName);
                    changeNotifications++;
                    zooKeeper.Children!.Add(Uri.EscapeDataString(
                        "tri://127.0.0.2:50051/com.foo.Service?application=demo"));
                    ProviderRegistrySnapshot updated =
                        await discovery!.GetProviderEndPointsAsync(serviceName);
                    Assert.Equal(2, updated.EndPoints.Count);
                    refreshed.TrySetResult();
                });
            using (discovery)
            {
                ProviderRegistrySnapshot initial =
                    await discovery.GetProviderEndPointsAsync("com.foo.Service");
                Assert.Single(initial.EndPoints);

                await Assert.IsAssignableFrom<IProviderChangeWatch>(zooKeeper.ReceivedWatcher)
                    .NotifyChangedAsync("/apps/com.foo.Service/providers");
                await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));

                Assert.Equal(1, changeNotifications);
                Assert.Equal(2, zooKeeper.ChildrenReads);
            }
        }

        [Fact]
        public async Task ZooKeeperDiscovery_RemovedPreviouslyWatchedProviderPathIsConfirmedEmpty()
        {
            var zooKeeper = new FakeProviderZooKeeper();
            const string providerPath = "/apps/com.foo.Service/providers";
            zooKeeper.Nodes["/apps/com.foo.Service"] = NodeWithChildren(1);
            zooKeeper.Nodes[providerPath] = NodeWithChildren(1);
            zooKeeper.Children!.Add(Uri.EscapeDataString(
                "dubbo://127.0.0.1:20880/com.foo.Service?application=demo"));
            using var discovery = ProviderDiscovery.ForZooKeeper(
                zooKeeper,
                "/apps/",
                new DubboClientZookeeperWatcher(null!),
                () => { });

            ProviderRegistrySnapshot available =
                await discovery.GetProviderEndPointsAsync("com.foo.Service");
            zooKeeper.Nodes.Remove(providerPath);

            ProviderRegistrySnapshot removed =
                await discovery.GetProviderEndPointsAsync("com.foo.Service");

            Assert.Equal(ProviderRegistrySnapshotState.Available, available.State);
            Assert.Single(available.EndPoints);
            Assert.Equal(ProviderRegistrySnapshotState.Empty, removed.State);
            Assert.Empty(removed.EndPoints);
        }


        [Fact]
        public async Task ZooKeeperDiscovery_WatchesServiceNodeUntilProvidersNodeIsRecreated()
        {
            var zooKeeper = new FakeProviderZooKeeper();
            const string servicePath = "/apps/com.foo.Service";
            const string providerPath = servicePath + "/providers";
            zooKeeper.Nodes[servicePath] = NodeWithChildren(1);
            zooKeeper.Nodes[providerPath] = NodeWithChildren(1);
            zooKeeper.Children!.Add(Uri.EscapeDataString(
                "dubbo://127.0.0.1:20880/com.foo.Service?application=demo"));

            ProviderDiscovery? discovery = null;
            var providerReturned = new TaskCompletionSource<ProviderRegistrySnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            discovery = ProviderDiscovery.ForZooKeeper(
                zooKeeper,
                "/apps/",
                new DubboClientZookeeperWatcher(null!),
                () => { },
                async serviceName =>
                {
                    ProviderRegistrySnapshot refreshed =
                        await discovery!.GetProviderEndPointsAsync(serviceName);
                    if (refreshed.State == ProviderRegistrySnapshotState.Available)
                        providerReturned.TrySetResult(refreshed);
                });
            using (discovery)
            {
                Assert.Single((await discovery.GetProviderEndPointsAsync("com.foo.Service")).EndPoints);

                zooKeeper.Nodes.Remove(providerPath);
                zooKeeper.Children.Clear();
                ProviderRegistrySnapshot empty =
                    await discovery.GetProviderEndPointsAsync("com.foo.Service");
                Assert.Equal(ProviderRegistrySnapshotState.Empty, empty.State);
                Assert.Equal(servicePath, zooKeeper.LastChildrenPath);

                zooKeeper.Nodes[providerPath] = NodeWithChildren(1);
                zooKeeper.Children.Add(Uri.EscapeDataString(
                    "tri://127.0.0.2:50051/com.foo.Service?application=demo"));
                await Assert.IsAssignableFrom<IProviderChangeWatch>(zooKeeper.ReceivedWatcher)
                    .NotifyChangedAsync(servicePath);

                ProviderRegistrySnapshot recovered =
                    await providerReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Single(recovered.EndPoints);
                Assert.Equal(50051, recovered.EndPoints[0].EndPoint.Port);
                Assert.Equal(3, zooKeeper.ChildrenReads);
            }
        }

        [Fact]
        public async Task ZooKeeperDiscovery_ChangeDuringLookupForcesFreshSnapshot()
        {
            var zooKeeper = new FakeProviderZooKeeper();
            const string providerPath = "/apps/com.foo.Service/providers";
            zooKeeper.Nodes[providerPath] = NodeWithChildren(0);
            var initialReadStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseInitialRead = new TaskCompletionSource<IReadOnlyList<string>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int reads = 0;
            zooKeeper.ChildrenReader = async _ =>
            {
                if (Interlocked.Increment(ref reads) == 1)
                {
                    initialReadStarted.TrySetResult();
                    return await releaseInitialRead.Task;
                }
                return zooKeeper.Children!.ToArray();
            };
            zooKeeper.Children!.Clear();

            ProviderDiscovery? discovery = null;
            var refreshedSnapshot = new TaskCompletionSource<ProviderRegistrySnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            discovery = ProviderDiscovery.ForZooKeeper(
                zooKeeper,
                "/apps/",
                new DubboClientZookeeperWatcher(null!),
                () => { },
                async serviceName =>
                {
                    ProviderRegistrySnapshot refreshed =
                        await discovery!.GetProviderEndPointsAsync(serviceName);
                    refreshedSnapshot.TrySetResult(refreshed);
                });
            using (discovery)
            {
                Task<ProviderRegistrySnapshot> initialLookup =
                    discovery.GetProviderEndPointsAsync("com.foo.Service");
                await initialReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

                zooKeeper.Children.Add(Uri.EscapeDataString(
                    "dubbo://127.0.0.1:20880/com.foo.Service?application=demo"));
                await Assert.IsAssignableFrom<IProviderChangeWatch>(zooKeeper.ReceivedWatcher)
                    .NotifyChangedAsync(providerPath);
                releaseInitialRead.TrySetResult(Array.Empty<string>());

                ProviderRegistrySnapshot initial = await initialLookup;
                ProviderRegistrySnapshot refreshed =
                    await refreshedSnapshot.Task.WaitAsync(TimeSpan.FromSeconds(5));

                Assert.Equal(ProviderRegistrySnapshotState.Empty, initial.State);
                Assert.Equal(ProviderRegistrySnapshotState.Available, refreshed.State);
                Assert.Single(refreshed.EndPoints);
                Assert.Equal(2, zooKeeper.ChildrenReads);
            }
        }

        [Fact]
        public async Task ZooKeeperDiscovery_ChildrenDisappearAfterExists_ReportsReadErrorWithoutEndpoints()
        {
            var zooKeeper = new FakeProviderZooKeeper { Children = null };
            zooKeeper.Nodes["/apps/com.foo.Service/providers"] = NodeWithChildren(1);
            int recoveries = 0;
            using var discovery = ProviderDiscovery.ForZooKeeper(zooKeeper, "/apps/",
                new DubboClientZookeeperWatcher(null!), () => recoveries++);

            var result = await discovery.GetProviderEndPointsAsync("com.foo.Service");

            Assert.Equal(ProviderRegistrySnapshotState.Unavailable, result.State);
            Assert.Equal("GetChildrenAsync error [com.foo.Service]", result.ErrorInfo);
            Assert.Empty(result.EndPoints);
            Assert.Equal(1, zooKeeper.ChildrenReads);
            Assert.Equal(1, recoveries);
        }

        [Fact]
        public async Task ZooKeeperDiscovery_AccessDeniedIsNotReportedAsTransientUnavailability()
        {
            var zooKeeper = new FakeProviderZooKeeper
            {
                ChildrenReader = _ => Task.FromException<IReadOnlyList<string>>(
                    new KeeperException.NoAuthException())
            };
            zooKeeper.Nodes["/apps/com.foo.Service/providers"] = NodeWithChildren(1);
            using var discovery = ProviderDiscovery.ForZooKeeper(
                zooKeeper,
                "/apps/",
                new DubboClientZookeeperWatcher(null!),
                () => { });

            ProviderRegistrySnapshot result =
                await discovery.GetProviderEndPointsAsync("com.foo.Service");

            Assert.Equal(ProviderRegistrySnapshotState.PermissionDenied, result.State);
            Assert.Empty(result.EndPoints);
            Assert.Contains("NoAuth", result.ErrorInfo);
        }

        [Fact]
        public async Task ZooKeeperDiscovery_AuthenticationFailureIsNotReportedAsTransientUnavailability()
        {
            var zooKeeper = new FakeProviderZooKeeper
            {
                ChildrenReader = _ => Task.FromException<IReadOnlyList<string>>(
                    new KeeperException.AuthFailedException())
            };
            zooKeeper.Nodes["/apps/com.foo.Service/providers"] = NodeWithChildren(1);
            using var discovery = ProviderDiscovery.ForZooKeeper(
                zooKeeper,
                "/apps/",
                new DubboClientZookeeperWatcher(null!),
                () => { });

            ProviderRegistrySnapshot result =
                await discovery.GetProviderEndPointsAsync("com.foo.Service");

            Assert.Equal(ProviderRegistrySnapshotState.PermissionDenied, result.State);
            Assert.Empty(result.EndPoints);
            Assert.Contains("AuthFailed", result.ErrorInfo);
        }

        [Fact]
        public async Task ZooKeeperDiscovery_BadPathArgumentsAreReportedAsInvalidRequest()
        {
            var zooKeeper = new FakeProviderZooKeeper
            {
                ChildrenReader = _ => Task.FromException<IReadOnlyList<string>>(
                    new KeeperException.BadArgumentsException("/apps/com.foo.Service/providers"))
            };
            zooKeeper.Nodes["/apps/com.foo.Service/providers"] = NodeWithChildren(1);
            using var discovery = ProviderDiscovery.ForZooKeeper(
                zooKeeper,
                "/apps/",
                new DubboClientZookeeperWatcher(null!),
                () => { });

            ProviderRegistrySnapshot result =
                await discovery.GetProviderEndPointsAsync("com.foo.Service");

            Assert.Equal(ProviderRegistrySnapshotState.InvalidRequest, result.State);
            Assert.Empty(result.EndPoints);
            Assert.Contains("BadArguments", result.ErrorInfo);
        }

        private static Stat NodeWithChildren(int count) =>
            new Stat(1, 2, 3, 4, 5, 6, 7, 8, 9, count, 11);

        [Fact]
        public void SharedProviderUrlContracts_AreOwnedByProtocol()
        {
            Assert.Equal("DubboNet.DubboService", typeof(ProviderUrlKeys).Namespace);
            Assert.Equal("DubboNet.DubboService", typeof(ProviderProtocolSchemes).Namespace);
            Assert.Equal("interface", ProviderUrlKeys.Interface);
            Assert.Equal("dubbo", ProviderProtocolSchemes.Dubbo);
            Assert.Equal("tri", ProviderProtocolSchemes.Tri);
        }

        [Fact]
        public void RegistryImplementationHasItsOwnNamespace_WithoutMovingPublicClients()
        {
            Assert.Equal("DubboNet.Registry", typeof(ProviderDiscovery).Namespace);
            Assert.Equal("DubboNet.Registry", typeof(RegistryCategories).Namespace);
            Assert.Equal("DubboNet.Registry", typeof(ConsumerRegistrationConfiguration).Namespace);
            Assert.Equal("DubboNet.Clients.RegistryClient", typeof(MyNacos).Namespace);
            Assert.Equal("DubboNet.Clients.RegistryClient", typeof(MyZookeeper).Namespace);
            Assert.Equal("DubboNet.Clients.RegistryClient", typeof(ZNode).Namespace);
        }

        [Fact]
        public void RegistryCategories_AreSharedWithoutIncludingZooKeeperPathSeparators()
        {
            Assert.Equal("providers", RegistryCategories.Providers);
            Assert.Equal("consumers", RegistryCategories.Consumers);
            Assert.Equal("/providers", ZooKeeperRegistryPaths.ProvidersSuffix);
            Assert.Equal("/consumers/", ZooKeeperRegistryPaths.ConsumersDirectory);
        }

        [Theory]
        [InlineData(null, "/dubbo/", "/dubbo")]
        [InlineData("apps", "/apps/", "/apps")]
        [InlineData(" /apps/ ", "/apps/", "/apps")]
        public void RegistryRoots_PreserveExistingZooKeeperNormalization(
            string root, string expectedProviderRoot, string expectedMetadataRoot)
        {
            var config = new DubboClient.DubboClientConf { DubboRootPath = root };

            Assert.Equal((expectedProviderRoot, expectedMetadataRoot),
                DubboClient.ResolveRegistryPaths(false, false, config));
        }

        [Fact]
        public void ConsumerRegistration_PreservesZooKeeperPathAndNacosServiceName()
        {
            var options = new ConsumerRegistrationOptions
            {
                Enabled = true,
                ApplicationName = "orders-consumer",
                Host = "127.0.0.1",
                InstanceId = "test-instance"
            }.Snapshot();
            var registration = new ConsumerRegistration(options, "owner", "/apps/",
                "com.foo.Service", "1", "prod");

            Assert.Equal("/apps/com.foo.Service/consumers/" + Uri.EscapeDataString(registration.ConsumerUrl),
                registration.ZooKeeperPath);
            Assert.Equal("consumers:com.foo.Service:1:prod", registration.NacosServiceName);
            Assert.Equal("consumers", registration.Metadata["category"]);
        }
    }
}
