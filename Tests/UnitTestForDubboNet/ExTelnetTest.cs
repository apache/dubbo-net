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

using NetService.Telnet;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace UnitTestForDubboNet
{
    public class ExTelnetTest
    {
        [Fact]
        public async Task ConnectionDeadlineStillAllowsSuccessfulLoopbackConnection()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                using ExTelnet telnet = new ExTelnet((IPEndPoint)listener.LocalEndpoint);
                Task<TcpClient> accept = listener.AcceptTcpClientAsync();

                Assert.True(await telnet.ConnectAsync(connectTimeOut: 5000)
                    .WaitAsync(TimeSpan.FromSeconds(10)));
                using TcpClient peer = await accept.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.True(telnet.IsConnected);
                Assert.Null(telnet.NowErrorMes);
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public async Task ConnectionDeadlineRetainsDurationInErrorAndStateReport()
        {
            Socket? attemptedSocket = null;
            CancellationToken deadlineToken = default;
            var messages = new List<string>();
            using ExTelnet telnet = new ExTelnet(new IPEndPoint(IPAddress.Loopback, 23),
                async (socket, endpoint, token) =>
                {
                    attemptedSocket = socket;
                    deadlineToken = token;
                    await Task.Delay(Timeout.Infinite, token);
                });
            telnet.OnMesageReport += (message, _) => messages.Add(message);

            bool connected = await telnet.ConnectAsync(connectTimeOut: 25).WaitAsync(TimeSpan.FromSeconds(3));

            Assert.False(connected);
            Assert.True(deadlineToken.IsCancellationRequested);
            Assert.Equal("Connect timed out for specify time : 25ms", telnet.NowErrorMes);
            Assert.Contains("Connect fail : " + telnet.NowErrorMes, messages);
            Assert.False(telnet.IsConnected);
            Assert.NotNull(attemptedSocket);
            // A failed, unpublished socket is still owned by this Telnet instance.
            telnet.Dispose();
            Assert.Throws<ObjectDisposedException>(() => attemptedSocket!.Poll(0, SelectMode.SelectRead));
        }

        [Fact]
        public async Task UnrelatedCancellationIsNotRelabeledAsConnectionTimeout()
        {
            using ExTelnet telnet = new ExTelnet(new IPEndPoint(IPAddress.Loopback, 23),
                (_, _, _) => throw new OperationCanceledException("unrelated cancellation"));

            Assert.False(await telnet.ConnectAsync(connectTimeOut: 60_000));
            Assert.Equal("unrelated cancellation", telnet.NowErrorMes);
            Assert.False(telnet.IsConnected);
        }

        [Fact]
        public async Task OrdinaryConnectionFailureKeepsItsOwnDiagnostics()
        {
            using ExTelnet telnet = new ExTelnet(new IPEndPoint(IPAddress.Loopback, 23),
                (_, _, _) => throw new IOException("connection failed before deadline"));

            Assert.False(await telnet.ConnectAsync(connectTimeOut: 60_000));
            Assert.Equal("connection failed before deadline", telnet.NowErrorMes);
            Assert.False(telnet.IsConnected);
        }

        [Fact]
        public async Task FindPositionReturnsTheActualMatchPosition()
        {
            using TelnetMemoryStream stream = new TelnetMemoryStream();
            await stream.AddDataAsync(new byte[] { 1, 2, 3, 2, 3 });

            Assert.Equal(0, stream.FindPosition(new byte[] { 1, 2 }));
            Assert.Equal(1, stream.FindPosition(new byte[] { 2, 3 }));
            Assert.Equal(3, stream.FindPosition(new byte[] { 2, 3 }, 2));
            Assert.Equal(-1, stream.FindPosition(new byte[] { 3, 3 }));
        }

        [Fact]
        public async Task WriteAndRequestFailFastWhenSocketIsNotConnected()
        {
            using ExTelnet telnet = new ExTelnet("127.0.0.1", 23);

            Assert.False(await telnet.WriteAsync(new byte[] { 1 }));
            await Assert.ThrowsAsync<IOException>(() => telnet.DoRequestAsync("ls"));
            Assert.False(telnet.IsInRequest);
        }

        [Fact]
        public async Task WaitStrWithoutDelayFindsMatchAtPositionZero()
        {
            using ExTelnet telnet = new ExTelnet("127.0.0.1", 23);
            FieldInfo requestStreamField = typeof(ExTelnet).GetField("requestStream", BindingFlags.NonPublic | BindingFlags.Instance)!;
            TelnetMemoryStream requestStream = (TelnetMemoryStream)requestStreamField.GetValue(telnet)!;
            await requestStream.AddDataAsync(Encoding.UTF8.GetBytes("target response"));
            MethodInfo waitStrMethod = typeof(ExTelnet).GetMethod("WaitStrAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

            Task<bool> waitTask = (Task<bool>)waitStrMethod.Invoke(telnet, new object[] { "target", 0 })!;

            Assert.True(await waitTask);
        }

        [Fact]
        public void DefaultHeartbeat_UsesTelnetCrLfWithoutSharingMutablePayload()
        {
            using ExTelnet telnet = new ExTelnet("127.0.0.1", 23);
            FieldInfo field = typeof(ExTelnet).GetField("_telnetBeatData", BindingFlags.NonPublic | BindingFlags.Instance)!;

            telnet.SetTelnetHeartbeat(60_000);
            byte[] firstPayload = Assert.IsType<byte[]>(field.GetValue(telnet));
            Assert.Equal(new byte[] { 13, 10 }, firstPayload);
            firstPayload[0] = 0;

            telnet.SetTelnetHeartbeat(60_000);
            Assert.Equal(new byte[] { 13, 10 }, Assert.IsType<byte[]>(field.GetValue(telnet)));
        }

        [Fact]
        public void HeartbeatUsesCustomDataAndCanBeDisabled()
        {
            using ExTelnet telnet = new ExTelnet("127.0.0.1", 23);
            byte[] heartbeat = new byte[] { 1, 2, 3 };

            telnet.SetTelnetHeartbeat(60_000, heartbeat);
            heartbeat[0] = 9;

            FieldInfo heartbeatDataField = typeof(ExTelnet).GetField("_telnetBeatData", BindingFlags.NonPublic | BindingFlags.Instance)!;
            FieldInfo heartbeatTimerField = typeof(ExTelnet).GetField("_telnetKeepliveTimer", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Assert.Equal(new byte[] { 1, 2, 3 }, (byte[])heartbeatDataField.GetValue(telnet)!);
            Assert.NotNull(heartbeatTimerField.GetValue(telnet));

            telnet.SetTelnetHeartbeat(0);

            Assert.Null(heartbeatDataField.GetValue(telnet));
            Assert.Null(heartbeatTimerField.GetValue(telnet));
        }

        [Fact]
        public void MessageSubscriberExceptionsAreIsolatedAndExceptionDetailsAreReported()
        {
            using ExTelnet telnet = new ExTelnet("127.0.0.1", 23);
            string? receivedMessage = null;
            telnet.OnMesageReport += (_, _) => throw new InvalidOperationException("subscriber failed");
            telnet.OnMesageReport += (message, _) => receivedMessage = message;

            MethodInfo reportMethod = typeof(ExTelnet).GetMethod("ReportMes", BindingFlags.NonPublic | BindingFlags.Instance)!;
            reportMethod.Invoke(telnet, new object[] { new InvalidOperationException("original error"), TelnetMessageType.Error });

            Assert.NotNull(receivedMessage);
            Assert.Contains("original error", receivedMessage!);
        }
    }
}
