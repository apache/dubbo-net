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
using NHessian.IO;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace UnitTestForDubboNet
{
    public class NativeDubboActuatorSuiteTest
    {
        [Fact]
        public async Task SendQuery_LargeListReachesTheFinalJsonResult()
        {
            List<long> expected = Enumerable.Range(0, 150_000)
                .Select(value => (long)value)
                .ToList();
            byte[] body = EncodeBody(output =>
            {
                output.WriteInt(1); // Value.
                output.WriteObject(expected);
            });

            DubboRequestResult result = await QueryResponse(body);

            Assert.True(result.QuerySuccess, result.ErrorMeaasge);
            Assert.Null(result.ErrorMeaasge);
            Assert.NotNull(result.RequestId);
            Assert.Equal(expected.Count, Assert.IsType<List<object>>(result.RawResult).Count);
            Assert.Equal(expected, JsonSerializer.Deserialize<List<long>>(result.Result));
        }

        [Fact]
        public async Task SendQuery_LargeAsciiStringsRemainWithinThePayloadLimit()
        {
            string[] expected =
            {
                new string('x', 4 * 1024 * 1024 - 1024),
                new string('y', 4 * 1024 * 1024 - 1024)
            };
            byte[] body = EncodeBody(output =>
            {
                output.WriteInt(1); // Value.
                output.WriteObject(expected);
            });
            Assert.True(body.Length < 8 * 1024 * 1024);

            DubboRequestResult result = await QueryResponse(body);

            Assert.True(result.QuerySuccess, result.ErrorMeaasge);
            Assert.Equal(expected, JsonSerializer.Deserialize<string[]>(result.Result));
        }

        [Fact]
        public async Task SendQuery_LargeProviderExceptionPreservesFailureAndJson()
        {
            const string message = "provider failure";
            List<long> expected = Enumerable.Range(0, 150_000)
                .Select(value => (long)value)
                .ToList();
            byte[] body = EncodeBody(output =>
            {
                output.WriteInt(0); // Exception.
                output.WriteObject(new Dictionary<string, object>
                {
                    ["detailMessage"] = message,
                    ["values"] = expected
                });
            });

            DubboRequestResult result = await QueryResponse(body);

            Assert.False(result.QuerySuccess);
            Assert.Equal(message, result.ErrorMeaasge);
            var raw = Assert.IsType<Dictionary<string, object>>(result.RawResult);
            Assert.Equal(message, raw["detailMessage"]);
            Assert.Equal(expected.Count, Assert.IsType<List<object>>(raw["values"]).Count);
            using JsonDocument document = JsonDocument.Parse(result.Result);
            Assert.Equal(message, document.RootElement.GetProperty("detailMessage").GetString());
            Assert.Equal(expected, JsonSerializer.Deserialize<List<long>>(
                document.RootElement.GetProperty("values").GetRawText()));
        }

        [Fact]
        public async Task SendQuery_ProtocolErrorPreservesItsErrorTextAndJson()
        {
            const string message = "provider rejected the request";
            byte[] body = EncodeBody(output => output.WriteString(message));

            DubboRequestResult result = await QueryResponse(body, status: 70);

            Assert.False(result.QuerySuccess);
            Assert.Equal(message, result.ErrorMeaasge);
            Assert.Null(result.RawResult);
            Assert.Equal(message, JsonSerializer.Deserialize<string>(result.Result));
        }

        [Fact]
        public async Task SendQuery_CircularEventStillUsesSafeNormalization()
        {
            // An event value containing an untyped list that references itself.
            byte[] body = { 0x57, 0x51, 0x90, 0x5a };

            DubboRequestResult result = await QueryResponse(body, flags: 0x22);

            Assert.True(result.QuerySuccess, result.ErrorMeaasge);
            Assert.Equal(new[] { "[Circular reference]" },
                JsonSerializer.Deserialize<string[]>(result.Result));
        }

        [Fact]
        public async Task SendQuery_EventReferenceAmplificationStillFailsTheBudget()
        {
            byte[] body = ReferenceDag(20).Skip(1).ToArray();

            DubboRequestResult result = await QueryResponse(body, flags: 0x22);

            Assert.False(result.QuerySuccess);
            Assert.NotNull(result.RawResult);
            Assert.Null(result.Result);
            Assert.Contains("Hessian2 response expansion", result.ErrorMeaasge);
            Assert.Contains("nodes", result.ErrorMeaasge);
        }

        [Fact]
        public async Task SendQuery_ResponseReferenceAmplificationReturnsAFailedResult()
        {
            DubboRequestResult result = await QueryResponse(ReferenceDag(20));

            Assert.False(result.QuerySuccess);
            Assert.Null(result.RawResult);
            Assert.Null(result.Result);
            Assert.Contains("Hessian2 response expansion", result.ErrorMeaasge);
            Assert.Contains("nodes", result.ErrorMeaasge);
        }

        private static byte[] EncodeBody(Action<HessianOutputV2> write)
        {
            using MemoryStream stream = new MemoryStream();
            using HessianStreamWriter writer = new HessianStreamWriter(stream, true);
            HessianOutputV2 output = new HessianOutputV2(writer, TypeBindings.Java);
            write(output);
            return stream.ToArray();
        }

        private static byte[] ReferenceDag(int layers)
        {
            // Each compact list references its predecessor twice; root is ref 0.
            var body = new List<byte> { 0x91, 0x57, 0x79, 0x90 };
            for (int index = 1; index <= layers; index++)
                body.AddRange(new byte[] { 0x7a, 0x51, (byte)(0x90 + index), 0x51, (byte)(0x90 + index) });
            body.Add((byte)'Z');
            return body.ToArray();
        }

        private static async Task<DubboRequestResult> QueryResponse(
            byte[] body, byte flags = 0x02, byte status = 0x14)
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                listener.Start();
                IPEndPoint endpoint = (IPEndPoint)listener.LocalEndpoint;
                using NativeDubboActuatorSuite suite = new NativeDubboActuatorSuite(
                    endpoint, new DubboActuatorSuiteConf { DubboRequestTimeout = 10_000 });
                Task provider = ServeResponse(listener, body, flags, status, timeout.Token);
                DubboInvocation invocation = new DubboInvocation(
                    "com.foo.DemoService", "query", Array.Empty<string>(), Array.Empty<object>());

                DubboRequestResult result = await suite.SendQuery(invocation, timeout.Token);
                await provider.WaitAsync(timeout.Token);
                return result;
            }
            finally
            {
                timeout.Cancel();
                listener.Stop();
            }
        }

        private static async Task ServeResponse(
            TcpListener listener, byte[] body, byte flags, byte status, CancellationToken cancellationToken)
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
            using NetworkStream stream = client.GetStream();
            byte[] requestHeader = new byte[16];
            await stream.ReadExactlyAsync(requestHeader, cancellationToken);
            int requestBodyLength = BinaryPrimitives.ReadInt32BigEndian(requestHeader.AsSpan(12, 4));
            byte[] requestBody = new byte[requestBodyLength];
            await stream.ReadExactlyAsync(requestBody, cancellationToken);

            // Literal wire values keep this provider independent of codec constants.
            byte[] responseHeader = new byte[16];
            responseHeader[0] = 0xda;
            responseHeader[1] = 0xbb;
            responseHeader[2] = flags;
            responseHeader[3] = status;
            requestHeader.AsSpan(4, 8).CopyTo(responseHeader.AsSpan(4, 8));
            BinaryPrimitives.WriteInt32BigEndian(responseHeader.AsSpan(12, 4), body.Length);
            await stream.WriteAsync(responseHeader, cancellationToken);
            await stream.WriteAsync(body, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
    }
}
