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
using DubboNet.DubboService.DataModle;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new ArgumentException("Pass expected asset: netstandard2.0, net8.0 or net10.0.");
            string actual = typeof(DubboClient).Assembly.GetCustomAttribute<TargetFrameworkAttribute>().FrameworkName;
            string expected = args[0] == "netstandard2.0" ? ".NETStandard,Version=v2.0" :
                args[0] == "net8.0" ? ".NETCoreApp,Version=v8.0" : ".NETCoreApp,Version=v10.0";
            Require(actual == expected, "Wrong NuGet asset: " + actual + ", expected " + expected);
            await NativeRoundTrip();
            await TripleRoundTrip(false);
            await TripleRoundTrip(true);
            Console.WriteLine("PASS: " + actual + "; Native Hessian2, Triple HTTP/JSON, stalled-body deadline.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static async Task NativeRoundTrip()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            Task server = Task.Run(async () =>
            {
                using (TcpClient client = await listener.AcceptTcpClientAsync())
                using (NetworkStream stream = client.GetStream())
                {
                    byte[] header = new byte[16];
                    await ReadExactly(stream, header);
                    int length = header[12] << 24 | header[13] << 16 | header[14] << 8 | header[15];
                    Require(length >= 0 && length < 1024 * 1024, "Invalid request size");
                    await ReadExactly(stream, new byte[length]);
                    // Independently encoded Hessian2 VALUE(1), string("ok").
                    // Echo the request ID; do not call production codec helpers.
                    byte[] body = { 0x91, 0x02, (byte)'o', (byte)'k' };
                    header[2] = 2; header[3] = 20;
                    header[12] = header[13] = header[14] = 0; header[15] = (byte)body.Length;
                    await stream.WriteAsync(header, 0, header.Length);
                    await stream.WriteAsync(body, 0, body.Length);
                }
            });
            using (var suite = new NativeDubboActuatorSuite(endpoint,
                new DubboActuatorSuiteConf { DubboRequestTimeout = 3000 }))
            {
                DubboRequestResult result = await Bounded(suite.SendQuery(Invocation()));
                Require(result.QuerySuccess && result.Result == "\"ok\"", "Native round trip failed: " + result.ErrorMeaasge);
            }
            await Bounded(server);
        }
        finally { listener.Stop(); }
    }

    private static async Task TripleRoundTrip(bool stallBody)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var release = new TaskCompletionSource<bool>();
        try
        {
            Task server = Task.Run(async () =>
            {
                using (TcpClient client = await listener.AcceptTcpClientAsync())
                using (NetworkStream stream = client.GetStream())
                {
                    // Read the complete HTTP request, including its small JSON body.
                    var bytes = new MemoryStream();
                    var single = new byte[1];
                    string headers;
                    do
                    {
                        await ReadExactly(stream, single);
                        bytes.WriteByte(single[0]);
                        headers = Encoding.ASCII.GetString(bytes.ToArray());
                    } while (!headers.EndsWith("\r\n\r\n", StringComparison.Ordinal));
                    foreach (string line in headers.Split(new[] { "\r\n" }, StringSplitOptions.None))
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            await ReadExactly(stream, new byte[int.Parse(line.Substring(15).Trim())]);
                    Require(headers.StartsWith("POST /Service/echo HTTP/1.1", StringComparison.Ordinal),
                        "Unexpected Triple request: " + headers);
                    byte[] response = Encoding.UTF8.GetBytes(
                        "HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: 4\r\nConnection: close\r\n\r\n" +
                        (stallBody ? "" : "\"ok\""));
                    await stream.WriteAsync(response, 0, response.Length);
                    if (stallBody) await release.Task;
                }
            });
            using (var suite = new TripleDubboActuatorSuite((IPEndPoint)listener.LocalEndpoint,
                new DubboActuatorSuiteConf { DubboRequestTimeout = stallBody ? 750 : 3000 }))
            {
                DubboRequestResult result = await Bounded(suite.SendQuery(Invocation()));
                Require(stallBody ? !result.QuerySuccess && result.ErrorMeaasge.Contains("timed out") :
                    result.QuerySuccess && result.Result == "\"ok\"", "Triple round trip failed: " + result.ErrorMeaasge);
            }
            release.TrySetResult(true);
            await Bounded(server);
        }
        finally { release.TrySetResult(true); listener.Stop(); }
    }

    private static DubboInvocation Invocation() =>
        new DubboInvocation("Service", "echo", Array.Empty<string>(), Array.Empty<object>());

    private static async Task ReadExactly(Stream stream, byte[] buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int count = await stream.ReadAsync(buffer, offset, buffer.Length - offset);
            if (count == 0) throw new EndOfStreamException();
            offset += count;
        }
    }

    private static async Task Bounded(Task task)
    {
        if (await Task.WhenAny(task, Task.Delay(10000)) != task)
            throw new TimeoutException("Package smoke-test operation exceeded 10 seconds.");
        await task;
    }

    private static async Task<T> Bounded<T>(Task<T> task) { await Bounded((Task)task); return await task; }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
