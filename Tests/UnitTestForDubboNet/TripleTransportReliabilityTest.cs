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
using DubboNet.DubboService;
using DubboNet.DubboService.DataModle;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace UnitTestForDubboNet;

public class TripleTransportReliabilityTest
{
    [Theory]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("localhost", "localhost")]
    [InlineData("::1", "::1")]
    [InlineData("[::1]", "::1")]
    public void PublicFactoryBuildsCorrectUrisAndHasNoCompetingTimeout(string host, string expected)
    {
        using var suite = new TripleDubboActuatorSuite(host, 50051);
        HttpClient client = Client(suite);
        Assert.Equal(expected, client.BaseAddress!.DnsSafeHost);
        Assert.Equal(50051, client.BaseAddress.Port);
        Assert.Equal("http", client.BaseAddress.Scheme);
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }

    [Fact]
    public void IpEndpointFactoryHandlesIpv6WithoutNetworkIo()
    {
        using var suite = new TripleDubboActuatorSuite(new IPEndPoint(IPAddress.IPv6Loopback, 50051));
        Assert.Equal("http://[::1]:50051/", Client(suite).BaseAddress!.AbsoluteUri);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicFactoryHonorsProviderTimeoutLongerThanClientDefault(bool delayBody)
    {
        using var server = new LoopbackServer(IPAddress.Loopback);
        using var suite = new TripleDubboActuatorSuite(server.EndPoint, new DubboActuatorSuiteConf { DubboRequestTimeout = 50 });
        suite.RegisterService(Endpoint(server.EndPoint, 5000));
        Task serving = server.ReplyAsync(delayBody ? 0 : 250, delayBody ? 250 : 0);
        DubboRequestResult result = await suite.SendQuery(Invocation()).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.QuerySuccess, result.ErrorMeaasge);
        Assert.Equal("42", result.Result);
        await serving;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task PublicFactoryDeadlineBoundsHeadersAndBodyWithAndWithoutProviderMetadata(bool delayBody, bool providerTimeout)
    {
        using var server = new LoopbackServer(IPAddress.Loopback);
        using var suite = new TripleDubboActuatorSuite(server.EndPoint,
            new DubboActuatorSuiteConf { DubboRequestTimeout = providerTimeout ? 5000 : 150 });
        if (providerTimeout) suite.RegisterService(Endpoint(server.EndPoint, 150));
        Task serving = server.ReplyAsync(delayBody ? 0 : 1000, delayBody ? 1000 : 0);
        DubboRequestResult result = await suite.SendQuery(Invocation()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(result.QuerySuccess);
        Assert.Equal("Triple request timed out after 150 ms.", result.ErrorMeaasge);
        await serving;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellationReturnsAnExplicitFailureRatherThanACanceledTask(bool delayBody)
    {
        using var server = new LoopbackServer(IPAddress.Loopback);
        using var suite = new TripleDubboActuatorSuite(server.EndPoint, new DubboActuatorSuiteConf { DubboRequestTimeout = 10000 });
        using var caller = new CancellationTokenSource();
        Task serving = server.ReplyAsync(delayBody ? 0 : 1000, delayBody ? 1000 : 0);
        Task<DubboRequestResult> request = suite.SendQuery(Invocation(), caller.Token);
        await (delayBody ? server.HeadersSent.Task : server.RequestReceived.Task).WaitAsync(TimeSpan.FromSeconds(5));
        caller.Cancel();
        DubboRequestResult result = await request.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TaskStatus.RanToCompletion, request.Status);
        Assert.False(result.QuerySuccess);
        Assert.Equal("Triple request canceled by caller.", result.ErrorMeaasge);
        await serving;
    }

    [Fact]
    public async Task PreCanceledCallerDoesNotEnterTheTransport()
    {
        using var handler = new UnexpectedHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:50051") };
        using var suite = new TripleDubboActuatorSuite(client);
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        DubboRequestResult result = await suite.SendQuery(Invocation(), caller.Token);
        Assert.False(result.QuerySuccess);
        Assert.Equal("Triple request canceled by caller.", result.ErrorMeaasge);
        Assert.False(handler.Called);
    }

    [Fact]
    public async Task UnrelatedOperationCancellationIsNotMisreportedAsADeadline()
    {
        using var handler = new UnexpectedHandler { CancelInternally = true };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:50051") };
        using var suite = new TripleDubboActuatorSuite(client);
        DubboRequestResult result = await suite.SendQuery(Invocation());
        Assert.False(result.QuerySuccess);
        Assert.Equal("Transport canceled independently.", result.ErrorMeaasge);
    }

    [Fact]
    public async Task CallerCancellationWinsWhenBothTokenSourcesAreCanceled()
    {
        using var caller = new CancellationTokenSource();
        using var handler = new UnexpectedHandler { CancelCallerWhenDeadlineFires = caller };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:50051"), Timeout = Timeout.InfiniteTimeSpan };
        using var suite = new TripleDubboActuatorSuite(client, new DubboActuatorSuiteConf { DubboRequestTimeout = 50 });
        DubboRequestResult result = await suite.SendQuery(Invocation(), caller.Token).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(result.QuerySuccess);
        Assert.Equal("Triple request canceled by caller.", result.ErrorMeaasge);
    }

    [Fact]
    public async Task Ipv6LoopbackWorksThroughTheRealPublicTransportWhenSupported()
    {
        if (!Socket.OSSupportsIPv6) return;
        using var server = new LoopbackServer(IPAddress.IPv6Loopback);
        using var suite = new TripleDubboActuatorSuite(server.EndPoint);
        Task serving = server.ReplyAsync();
        DubboRequestResult result = await suite.SendQuery(Invocation()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.QuerySuccess, result.ErrorMeaasge);
        Assert.Equal("42", result.Result);
        await serving;
    }

    private static HttpClient Client(TripleDubboActuatorSuite suite) =>
        (HttpClient)typeof(TripleDubboActuatorSuite).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(suite)!;

    private static DubboInvocation Invocation() => new("Service", "echo", Array.Empty<string>(), Array.Empty<object>());
    private static DubboServiceEndPointInfo Endpoint(IPEndPoint endPoint, int timeout) =>
        new() { EndPoint = endPoint, Interface = "Service", Scheme = "tri", Timeout = timeout };

    private sealed class UnexpectedHandler : HttpMessageHandler
    {
        internal bool Called, CancelInternally;
        internal CancellationTokenSource? CancelCallerWhenDeadlineFires;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Called = true;
            if (CancelCallerWhenDeadlineFires != null)
            {
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException) { CancelCallerWhenDeadlineFires.Cancel(); throw; }
            }
            if (CancelInternally) throw new OperationCanceledException("Transport canceled independently.");
            throw new InvalidOperationException("A pre-canceled call must not reach the handler.");
        }
    }

    private sealed class LoopbackServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromSeconds(10));
        internal TaskCompletionSource RequestReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource HeadersSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal IPEndPoint EndPoint => (IPEndPoint)_listener.LocalEndpoint;

        internal LoopbackServer(IPAddress address)
        {
            _listener = new TcpListener(address, 0);
            _listener.Start();
        }

        internal async Task ReplyAsync(int headersDelay = 0, int bodyDelay = 0)
        {
            using TcpClient socket = await _listener.AcceptTcpClientAsync(_lifetime.Token);
            using NetworkStream stream = socket.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            int contentLength = 0;
            string? line;
            do
            {
                line = await reader.ReadLineAsync(_lifetime.Token);
                if (line?.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) == true)
                    contentLength = int.Parse(line["Content-Length:".Length..]);
            } while (!string.IsNullOrEmpty(line));
            if (contentLength > 0)
                await reader.ReadBlockAsync(new char[contentLength], _lifetime.Token);
            RequestReceived.TrySetResult();
            try
            {
                await Task.Delay(headersDelay, _lifetime.Token);
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nContent-Type: application/json\r\nConnection: close\r\n\r\n"), _lifetime.Token);
                HeadersSent.TrySetResult();
                await Task.Delay(bodyDelay, _lifetime.Token);
                await stream.WriteAsync(Encoding.ASCII.GetBytes("42"), _lifetime.Token);
            }
            catch (IOException)
            {
                // Deadline/cancellation tests intentionally close the client socket.
            }
        }

        public void Dispose()
        {
            _lifetime.Cancel();
            _listener.Stop();
            _lifetime.Dispose();
        }
    }
}
