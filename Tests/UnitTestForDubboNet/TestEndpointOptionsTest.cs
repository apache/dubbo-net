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
using DubboNet.DubboService;
using DubboNet.Testing;
using System.Net;

namespace UnitTestForDubboNet;

public class TestEndpointOptionsTest
{
    private static TestEndpointOptions Parse(params string[] args) => TestEndpointOptions.Parse(args, _ => null);

    [Fact]
    public void DefaultsRemainLocalWithoutEnvironmentOverrides()
    {
        TestEndpointOptions options = Parse();
        Assert.Equal("localhost:2181", options.RegistryAddress);
        Assert.Equal("localhost", options.ProviderHost);
        Assert.False(options.PrintConfiguration);
        Assert.False(options.ShowHelp);
    }

    [Theory]
    [InlineData("nacos")]
    [InlineData("--nacos")]
    public void NacosShortcutIsLocal(string argument)
    {
        Assert.Equal("nacos://localhost:8848", Parse(argument).RegistryAddress);
        Assert.Equal("localhost", Parse(argument).ProviderHost);
    }

    [Theory]
    [InlineData("192.0.2.10", "192.0.2.10:2181")]
    [InlineData("registry.example.test", "registry.example.test:2181")]
    [InlineData("::1", "[::1]:2181")]
    [InlineData("[::1]", "[::1]:2181")]
    public void HostOverrideIsUsedByRegistryAndDirectProvider(string host, string expected)
    {
        TestEndpointOptions options = Parse("--host", host);
        Assert.Equal(expected, options.RegistryAddress);
        Assert.Equal(host.Trim('[', ']'), options.ProviderHost);
    }

    [Fact]
    public void NacosHostOverrideUsesItsOwnPort()
    {
        Assert.Equal("nacos://192.0.2.10:8848", Parse("--nacos", "--host", "192.0.2.10").RegistryAddress);
        Assert.Equal("nacos://[::1]:8848", Parse("nacos", "--host", "::1").RegistryAddress);
    }

    [Theory]
    [InlineData("192.0.2.10:22181", "192.0.2.10:22181", "192.0.2.10")]
    [InlineData("nacos://registry.example.test:18848", "nacos://registry.example.test:18848", "registry.example.test")]
    [InlineData("registry.example.test", "registry.example.test:2181", "registry.example.test")]
    [InlineData("nacos://registry.example.test", "nacos://registry.example.test:8848", "registry.example.test")]
    [InlineData("[::1]:2181", "[::1]:2181", "::1")]
    [InlineData("zk1:2181,zk2:2181", "zk1:2181,zk2:2181", "zk1")]
    public void LegacyRegistryArgumentIsRetainedAndProvidesTheDefaultDirectHost(string argument, string expected, string host)
    {
        TestEndpointOptions options = Parse(argument);
        Assert.Equal(expected, options.RegistryAddress);
        Assert.Equal(host, options.ProviderHost);
    }

    [Fact]
    public void RegistryAndProviderCanBeConfiguredIndependently()
    {
        TestEndpointOptions options = Parse("--registry", "zk.example.test:2181", "--provider-host", "provider.example.test");
        Assert.Equal("zk.example.test:2181", options.RegistryAddress);
        Assert.Equal("provider.example.test", options.ProviderHost);
    }

    [Fact]
    public void EnvironmentConfigurationIsSupportedAndCommandLineHostOverridesIt()
    {
        var environment = new Dictionary<string, string>
        {
            ["DUBBONET_TEST_HOST"] = "host.example.test",
            ["DUBBONET_TEST_REGISTRY"] = "zk.example.test:2181",
            ["DUBBONET_TEST_PROVIDER_HOST"] = "provider.example.test"
        };
        string? Read(string key) => environment.GetValueOrDefault(key);
        TestEndpointOptions configured = TestEndpointOptions.Parse(Array.Empty<string>(), Read);
        Assert.Equal("zk.example.test:2181", configured.RegistryAddress);
        Assert.Equal("provider.example.test", configured.ProviderHost);
        TestEndpointOptions overridden = TestEndpointOptions.Parse(new[] { "--host", "localhost" }, Read);
        Assert.Equal("localhost:2181", overridden.RegistryAddress);
        Assert.Equal("localhost", overridden.ProviderHost);
        Assert.Throws<ArgumentException>(() => TestEndpointOptions.Parse(new[] { "--nacos" }, Read));
        Assert.Equal("nacos://localhost:8848", TestEndpointOptions.Parse(new[] { "--nacos", "--host", "localhost" }, Read).RegistryAddress);
    }

    [Theory]
    [InlineData("nacos")]
    [InlineData("--nacos")]
    public void NacosShortcutHonorsTheExplicitEnvironmentRegistry(string shortcut)
    {
        string? Read(string key) => key == "DUBBONET_TEST_REGISTRY" ? "nacos://registry.example.test:18848" : null;
        TestEndpointOptions options = TestEndpointOptions.Parse(new[] { shortcut }, Read);
        Assert.Equal("nacos://registry.example.test:18848", options.RegistryAddress);
        Assert.Equal("registry.example.test", options.ProviderHost);
        Assert.Equal("nacos://cli.example.test:28848", TestEndpointOptions.Parse(new[]
        {
            shortcut, "--registry", "nacos://cli.example.test:28848"
        }, Read).RegistryAddress);
    }

    [Fact]
    public void BlankEnvironmentDoesNotEnableRemoteDefaults()
    {
        Assert.Equal("localhost:2181", TestEndpointOptions.Parse(Array.Empty<string>(), _ => "  ").RegistryAddress);
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--host")]
    [InlineData("--registry")]
    [InlineData("--provider-host")]
    [InlineData("http://example.test:2181")]
    [InlineData("localhost:0")]
    public void InvalidArgumentsAreRejectedBeforeAnyRequests(string argument)
    {
        Assert.Throws<ArgumentException>(() => Parse(argument));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://example.test")]
    [InlineData("example.test:2181")]
    public void HostArgumentCannotContainAnAddressOrAnEmptyValue(string host)
    {
        Assert.Throws<ArgumentException>(() => Parse("--host", host));
    }

    [Fact]
    public void ConflictingOrExtraRegistryArgumentsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Parse("one:2181", "two:2181"));
        Assert.Throws<ArgumentException>(() => Parse("--nacos", "zk:2181"));
        Assert.Throws<ArgumentException>(() => Parse("--host", "--nacos"));
    }

    [Fact]
    public void NonNetworkInspectionFlagsAreAvailable()
    {
        Assert.True(Parse("--print-config").PrintConfiguration);
        Assert.True(Parse("--help").ShowHelp);
    }

    [Fact]
    public async Task LocalhostResolvesBeforeTheIpOnlyNativeConstructorIsUsed()
    {
        IPAddress[] addresses = await Parse().ResolveProviderAddressesAsync();
        Assert.NotEmpty(addresses);
        Assert.All(addresses, address => Assert.True(IPAddress.IsLoopback(address)));
        using var suite = new NativeDubboActuatorSuite(new IPEndPoint(addresses[0], 20880));
    }

    [Theory]
    [InlineData("192.0.2.10")]
    [InlineData("::1")]
    public async Task LiteralProviderAddressesDoNotRequireDns(string host)
    {
        Assert.Equal(IPAddress.Parse(host), Assert.Single(await Parse("--host", host).ResolveProviderAddressesAsync()));
    }

    [Fact]
    public void TestConfigurationIsNotShippedAsLibraryApi()
    {
        Assert.Null(typeof(DubboClient).Assembly.GetType("DubboNet.Testing.TestEndpointOptions"));
    }
}
