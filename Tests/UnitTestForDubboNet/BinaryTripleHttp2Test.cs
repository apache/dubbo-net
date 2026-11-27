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

using DubboNet.DubboService;
using DubboNet.Clients;
using DubboNet.Clients.DataModle;
using DubboNet.DubboService.DataModle;
using DubboNet.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace UnitTestForDubboNet;

[CollectionDefinition("Binary Triple HTTP/2 runtime", DisableParallelization = true)]
public sealed class BinaryTripleHttp2Collection { }

[Collection("Binary Triple HTTP/2 runtime")]
public class BinaryTripleHttp2Test
{
    public static IEnumerable<object[]> ParameterModes() =>
        Enum.GetValues<DubboClient.LoadBalanceMode>().SelectMany(mode =>
            new[] { "java.util.List", "int[]", "java.util.Set", "java.lang.Iterable", "java.util.ArrayList", "java.util.HashSet" }
                .Select(javaType => new object[] { mode, javaType }));

    [Theory]
    [MemberData(nameof(ParameterModes))]
    public async Task BinaryParametersAreEnumeratedOnce(DubboClient.LoadBalanceMode mode, string javaType)
    {
        await using var server = await Server.StartAsync();
        using var client = CreateClient(server.Port, 5000, mode);
        var sequence = new SingleUseSequence();
        var invocation = new DubboInvocation("demo.Service", "overload", new[] { javaType }, new object[] { sequence });
        var result = await client.QueryAsync(invocation);
        Assert.True(result.QuerySuccess, result.ErrorMeaasge);
        Assert.Equal(1, sequence.Enumerations);
        var args = DecodeArguments(Assert.Single(server.Bodies));
        Assert.Equal(new object[] { 1, 2, 3 }, Assert.IsType<List<object>>(Assert.Single(args)));
    }

    [Theory]
    [InlineData(DubboClient.LoadBalanceMode.Random)]
    [InlineData(DubboClient.LoadBalanceMode.RoundRobin)]
    [InlineData(DubboClient.LoadBalanceMode.ShortestResponse)]
    [InlineData(DubboClient.LoadBalanceMode.LeastActive)]
    [InlineData(DubboClient.LoadBalanceMode.ConsistentHash)]
    [InlineData(DubboClient.LoadBalanceMode.P2CLoadBalance)]
    public async Task BinaryDoubleArgumentsDoNotRequireJsonEncoding(DubboClient.LoadBalanceMode mode)
    {
        await using var server = await Server.StartAsync();
        using var client = CreateClient(server.Port, 5000, mode);
        var invocation = new DubboInvocation("demo.Service", "overload", new[] { "double" }, new object[] { double.NaN });
        var result = await client.QueryAsync(invocation);
        Assert.True(result.QuerySuccess, result.ErrorMeaasge);
        Assert.True(double.IsNaN(Assert.IsType<double>(Assert.Single(DecodeArguments(Assert.Single(server.Bodies))))));
    }

    [Fact]
    public async Task BinaryEmptySignatureDoesNotDependOnMetadataDiscovery()
    {
        await using var server = await Server.StartAsync();
        using var client = CreateClient(server.Port, 3000);
        var metadata = (DubboMetadataManager)typeof(DubboClient).GetField("_metadataManager",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(client)!;
        var invocation = new DubboInvocation("demo.Service", "ping", Array.Empty<string>(), Array.Empty<object>());

        var result = await client.QueryAsync(invocation);

        Assert.False(metadata.HasCachedService(invocation.Service));
        Assert.True(result.QuerySuccess, result.ErrorMeaasge);
        Assert.Equal(0, result.GrpcStatusCode);
        Assert.Empty(DecodeArguments(Assert.Single(server.Bodies)));
    }

    [Fact]
    public async Task BinaryHashKeepsIdenticalRequestsOnOneNodeWithoutIncludingTraceMetadata()
    {
        await using var first = await Server.StartAsync();
        await using var second = await Server.StartAsync();
        using var client = CreateClient(first.Port, 5000, DubboClient.LoadBalanceMode.ConsistentHash,
            8 * 1024 * 1024, second.Port);
        for (int key = 0; key < 32; key++)
        {
            int a = first.Connections.Count, b = second.Connections.Count;
            var invocation = BinaryTripleTransportTest.Invocation;
            invocation.Arguments = new object[] { "key-" + key };
            invocation.Attachments = new Dictionary<string, string> { ["trace-id"] = "first" };
            Assert.True((await client.QueryAsync(invocation)).QuerySuccess);
            invocation.Attachments = new Dictionary<string, string> { ["trace-id"] = "second" };
            Assert.True((await client.QueryAsync(invocation)).QuerySuccess);
            Assert.True(first.Connections.Count - a == 2 && second.Connections.Count - b == 0
                || first.Connections.Count - a == 0 && second.Connections.Count - b == 2,
                "The same binary request changed provider when only trace metadata changed.");
            Assert.Null(invocation.PreparedTripleWrapper);
        }
        Assert.NotEmpty(first.Connections);
        Assert.NotEmpty(second.Connections);
    }

    [Fact]
    public async Task BinaryHashPreparationPreservesTheMessageLimitFailureContract()
    {
        await using var server = await Server.StartAsync();
        using var client = CreateClient(server.Port, 5000, DubboClient.LoadBalanceMode.ConsistentHash, 128);
        var invocation = BinaryTripleTransportTest.Invocation;
        invocation.Arguments = new object[] { new string('x', 4096) };
        var result = await client.QueryAsync(invocation);
        Assert.False(result.QuerySuccess);
        Assert.Contains("128-byte limit", result.ErrorMeaasge);
        Assert.Empty(server.Connections);
        Assert.Null(invocation.PreparedTripleWrapper);
    }

    [Theory]
    [InlineData(DubboClient.LoadBalanceMode.Random)]
    [InlineData(DubboClient.LoadBalanceMode.ConsistentHash)]
    public async Task RequestTraversalObservesCancellationBeforeEnumeratingMoreArguments(DubboClient.LoadBalanceMode mode)
    {
        await using var server = await Server.StartAsync();
        using var client = CreateClient(server.Port, 5000, mode);
        using var caller = new CancellationTokenSource();
        var invocation = new DubboInvocation("demo.Service", "overload", new[] { "java.util.List" },
            new object[] { CancelWhileEnumerating(caller) });
        var result = await client.QueryAsync(invocation, caller.Token);
        Assert.False(result.QuerySuccess);
        Assert.Contains("canceled by caller", result.ErrorMeaasge);
        Assert.Empty(server.Connections);
    }

    private static IEnumerable<int> CancelWhileEnumerating(CancellationTokenSource caller)
    {
        yield return 1;
        caller.Cancel();
        yield return 2;
        throw new InvalidOperationException("Argument traversal continued after cancellation.");
    }

    private static List<object> DecodeArguments(byte[] body)
    {
        // Decode the observed request, not a second invocation of its input sequence.
        using var input = new Google.Protobuf.CodedInputStream(body, 5, body.Length - 5);
        var fields = new List<byte[]>();
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            if (tag == 18) fields.Add(input.ReadBytes().ToByteArray());
            else input.SkipLastField();
        }
        Assert.Equal(3, fields.Count);
        return Assert.IsType<List<object>>(new DubboNet.DubboService.Native.DubboHessian2Reader(fields[2]).ReadValue());
    }

    private sealed class SingleUseSequence : IEnumerable<int>
    {
        internal int Enumerations;
        public IEnumerator<int> GetEnumerator()
        {
            if (++Enumerations > 1) throw new InvalidOperationException("The request sequence was enumerated twice.");
            return ((IEnumerable<int>)new[] { 1, 2, 3 }).GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Theory]
    [InlineData(DubboClient.LoadBalanceMode.Random)]
    [InlineData(DubboClient.LoadBalanceMode.RoundRobin)]
    [InlineData(DubboClient.LoadBalanceMode.ShortestResponse)]
    [InlineData(DubboClient.LoadBalanceMode.LeastActive)]
    [InlineData(DubboClient.LoadBalanceMode.ConsistentHash)]
    [InlineData(DubboClient.LoadBalanceMode.P2CLoadBalance)]
    public async Task BinaryClientFiltersNativeBeforeBalancingAndPreservesTheCallerInvocation(DubboClient.LoadBalanceMode mode)
    {
        await using var server = await Server.StartAsync();
        using var client = CreateClient(server.Port, 5000, mode);
        var invocation = BinaryTripleTransportTest.Invocation;
        invocation.Method = "overload";
        var result = await client.QueryAsync(invocation);
        Assert.True(result.QuerySuccess, result.ErrorMeaasge);
        Assert.Equal(0, result.GrpcStatusCode);
        Assert.Equal(new Version(2, 0), result.ResponseHttpVersion);
        Assert.Null(invocation.DeadlineTimestamp);
        Assert.Null(invocation.PreparedTripleWrapper);
        Assert.Single(server.Connections);
    }

    [Fact]
    public async Task ClientCancellationOverloadReachesTheRpcWithoutChangingOtherCalls()
    {
        await using var server = await Server.StartAsync();
        using var client = CreateClient(server.Port, 5000);
        using var caller = new CancellationTokenSource();
        var slow = BinaryTripleTransportTest.Invocation;
        slow.Attachments = new Dictionary<string, string> { ["x-test-wait"] = "headers" };
        Task<DubboRequestResult> pending = client.QueryAsync(slow, caller.Token);
        await server.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        caller.Cancel();
        var cancelled = await pending.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(cancelled.QuerySuccess);
        Assert.Contains("canceled by caller", cancelled.ErrorMeaasge);
        var next = await client.QueryAsync<string>(BinaryTripleTransportTest.Invocation, CancellationToken.None);
        Assert.True(next.QuerySuccess, next.ErrorMeaasge);
        Assert.Equal("ok", next.ResultModle);
    }

    [Fact]
    public async Task ClientBudgetCannotBeExtendedByALongerProviderTimeout()
    {
        await using var server = await Server.StartAsync();
        using var client = CreateClient(server.Port, 500);
        var slow = BinaryTripleTransportTest.Invocation;
        slow.Attachments = new Dictionary<string, string> { ["x-test-wait"] = "body" };
        var timedOut = await client.QueryAsync(slow).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(timedOut.QuerySuccess);
        Assert.Contains("including metadata/discovery", timedOut.ErrorMeaasge);
        Assert.EndsWith("m", server.Timeouts.Single());
        Assert.InRange(int.Parse(server.Timeouts.Single()[..^1]), 1, 500);
        Assert.True((await client.QueryAsync(BinaryTripleTransportTest.Invocation)).QuerySuccess);
    }

    private static DubboClient CreateClient(int port, int timeout, DubboClient.LoadBalanceMode mode = DubboClient.LoadBalanceMode.Random,
        int messageLimit = 8 * 1024 * 1024, params int[] additionalTriplePorts)
    {
        var client = new DubboClient("127.0.0.1:1", new DubboClient.DubboClientConf
        {
            TripleWireMode = TripleWireMode.Hessian2Wrapper,
            TripleMaxMessageBytes = messageLimit,
            DubboRequestTimeout = timeout,
            NowLoadBalanceMode = mode
        });
        // A prepopulated driver isolates these call-path tests from real registries.
        var drivers = (DubboDriverCollection)typeof(DubboClient).GetField("_dubboDriverCollection",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(client)!;
        var endpoints = new List<DubboServiceEndPointInfo>
        {
            DubboServiceEndPointInfo.GetDubboServiceEndPointInfo(new Uri($"dubbo://127.0.0.1:{port}/demo.Service?group=demo&version=1.0.0&timeout=5000")),
            DubboServiceEndPointInfo.GetDubboServiceEndPointInfo(new Uri($"tri://127.0.0.1:{port}/demo.Service?group=demo&version=1.0.0&timeout=5000"))
        };
        endpoints.AddRange(additionalTriplePorts.Select(extra => DubboServiceEndPointInfo.GetDubboServiceEndPointInfo(
            new Uri($"tri://127.0.0.1:{extra}/demo.Service?group=demo&version=1.0.0&timeout=5000"))));
        drivers.AddDubboServiceDriver("demo.Service", endpoints);
        return client;
    }

    [Fact]
    public async Task CancellingOneStreamDoesNotInterruptOtherCallsOrTheSharedConnection()
    {
        await using var server = await Server.StartAsync();
        using var suite = new TripleDubboActuatorSuite("127.0.0.1", server.Port, BinaryTripleTransportTest.Options());
        using var caller = new CancellationTokenSource();
        var slow = BinaryTripleTransportTest.Invocation;
        slow.Attachments = new Dictionary<string, string> { ["x-test-wait"] = "headers" };
        Task<DubboRequestResult> pending = suite.SendQuery(slow, caller.Token);
        await server.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        DubboRequestResult concurrent = await suite.SendQuery(BinaryTripleTransportTest.Invocation);
        caller.Cancel();
        DubboRequestResult cancelled = await pending.WaitAsync(TimeSpan.FromSeconds(3));
        DubboRequestResult next = await suite.SendQuery(BinaryTripleTransportTest.Invocation);
        Assert.False(cancelled.QuerySuccess);
        Assert.Contains("canceled by caller", cancelled.ErrorMeaasge);
        Assert.True(concurrent.QuerySuccess, concurrent.ErrorMeaasge);
        Assert.True(next.QuerySuccess, next.ErrorMeaasge);
        Assert.Equal(3, server.Connections.Count);
        Assert.Single(server.Connections.Distinct());
        Assert.All(server.Protocols, value => Assert.Equal("HTTP/2", value));
        Assert.Equal(0, next.GrpcStatusCode);
    }

    [Fact]
    public async Task DeadlineIncludesAStalledBodyAndDoesNotCloseThePool()
    {
        await using var server = await Server.StartAsync();
        var options = BinaryTripleTransportTest.Options(); options.DubboRequestTimeout = 500;
        using var suite = new TripleDubboActuatorSuite("127.0.0.1", server.Port, options);
        var slow = BinaryTripleTransportTest.Invocation;
        slow.Attachments = new Dictionary<string, string> { ["x-test-wait"] = "body" };
        DubboRequestResult timedOut = await suite.SendQuery(slow).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(timedOut.QuerySuccess);
        Assert.Contains("timed out after 500 ms", timedOut.ErrorMeaasge);
        DubboRequestResult next = await suite.SendQuery(BinaryTripleTransportTest.Invocation);
        Assert.True(next.QuerySuccess, next.ErrorMeaasge);
        Assert.Single(server.Connections.Distinct());
    }

    [Fact]
    public async Task DisposingTheSuiteStopsAStalledBodyPromptly()
    {
        await using var server = await Server.StartAsync();
        var options = BinaryTripleTransportTest.Options(); options.DubboRequestTimeout = 30_000;
        using var suite = new TripleDubboActuatorSuite("127.0.0.1", server.Port, options);
        var slow = BinaryTripleTransportTest.Invocation;
        slow.Attachments = new Dictionary<string, string> { ["x-test-wait"] = "body" };
        var pending = suite.SendQuery(slow);
        await server.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        suite.Dispose();
        var stopped = await pending.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(stopped.QuerySuccess);
        Assert.Contains("disposed", stopped.ErrorMeaasge);
        suite.Dispose(); // Repeated disposal must not cancel/dispose the source twice.
    }

    [Fact]
    public async Task DisposingOneSuiteDoesNotCloseAnInjectedSharedClient()
    {
        await using var server = await Server.StartAsync();
        using var http = new HttpClient(new SocketsHttpHandler())
        { BaseAddress = new Uri($"http://127.0.0.1:{server.Port}"), Timeout = Timeout.InfiniteTimeSpan };
        using var first = new TripleDubboActuatorSuite(http, BinaryTripleTransportTest.Options());
        using var other = new TripleDubboActuatorSuite(http, BinaryTripleTransportTest.Options());
        var slow = BinaryTripleTransportTest.Invocation;
        slow.Attachments = new Dictionary<string, string> { ["x-test-wait"] = "body" };
        var pending = first.SendQuery(slow);
        await server.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        first.Dispose();
        var stopped = await pending.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(stopped.QuerySuccess);
        Assert.Contains("disposed", stopped.ErrorMeaasge);
        var next = await other.SendQuery(BinaryTripleTransportTest.Invocation);
        Assert.True(next.QuerySuccess, next.ErrorMeaasge);
        Assert.Single(server.Connections.Distinct());
    }

    [Fact]
    public async Task InterruptedResponseIsNotReplayedAndLaterCallRecovers()
    {
        await using var server = await Server.StartAsync();
        using var suite = new TripleDubboActuatorSuite("127.0.0.1", server.Port, BinaryTripleTransportTest.Options());
        var broken = BinaryTripleTransportTest.Invocation;
        broken.Attachments = new Dictionary<string, string> { ["x-test-abort"] = "1" };
        var result = await suite.SendQuery(broken).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(result.QuerySuccess);
        Assert.Single(server.Connections);
        var next = await suite.SendQuery(BinaryTripleTransportTest.Invocation);
        Assert.True(next.QuerySuccess, next.ErrorMeaasge);
        Assert.Equal(2, server.Connections.Count);
    }

    [Fact]
    public async Task BinaryCallsDoNotCarryCookiesFromEarlierResponses()
    {
        await using var server = await Server.StartAsync();
        using var suite = new TripleDubboActuatorSuite("127.0.0.1", server.Port, BinaryTripleTransportTest.Options());
        var first = BinaryTripleTransportTest.Invocation;
        first.Attachments = new Dictionary<string, string> { ["x-test-set-cookie"] = "1" };
        var second = BinaryTripleTransportTest.Invocation;
        var third = BinaryTripleTransportTest.Invocation;
        third.Attachments = new Dictionary<string, string> { ["cookie"] = "fixture=explicit" };

        var setCookie = await suite.SendQuery(first);
        Assert.True(setCookie.QuerySuccess, setCookie.ErrorMeaasge);
        Assert.Contains("fixture=one", Assert.IsType<string>(setCookie.ResponseAttachments!["set-cookie"]));
        Assert.True((await suite.SendQuery(second)).QuerySuccess);
        Assert.True((await suite.SendQuery(third)).QuerySuccess);

        Assert.Equal(new[] { "", "", "fixture=explicit" }, server.Cookies.ToArray());
        Assert.Single(server.Connections.Distinct());
    }

    [Fact]
    public async Task HttpsTransportUsesHttp2WithAnExplicitlyTrustedTestCertificate()
    {
        using RSA rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        await using var server = await Server.StartAsync(certificate);
        using var handler = new SocketsHttpHandler();
        // Trust only this ephemeral fixture certificate. No production/global TLS
        // validation setting or OS trust store is changed by this test.
        handler.SslOptions.RemoteCertificateValidationCallback = (_, cert, _, _) => cert?.GetCertHashString() == certificate.Thumbprint;
        using var client = new HttpClient(handler) { BaseAddress = new Uri($"https://127.0.0.1:{server.Port}") };
        using var suite = new TripleDubboActuatorSuite(client, BinaryTripleTransportTest.Options());
        var result = await suite.SendQuery(BinaryTripleTransportTest.Invocation);
        Assert.True(result.QuerySuccess, result.ErrorMeaasge);
        Assert.Equal(new Version(2, 0), result.ResponseHttpVersion);
    }

    private sealed class Server : IAsyncDisposable
    {
        private readonly WebApplication _app;
        internal int Port { get; private set; }
        internal ConcurrentBag<string> Connections { get; } = new();
        internal ConcurrentBag<string> Protocols { get; } = new();
        internal ConcurrentBag<string> Timeouts { get; } = new();
        internal ConcurrentBag<byte[]> Bodies { get; } = new();
        internal ConcurrentQueue<string> Cookies { get; } = new();
        internal TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Server(WebApplication app) => _app = app;

        internal static async Task<Server> StartAsync(X509Certificate2? certificate = null)
        {
            var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listener =>
            {
                listener.Protocols = HttpProtocols.Http2;
                if (certificate != null) listener.UseHttps(certificate);
            }));
            var server = new Server(builder.Build());
            server._app.Run(server.HandleAsync);
            await server._app.StartAsync();
            server.Port = new Uri(server._app.Urls.Single()).Port;
            return server;
        }

        private async Task HandleAsync(HttpContext context)
        {
            Connections.Add(context.Connection.Id); Protocols.Add(context.Request.Protocol);
            Timeouts.Add(context.Request.Headers["grpc-timeout"].ToString());
            Cookies.Enqueue(context.Request.Headers.Cookie.ToString());
            Assert.Equal("application/grpc+proto", context.Request.ContentType);
            Assert.Equal("/demo.Service/$invoke", context.Request.Path.Value);
            using var requestBody = new MemoryStream();
            await context.Request.Body.CopyToAsync(requestBody, context.RequestAborted);
            Bodies.Add(requestBody.ToArray());
            context.Response.ContentType = "application/grpc+proto";
            if (context.Request.Headers.ContainsKey("x-test-set-cookie"))
                context.Response.Headers.Append("Set-Cookie", "fixture=one; Path=/");
            context.Response.DeclareTrailer("grpc-status");
            try
            {
                if (context.Request.Headers.TryGetValue("x-test-wait", out var kind))
                {
                    if (kind == "body")
                    {
                        await context.Response.Body.WriteAsync(new byte[] { 0 }, context.RequestAborted);
                        await context.Response.Body.FlushAsync(context.RequestAborted);
                    }
                    Waiting.TrySetResult();
                    await Task.Delay(Timeout.Infinite, context.RequestAborted);
                }
                if (context.Request.Headers.ContainsKey("x-test-abort"))
                {
                    await context.Response.Body.WriteAsync(new byte[] { 0 }, context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                    context.Abort(); return;
                }
                await context.Response.Body.WriteAsync(BinaryTripleCodecTest.OkFrame, context.RequestAborted);
                context.Response.AppendTrailer("grpc-status", "0");
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _app.StopAsync(stop.Token);
            await _app.DisposeAsync();
        }
    }
}
