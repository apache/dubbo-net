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
            public List<string> Children { get; } = new();
            public Watcher? ReceivedWatcher { get; private set; }
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
                ReceivedWatcher = watcher;
                return Task.FromResult<IReadOnlyList<string>>(Children);
            }
        }

        [Fact]
        public async Task ZooKeeperDiscovery_MissingServiceReportsExistingError()
        {
            var zooKeeper = new FakeProviderZooKeeper();
            using var discovery = ProviderDiscovery.ForZooKeeper(zooKeeper, "/apps/",
                new DubboClientZookeeperWatcher(null!), () => { });

            var result = await discovery.GetProviderEndPointsAsync("com.foo.Service");

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

            Assert.Equal("no provider endpoint in [com.foo.Service]", result.ErrorInfo);
            Assert.Empty(result.EndPoints);
            // Watch installation for the empty case is tracked separately in #37.
        }

        [Fact]
        public async Task ZooKeeperDiscovery_ExistingProvidersPassesWatcherAndSignalsRecovery()
        {
            var zooKeeper = new FakeProviderZooKeeper();
            zooKeeper.Nodes["/apps/com.foo.Service/providers"] = NodeWithChildren(1);
            zooKeeper.Children.Add(Uri.EscapeDataString(
                "dubbo://127.0.0.1:20880/com.foo.Service?application=demo"));
            var watcher = new DubboClientZookeeperWatcher(null!);
            int recoveries = 0;
            using var discovery = ProviderDiscovery.ForZooKeeper(zooKeeper, "/apps/", watcher,
                () => recoveries++);

            var result = await discovery.GetProviderEndPointsAsync("com.foo.Service");

            Assert.Null(result.ErrorInfo);
            Assert.Single(result.EndPoints);
            Assert.Equal("127.0.0.1:20880", result.EndPoints[0].EndPoint.ToString());
            Assert.Same(watcher, zooKeeper.ReceivedWatcher);
            Assert.Equal(1, zooKeeper.ChildrenReads);
            Assert.Equal(1, recoveries);
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
        public void ProviderChangePath_RecognizesOnlyProviderNodesWithinConfiguredRoot()
        {
            using var client = new DubboClient("127.0.0.1:2181",
                new DubboClient.DubboClientConf { DubboRootPath = "/apps/" });

            Assert.True(client.TryGetServiceNameFromProviderPath(
                "/apps/com.foo.Service/providers", out string serviceName));
            Assert.Equal("com.foo.Service", serviceName);
            Assert.False(client.TryGetServiceNameFromProviderPath("/apps//providers", out _));
            Assert.False(client.TryGetServiceNameFromProviderPath("/dubbo/com.foo.Service/providers", out _));
            Assert.False(client.TryGetServiceNameFromProviderPath("/apps/com.foo.Service/consumers", out _));
            Assert.False(client.TryGetServiceNameFromProviderPath("/apps/com.foo.Service/providers/child", out _));
        }

        [Fact]
        public void DisposedClient_DoesNotLoseItsDiscoveryReference()
        {
            var client = new DubboClient("127.0.0.1:2181");
            client.Dispose();

            Assert.Throws<ObjectDisposedException>(() =>
                client.TryGetServiceNameFromProviderPath("/dubbo/com.foo.Service/providers", out _));
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
