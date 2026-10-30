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

using System.Net;
using System.Net.Sockets;

namespace DubboNet.Testing;

// Shared only by the examples/tests; this is not part of the library's API.
internal sealed record TestEndpointOptions(
    string RegistryAddress,
    string ProviderHost,
    bool PrintConfiguration,
    bool ShowHelp)
{
    public const string Usage = """
        Options:
          --host HOST           Registry/provider host (default: localhost).
          --registry ADDRESS    ZooKeeper address or nacos:// address.
          --provider-host HOST  Override the direct provider host independently.
          --nacos               Use Nacos instead of ZooKeeper.
          --print-config        Print resolved configuration without network I/O.
          --help                Show this help without running requests.
        The first positional argument remains a registry address or 'nacos'.
        Environment: DUBBONET_TEST_HOST, DUBBONET_TEST_REGISTRY,
                     DUBBONET_TEST_PROVIDER_HOST.
        """;

    public static TestEndpointOptions Parse(
        string[] arguments,
        Func<string, string?>? readEnvironment = null)
    {
        readEnvironment ??= Environment.GetEnvironmentVariable;
        string? host = null, registry = null, provider = null;
        bool nacos = false, print = false, help = false;
        for (int index = 0; index < arguments.Length; index++)
        {
            string argument = arguments[index];
            switch (argument)
            {
                case "--host": host = Value(); break;
                case "--registry": registry = Value(); break;
                case "--provider-host": provider = Value(); break;
                case "--nacos": nacos = true; break;
                case "--print-config": print = true; break;
                case "--help": case "-h": help = true; break;
                default:
                    if (argument.StartsWith('-') || registry != null)
                        throw new ArgumentException($"Unexpected argument: {argument}");
                    registry = argument;
                    break;
            }

            string Value()
            {
                if (++index >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index])
                    || arguments[index].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"{argument} requires a value.");
                return arguments[index].Trim();
            }
        }

        string? environmentHost = Read("DUBBONET_TEST_HOST");
        string effectiveHost = Host(host ?? environmentHost ?? "localhost");
        registry ??= host != null || nacos ? null : Read("DUBBONET_TEST_REGISTRY");
        registry = registry?.Trim();
        string registryHost;
        if (string.IsNullOrEmpty(registry) || registry.Equals("nacos", StringComparison.OrdinalIgnoreCase))
        {
            nacos |= string.Equals(registry, "nacos", StringComparison.OrdinalIgnoreCase);
            string authority = effectiveHost.Contains(':') ? $"[{effectiveHost}]" : effectiveHost;
            registry = nacos ? $"nacos://{authority}:8848" : $"{authority}:2181";
            registryHost = effectiveHost;
        }
        else
        {
            bool nacosAddress = registry.StartsWith("nacos://", StringComparison.OrdinalIgnoreCase);
            if ((nacos && !nacosAddress) || (!nacosAddress && registry.Contains("://", StringComparison.Ordinal)))
                throw new ArgumentException("Use a ZooKeeper address or a nacos:// address.");
            string first = registry.Split(',')[0];
            if (!Uri.TryCreate(nacosAddress ? first : "zookeeper://" + first, UriKind.Absolute, out Uri? uri)
                || string.IsNullOrWhiteSpace(uri.Host) || uri.Port == 0)
                throw new ArgumentException("Invalid registry address.");
            registryHost = Host(uri.DnsSafeHost);
            if (uri.Port < 0)
            {
                if (registry.Contains(','))
                    throw new ArgumentException("Specify ports explicitly for multi-server registries.");
                UriBuilder builder = new(uri) { Port = nacosAddress ? 8848 : 2181 };
                registry = builder.Uri.ToString().TrimEnd('/');
                if (!nacosAddress) registry = registry["zookeeper://".Length..];
            }
        }

        string providerHost = Host(provider ?? host ?? Read("DUBBONET_TEST_PROVIDER_HOST")
            ?? environmentHost ?? registryHost);
        return new TestEndpointOptions(registry, providerHost, print, help);

        string? Read(string name)
        {
            string? value = readEnvironment(name);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    public async Task<IPAddress[]> ResolveProviderAddressesAsync()
    {
        IPAddress[] addresses = IPAddress.TryParse(ProviderHost, out IPAddress? address)
            ? new[] { address }
            : await Dns.GetHostAddressesAsync(ProviderHost);
        addresses = addresses.Where(item => item.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
            .OrderBy(item => item.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).ToArray();
        return addresses.Length > 0 ? addresses
            : throw new ArgumentException($"No usable IP address for provider host '{ProviderHost}'.");
    }

    private static string Host(string value)
    {
        string host = value.Trim().Trim('[', ']');
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
            throw new ArgumentException("Host must be a hostname or IP address, without a scheme or port.");
        return host;
    }
}
