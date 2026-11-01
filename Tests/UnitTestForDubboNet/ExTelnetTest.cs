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
using System.Reflection;
using System.Text;

namespace UnitTestForDubboNet
{
    public class ExTelnetTest
    {
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
