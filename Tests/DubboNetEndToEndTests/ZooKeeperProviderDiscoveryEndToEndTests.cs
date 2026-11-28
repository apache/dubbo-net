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
using DubboNet.Registry;
using org.apache.zookeeper;
using Xunit;

namespace DubboNetEndToEndTests;

[Collection(EndToEndTestCollection.Name)]
public sealed class ZooKeeperProviderDiscoveryEndToEndTests
{
    [EndToEndTheory]
    [InlineData("3.3.6", 20880)]
    public async Task Production_reader_discovers_published_provider_from_real_zookeeper(
        string version, int providerPort)
    {
        await using var zookeeper = new MyZookeeper(
            EndToEndTestCatalog.GetRegistryAddress(EndToEndTestCatalog.ZooKeeper));
        using var discovery = new ProviderDiscovery(zookeeper, null!, $"/dubbo-{version}/",
            new PassiveWatcher(), _ => Task.CompletedTask, () => { });

        var result = await discovery.GetProviderEndPointsAsync(EndToEndTestCatalog.EchoService);

        Assert.Equal(ProviderRegistrySnapshotState.Available, result.State);
        Assert.Contains(result.EndPoints, endpoint =>
            endpoint.Scheme == "dubbo"
            && endpoint.EndPoint.Port == providerPort
            && endpoint.Interface == EndToEndTestCatalog.EchoService
            && endpoint.Version == "1.0.0"
            && endpoint.Group == $"dubbonet-{version}");
    }

    private sealed class PassiveWatcher : Watcher
    {
        public override Task process(WatchedEvent @event) => Task.CompletedTask;
    }
}
