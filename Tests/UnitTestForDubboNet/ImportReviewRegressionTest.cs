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
using DubboNet.DubboService;
using DubboNet.DubboService.DataModle;
using DubboNet.DubboService.Native;
using System.Net;
using System.Net.Http;
using static DubboNet.Clients.DubboClient;

namespace UnitTestForDubboNet;

public class ImportReviewRegressionTest
{
    public static IEnumerable<object[]> Modes => Enum.GetValues<LoadBalanceMode>().Select(mode => new object[] { mode });

    [Theory]
    [MemberData(nameof(Modes))]
    public void ExplicitServiceIdentityFiltersEveryStrategyAndItsCache(LoadBalanceMode mode)
    {
        var shared = new Dictionary<DubboEndpointKey, DubboActuatorSuiteEndPintInfo>();
        var endpoints = new List<DubboServiceEndPointInfo>
        {
            Endpoint("dubbo", 20880, "1.0", "prod"),
            Endpoint("dubbo", 20881, "2.0", "canary"),
            Endpoint("dubbo", 20882, "1.0", "canary")
        };
        using var driver = new DubboServiceDriver("Service", endpoints, shared);
        for (int i = 0; i < 30; i++)
        {
            foreach (var endpoint in endpoints)
            {
                using var call = driver.SelectAndReserve(mode, "same hash", null,
                    Invocation(endpoint.Version, endpoint.Group));
                Assert.NotNull(call);
                Assert.Same(driver.InnerActuatorSuites[DubboEndpointKey.From(endpoint)].InnerDubboActuatorSuite, call.Suite);
            }
        }
        Assert.Null(driver.SelectAndReserve(mode, "same hash", null, Invocation("missing", "prod")));
        Assert.Null(driver.SelectAndReserve(mode, "same hash", null, Invocation("2.0", "prod")));
        using var versionOnly = driver.SelectAndReserve(mode, "same hash", null, Invocation("2.0", null));
        using var groupOnly = driver.SelectAndReserve(mode, "same hash", null, Invocation(null, "prod"));
        Assert.Same(driver.InnerActuatorSuites[DubboEndpointKey.From(endpoints[1])].InnerDubboActuatorSuite, versionOnly!.Suite);
        Assert.Same(driver.InnerActuatorSuites[DubboEndpointKey.From(endpoints[0])].InnerDubboActuatorSuite, groupOnly!.Suite);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void SameSocketCanExposeMultipleIdentitiesAndProtocolsWithoutOverwriting(LoadBalanceMode mode)
    {
        var shared = new Dictionary<DubboEndpointKey, DubboActuatorSuiteEndPintInfo>();
        using var driver = new DubboServiceDriver("Service", new()
        {
            Endpoint("dubbo", 20880, "1", "prod"),
            Endpoint("tri", 20880, "1", "prod"),
            Endpoint("dubbo", 20880, "2", "canary")
        }, shared);
        Assert.Equal(3, driver.ProviderCount);
        Assert.Equal(3, shared.Count);
        using var native = driver.SelectAndReserve(mode, "same hash", DubboActuatorProtocolType.NativeDubbo, Invocation("1", "prod"));
        using var triple = driver.SelectAndReserve(mode, "same hash", DubboActuatorProtocolType.Triple, Invocation("1", "prod"));
        using var otherVersion = driver.SelectAndReserve(mode, "same hash", DubboActuatorProtocolType.NativeDubbo, Invocation("2", "canary"));
        Assert.IsType<NativeDubboActuatorSuite>(native!.Suite);
        Assert.IsType<TripleDubboActuatorSuite>(triple!.Suite);
        Assert.NotSame(native.Suite, otherVersion!.Suite);
        Assert.NotSame(native.Statistics, otherVersion.Statistics);
        Assert.Null(driver.SelectAndReserve(LoadBalanceMode.Random, null, DubboActuatorProtocolType.Triple, Invocation("2", "canary")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DifferentServicesShareOnlyMatchingProtocolConnectionsRegardlessOfLoadOrder(bool tripleFirst)
    {
        var shared = new Dictionary<DubboEndpointKey, DubboActuatorSuiteEndPintInfo>();
        using var first = new DubboServiceDriver("A", new() { Endpoint(tripleFirst ? "tri" : "dubbo", 20880) }, shared);
        using var second = new DubboServiceDriver("B", new() { Endpoint(tripleFirst ? "dubbo" : "tri", 20880) }, shared);
        using var third = new DubboServiceDriver("C", new() { Endpoint(tripleFirst ? "tri" : "dubbo", 20880) }, shared);
        var a = first.GetDubboActuatorSuite(LoadBalanceMode.Random);
        var b = second.GetDubboActuatorSuite(LoadBalanceMode.Random);
        Assert.NotEqual(a.ProtocolType, b.ProtocolType);
        Assert.Same(a, third.GetDubboActuatorSuite(LoadBalanceMode.Random));
        Assert.Equal(2, shared.Count);
    }

    [Fact]
    public void ProtocolChangeAndRemovalKeepOnlyTheInFlightOldConnectionAlive()
    {
        var shared = new Dictionary<DubboEndpointKey, DubboActuatorSuiteEndPintInfo>();
        using var driver = new DubboServiceDriver("Service", new() { Endpoint("dubbo", 20880) }, shared);
        using var pending = driver.SelectAndReserve(LoadBalanceMode.Random, null, null, Invocation());
        driver.UpdateActuatorSuiteEndPoints(new() { Endpoint("tri", 20880) });
        Assert.Equal(2, shared.Count);
        Assert.Null(driver.GetDubboActuatorSuite(LoadBalanceMode.Random, requiredProtocol: DubboActuatorProtocolType.NativeDubbo));
        Assert.IsType<TripleDubboActuatorSuite>(driver.GetDubboActuatorSuite(LoadBalanceMode.Random));
        pending!.Complete(true);
        Assert.Single(shared);
        driver.UpdateActuatorSuiteEndPoints(new());
        Assert.Empty(shared);
    }

    [Theory]
    [InlineData("list")]
    [InlineData("map")]
    [InlineData("object")]
    [InlineData("definitions")]
    public void ExcessivelyDeepHessianResponsesFailWithACatchableProtocolException(string shape)
    {
        var payload = new List<byte> { 0x91 }; // Dubbo response kind: value.
        if (shape == "object") payload.AddRange(new byte[] { (byte)'C', 1, (byte)'T', 0x91, 1, (byte)'x' });
        for (int i = 0; i < 20000; i++)
            payload.AddRange(shape switch
            {
                "map" => new byte[] { (byte)'H', 1, (byte)'x' },
                "object" => new byte[] { 0x60 },
                "definitions" => new byte[] { (byte)'C', 1, (byte)'T', 0x90 },
                _ => new byte[] { 0x79 } // Untyped, fixed-length, one-element list.
            });
        payload.Add((byte)'N');
        if (shape == "map") payload.AddRange(Enumerable.Repeat((byte)'Z', 20000));
        var error = Assert.Throws<InvalidDataException>(() => Decode(payload.ToArray()));
        Assert.Contains("nesting", error.Message);
    }

    [Fact]
    public void HessianDepthLimitAcceptsBoundaryAndAlsoBoundsReferenceExpansion()
    {
        byte[] shallow = new byte[] { 0x91 }.Concat(Enumerable.Repeat((byte)0x79, DubboHessian2Reader.MaxNestingDepth - 1)).Append((byte)'N').ToArray();
        Assert.NotNull(Decode(shallow).Value);
        Assert.Throws<InvalidDataException>(() => Decode(new byte[] { 0x91, 0x79 }.Concat(shallow.Skip(1)).ToArray()));

        // Sibling lists referencing their predecessor are shallow on the wire but
        // form a deep graph when expanded. Parsing depth alone cannot stop this.
        var payload = new List<byte> { 0x91, 0x57, 0x78 };
        for (int i = 1; i < 100; i++)
        {
            payload.AddRange(new byte[] { 0x79, 0x51, (byte)'I', 0, 0, 0, (byte)i });
        }
        payload.Add((byte)'Z');
        Assert.Throws<InvalidDataException>(() => Decode(payload.ToArray()));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task TripleDeadlineCoversHeadersBodyCallerCancellationAndProviderTimeout(bool delayHeaders, bool cancelCaller, bool providerTimeout)
    {
        using var handler = new DelayedHandler(delayHeaders);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:50051"), Timeout = Timeout.InfiniteTimeSpan };
        using var suite = new TripleDubboActuatorSuite(client, new DubboActuatorSuiteConf { DubboRequestTimeout = providerTimeout ? 10000 : 150 });
        if (providerTimeout) suite.RegisterService(Endpoint("tri", 50051, timeout: 150));
        using var caller = new CancellationTokenSource();
        if (cancelCaller) caller.CancelAfter(50);
        var task = suite.SendQuery(Invocation(), caller.Token);
        Assert.Same(task, await Task.WhenAny(task, Task.Delay(5000)));
        var result = await task;
        Assert.False(result.QuerySuccess);
        Assert.InRange(result.RequestElapsed, 0, 4000);
        Assert.True(handler.Cancelled);
        if (!delayHeaders) Assert.True(handler.ContentDisposed);
    }

    private static DubboInvocation Invocation(string? version = null, string? group = null) =>
        new("Service", "echo", Array.Empty<string>(), Array.Empty<object>()) { Version = version, Group = group };

    private static DubboServiceEndPointInfo Endpoint(string scheme, int port, string? version = null, string? group = null, int? timeout = null) =>
        new() { EndPoint = new IPEndPoint(IPAddress.Loopback, port), Interface = "Service", Scheme = scheme, Version = version, Group = group, Timeout = timeout };

    private static NativeDubboResponse Decode(byte[] body) => NativeDubboCodec.DecodeResponse(new NativeDubboFrame
    { Status = 20, SerializationId = 2, Body = body });

    private sealed class DelayedHandler : HttpMessageHandler
    {
        private readonly bool _delayHeaders;
        internal DelayedHandler(bool delayHeaders) => _delayHeaders = delayHeaders;
        internal bool Cancelled, ContentDisposed;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_delayHeaders) await Wait(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new DelayedContent(this) };
        }
        private async Task Wait(CancellationToken token)
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
        }
        private sealed class DelayedContent : HttpContent
        {
            private readonly DelayedHandler _owner;
            internal DelayedContent(DelayedHandler owner) => _owner = owner;
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new InvalidOperationException("Expected cancellation-aware body read.");
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) => _owner.Wait(cancellationToken);
            protected override bool TryComputeLength(out long length) { length = 0; return false; }
            protected override void Dispose(bool disposing) { _owner.ContentDisposed = true; base.Dispose(disposing); }
        }
    }
}
