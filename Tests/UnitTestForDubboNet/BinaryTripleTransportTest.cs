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
using DubboNet.DubboService.DataModle;
using System.Net;
using System.Net.Http.Headers;

namespace UnitTestForDubboNet;

public class BinaryTripleTransportTest
{
    [Fact]
    public void TypedResultsRetainBinaryProtocolMetadata()
    {
        var source = new DubboRequestResult
        {
            Result = "\"ok\"",
            GrpcStatusCode = 0,
            TripleExceptionCode = 0,
            ResponseHttpVersion = new Version(2, 0),
            ResponseTrailers = new Dictionary<string, object> { ["trace-id"] = "trace-1" }
        };
        var typed = new DubboRequestResult<string>(source);
        Assert.True(typed.QuerySuccess);
        Assert.Equal("ok", typed.ResultModle);
        Assert.Equal(0, typed.GrpcStatusCode);
        Assert.Equal(0, typed.TripleExceptionCode);
        Assert.Equal(new Version(2, 0), typed.ResponseHttpVersion);
        Assert.Same(source.ResponseTrailers, typed.ResponseTrailers);

        source.UpdateQueryFailed();
        source.TripleExceptionCode = 3;
        source.ErrorMeaasge = "Java exception";
        var failed = new DubboRequestResult<string>(source);
        Assert.False(failed.QuerySuccess);
        Assert.Equal(3, failed.TripleExceptionCode);
        Assert.Equal("Java exception", failed.ErrorMeaasge);
        Assert.Same(source.ResponseTrailers, failed.ResponseTrailers);
        Assert.Null(failed.ResultModle);
    }

    internal static DubboInvocation Invocation => new("demo.Service", "echo",
        new[] { "java.lang.String" }, new object[] { "hello" })
    { Group = "demo", Version = "1.0.0" };

    [Fact]
    public async Task SpecialDoubleValuesRoundTripInTypedCollections()
    {
        // A Hessian list of four IEEE doubles in an independently framed wrapper.
        using var handler = new Handler
        {
            ResponseFrame = Convert.FromHexString("00000000310a086865737369616e3412257c"
                + "447ff8000000000000" + "447ff0000000000000" + "44fff0000000000000" + "444004000000000000")
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        using var suite = new TripleDubboActuatorSuite(client, Options());

        var result = new DubboRequestResult<double[]>(await suite.SendQuery(Invocation));

        Assert.True(result.QuerySuccess, result.ErrorMeaasge);
        Assert.Null(result.ErrorMeaasge);
        Assert.Equal("[\"NaN\",\"Infinity\",\"-Infinity\",2.5]", result.Result);
        Assert.Equal(new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 2.5 }, result.ResultModle);
    }

    [Fact]
    public async Task ExplicitModeSendsFramedGenericWrapperOverExactHttp2()
    {
        using var handler = new Handler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:50051") };
        using var suite = new TripleDubboActuatorSuite(client, Options());
        var invocation = Invocation;
        invocation.Attachments = new Dictionary<string, string>
        {
            ["TraceId"] = "abc",
            ["opaque-bin"] = "AP8=",
            ["generic"] = "false",
            ["grpc-timeout"] = "1n",
            ["Content-Language"] = "zh-CN",
            ["Content-Encoding"] = "gzip"
        };
        DubboRequestResult result = await suite.SendQuery(invocation);
        Assert.True(result.QuerySuccess, result.ErrorMeaasge);
        Assert.Equal("\"ok\"", result.Result);
        Assert.Equal("ok", result.RawResult);
        Assert.Equal(0, result.GrpcStatusCode);
        Assert.Null(result.ResponseStatus);
        Assert.Equal(new Version(2, 0), result.ResponseHttpVersion);
        Assert.Equal("/demo.Service/$invoke", handler.Path);
        Assert.Equal("application/grpc+proto", handler.ContentType);
        Assert.Equal(new Version(2, 0), handler.Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionExact, handler.Policy);
        Assert.Equal("true", handler.Headers["generic"]);
        Assert.Equal("demo", handler.Headers["tri-service-group"]);
        Assert.Equal("1.0.0", handler.Headers["tri-service-version"]);
        Assert.Equal("trailers", handler.Headers["te"]);
        Assert.EndsWith("m", handler.Headers["grpc-timeout"]);
        Assert.NotEqual("1n", handler.Headers["grpc-timeout"]);
        Assert.Equal("AP8=", handler.Headers["opaque-bin"]);
        Assert.Equal("abc", handler.Headers["traceid"]);
        Assert.Equal("zh-CN", handler.ContentLanguage);
        Assert.False(handler.HasContentEncoding);
        Assert.Equal(0, handler.Body![0]);
        Assert.Equal(handler.Body.Length - 5,
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(handler.Body.AsSpan(1, 4)));
        Assert.Equal("0", result.ResponseTrailers!["grpc-status"]);
        Assert.Equal("application/grpc+proto", result.ResponseAttachments!["Content-Type"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RpcErrorIsAFailureEvenWithHttp200IncludingTrailersOnly(bool trailersOnly)
    {
        using var handler = new Handler { Status = "13", Message = "expected%20error%20%E4%B8%AD%E6%96%87", TrailersOnly = trailersOnly };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        using var suite = new TripleDubboActuatorSuite(client, Options());
        var result = await suite.SendQuery(Invocation);
        Assert.False(result.QuerySuccess);
        Assert.Equal(13, result.GrpcStatusCode);
        Assert.Contains("expected error 中文", result.ErrorMeaasge);
        Assert.Null(result.Result);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("00")]
    [InlineData("17")]
    [InlineData("-1")]
    [InlineData("0,0")]
    [InlineData("invalid")]
    public async Task MissingOrInvalidRpcStatusCannotSucceed(string? status)
    {
        using var handler = new Handler { Status = status };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        using var suite = new TripleDubboActuatorSuite(client, Options());
        var result = await suite.SendQuery(Invocation);
        Assert.False(result.QuerySuccess);
        Assert.Contains("grpc-status", result.ErrorMeaasge);
    }

    [Fact]
    public async Task SuccessfulTrailersOnlyCannotReplaceAUnaryResponseMessage()
    {
        using var handler = new Handler { TrailersOnly = true };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        using var suite = new TripleDubboActuatorSuite(client, Options());
        var result = await suite.SendQuery(Invocation);
        Assert.False(result.QuerySuccess);
        Assert.Contains("no message", result.ErrorMeaasge);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("0", true)]
    [InlineData("3", false)]
    [InlineData("invalid", false)]
    public async Task JavaExceptionIndicatorIsIndependentOfSuccessfulGrpcStatus(string? code, bool success)
    {
        using var handler = new Handler { JavaExceptionCode = code };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        using var suite = new TripleDubboActuatorSuite(client, Options());
        var result = await suite.SendQuery(Invocation);
        Assert.Equal(success, result.QuerySuccess);
        Assert.Equal(0, result.GrpcStatusCode);
        if (!success) Assert.Contains("tri-exception-code", result.ErrorMeaasge);
    }

    [Fact]
    public async Task ProviderReceivesOnlyTheRemainingWholeCallBudget()
    {
        using var handler = new Handler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        using var suite = new TripleDubboActuatorSuite(client, Options());
        var invocation = Invocation;
        invocation.DeadlineTimestamp = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency;
        Assert.True((await suite.SendQuery(invocation)).QuerySuccess);
        string timeout = handler.Headers["grpc-timeout"];
        Assert.EndsWith("m", timeout);
        Assert.InRange(int.Parse(timeout[..^1]), 1, 1000);
    }

    [Fact]
    public async Task ExpiredWholeCallBudgetDoesNotDispatch()
    {
        using var handler = new Handler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        using var suite = new TripleDubboActuatorSuite(client, Options());
        var invocation = Invocation;
        invocation.DeadlineTimestamp = System.Diagnostics.Stopwatch.GetTimestamp() - 1;
        Assert.False((await suite.SendQuery(invocation)).QuerySuccess);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/grpc+json")]
    [InlineData("text/html")]
    public async Task WrongContentTypeCannotSilentlyDowngradeToJson(string mediaType)
    {
        using var handler = new Handler { ResponseMediaType = mediaType };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        using var suite = new TripleDubboActuatorSuite(client, Options());
        var result = await suite.SendQuery(Invocation);
        Assert.False(result.QuerySuccess);
        Assert.Contains("Content-Type", result.ErrorMeaasge);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("version")]
    [InlineData("duplicate-status")]
    [InlineData("oversized-trailers")]
    [InlineData("oversized-headers")]
    [InlineData("leading-status-with-body")]
    public async Task InvalidResponseContractsFailWithoutRetry(string fault)
    {
        using var handler = new Handler { Fault = fault };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        using var suite = new TripleDubboActuatorSuite(client, Options());
        Assert.False((await suite.SendQuery(Invocation)).QuerySuccess);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("bad name", "abc")]
    [InlineData("trace", "中文")]
    [InlineData("trace", "header\r\ninjection")]
    [InlineData("opaque-bin", "not-base64")]
    public async Task InvalidMetadataFailsBeforeDispatch(string key, string value)
    {
        using var handler = new Handler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        using var suite = new TripleDubboActuatorSuite(client, Options());
        var invocation = Invocation;
        invocation.Attachments = new Dictionary<string, string> { [key] = value };
        Assert.False((await suite.SendQuery(invocation)).QuerySuccess);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task OversizedMetadataFailsBeforeDispatch()
    {
        using var handler = new Handler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        using var suite = new TripleDubboActuatorSuite(client, Options());
        var invocation = Invocation;
        invocation.Attachments = new Dictionary<string, string> { ["trace"] = new string('x', 8192) };
        var result = await suite.SendQuery(invocation);
        Assert.False(result.QuerySuccess);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task PreCancelledCallDoesNotSendAndDoesNotDisposeSharedClient()
    {
        using var handler = new Handler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        using var suite = new TripleDubboActuatorSuite(client, Options());
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var result = await suite.SendQuery(Invocation, cancel.Token);
        Assert.False(result.QuerySuccess);
        Assert.Contains("canceled by caller", result.ErrorMeaasge);
        Assert.Equal(0, handler.Calls);
        Assert.True((await suite.SendQuery(Invocation)).QuerySuccess);
    }

    [Fact]
    public async Task CancellationAtResponseEndCannotReturnSuccess()
    {
        using var caller = new CancellationTokenSource();
        using var handler = new Handler { CancelAtEnd = caller };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        using var suite = new TripleDubboActuatorSuite(client, Options());
        var result = await suite.SendQuery(Invocation, caller.Token);
        Assert.False(result.QuerySuccess);
        Assert.Contains("canceled by caller", result.ErrorMeaasge);
    }

    internal static DubboActuatorSuiteConf Options() => new()
    { TripleWireMode = TripleWireMode.Hessian2Wrapper, DubboRequestTimeout = 5000 };

    private sealed class Handler : HttpMessageHandler
    {
        internal string? Status = "0", Message, Path, ContentType, JavaExceptionCode, Fault;
        internal string ResponseMediaType = "application/grpc+proto";
        internal bool TrailersOnly;
        internal int Calls;
        internal byte[]? Body;
        internal Version? Version;
        internal HttpVersionPolicy Policy;
        internal CancellationTokenSource? CancelAtEnd;
        internal Dictionary<string, string> Headers = new(StringComparer.OrdinalIgnoreCase);
        internal byte[]? ResponseFrame;
        internal string? ContentLanguage;
        internal bool HasContentEncoding;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++; Path = request.RequestUri!.AbsolutePath;
            Version = request.Version; Policy = request.VersionPolicy;
            ContentType = request.Content!.Headers.ContentType!.MediaType;
            ContentLanguage = request.Content.Headers.ContentLanguage.SingleOrDefault();
            HasContentEncoding = request.Content.Headers.ContentEncoding.Count > 0;
            Body = await request.Content.ReadAsByteArrayAsync(token);
            Headers = request.Headers.ToDictionary(p => p.Key, p => string.Join(",", p.Value), StringComparer.OrdinalIgnoreCase);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            { Version = new Version(2, 0), Content = new ByteArrayContent(TrailersOnly ? Array.Empty<byte>() : ResponseFrame ?? BinaryTripleCodecTest.OkFrame) };
            if (CancelAtEnd != null)
                response.Content = new StreamContent(new CancelOnEofStream(BinaryTripleCodecTest.OkFrame, CancelAtEnd));
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(ResponseMediaType);
            var trailers = TrailersOnly ? response.Headers : response.TrailingHeaders;
            if (Status != null) trailers.TryAddWithoutValidation("grpc-status", Status);
            if (JavaExceptionCode != null) response.Headers.TryAddWithoutValidation("tri-exception-code", JavaExceptionCode);
            if (Message != null) trailers.TryAddWithoutValidation("grpc-message", Message);
            if (Fault == "http") response.StatusCode = HttpStatusCode.ServiceUnavailable;
            if (Fault == "version") response.Version = new Version(1, 1);
            if (Fault == "duplicate-status") response.Headers.TryAddWithoutValidation("grpc-status", "0");
            if (Fault == "leading-status-with-body")
            {
                response.TrailingHeaders.Remove("grpc-status");
                response.Headers.TryAddWithoutValidation("grpc-status", "0");
            }
            if (Fault == "oversized-trailers") response.TrailingHeaders.TryAddWithoutValidation("extra", new string('x', 8192));
            if (Fault == "oversized-headers")
            {
                // Each HttpClient collection fits separately; the single HTTP
                // header block must still share one allowance.
                response.Headers.TryAddWithoutValidation("extra", new string('x', 4500));
                response.Content.Headers.TryAddWithoutValidation("Content-Language", new string('x', 4500));
            }
            return response;
        }
    }

    private sealed class CancelOnEofStream : System.IO.MemoryStream
    {
        private readonly CancellationTokenSource _caller;
        internal CancelOnEofStream(byte[] body, CancellationTokenSource caller) : base(body) => _caller = caller;
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            int read = await base.ReadAsync(buffer, offset, count, token);
            if (read == 0) _caller.Cancel();
            return read;
        }
    }
}
