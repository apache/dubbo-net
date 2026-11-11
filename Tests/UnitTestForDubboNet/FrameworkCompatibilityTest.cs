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
using DubboNet.Compatibility;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;

namespace UnitTestForDubboNet;

public class FrameworkCompatibilityTest
{
    [Fact]
    public void LoadsTheRequestedLibraryAsset()
    {
        string expected = typeof(FrameworkCompatibilityTest).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "DubboNetExpectedTargetFramework").Value!;
        string framework = typeof(DubboClient).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName;
        Assert.Equal(expected == "netstandard2.0" ? ".NETStandard,Version=v2.0" :
            expected == "net8.0" ? ".NETCoreApp,Version=v8.0" : ".NETCoreApp,Version=v10.0", framework);
        // The assertion is intentionally not inferred from the loaded library:
        // a bad project-reference/restore selection MUST fail this test.
    }

    [Theory]
    [InlineData("", 2)]
    [InlineData(" a , , b , c ", 2)]
    [InlineData(" , , a , , b , c ", 2)]
    [InlineData(" a : : b :c ", 2)]
    [InlineData(" , a , b ", 1)]
    [InlineData(" , a , b ", 0)]
    [InlineData(" \t , \r\n ", 3)]
    [InlineData("a,, b, c", 10)]
    public void TrimmedSplitMatchesRuntime(string value, int count)
    {
        foreach (string separator in new[] { ",", ":" })
            Assert.Equal(value.Split(new[] { separator }, count,
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                StringCompatibility.SplitTrimmed(value, separator, count));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:20880")]
    [InlineData("127.0.0.1:0")]
    [InlineData("127.0.0.1:65535")]
    [InlineData("127.0.0.1:65536")]
    [InlineData("127.0.0.1:-1")]
    [InlineData("127.0.0.1:+1")]
    [InlineData("127.0.0.1: 1")]
    [InlineData("[::1]:20880")]
    [InlineData("[::1]")]
    [InlineData("::1")]
    [InlineData("2001:db8::10")]
    [InlineData("[::ffff:127.0.0.1]:20880")]
    [InlineData("localhost:20880")]
    [InlineData("[::1]:")]
    [InlineData("[127.0.0.1]:80")]
    public void NumericEndpointParsingMatchesRuntime(string? value)
    {
        bool expected = IPEndPoint.TryParse(value!, out var endpoint);
        Assert.Equal(expected, EndpointCompatibility.TryParse(value!, out var actual));
        Assert.Equal(endpoint, actual);
    }

    [Fact]
    public void ExactRemovalNeverRemovesNewerDiscoveryTask()
    {
        var entries = new ConcurrentDictionary<string, Task>();
        Task old = Task.FromResult(1), current = Task.FromResult(2);
        entries["service"] = current;
        Assert.False(entries.RemoveExact(new("service", old)));
        Assert.Same(current, entries["service"]);
        Assert.True(entries.RemoveExact(new("service", current)));
        Assert.Empty(entries);
    }

    [Fact]
    public void RandomSupportsLongWeightsUnderConcurrency()
    {
        Assert.Equal(0, RuntimeCompatibility.NextInt64(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RuntimeCompatibility.NextInt64(-1));
        long maximum = (long)int.MaxValue * 100;
        var samples = new ConcurrentBag<long>();
        Parallel.For(0, 20000, _ => samples.Add(RuntimeCompatibility.NextInt64(maximum)));
        Assert.All(samples, value => Assert.InRange(value, 0, maximum - 1));
        Assert.Contains(samples, value => value > int.MaxValue);
        Assert.Contains(samples, value => value > maximum / 2);
        Assert.Contains(samples, value => value < maximum / 2);
        Assert.Equal(0, RuntimeCompatibility.NextInt64(1));
        Assert.Equal(Environment.ProcessId, RuntimeCompatibility.ProcessId);
        long first = RuntimeCompatibility.TickCount64;
        Assert.True(RuntimeCompatibility.TickCount64 >= first);
    }

    [Fact]
    public void IdentityComparerDoesNotCollapseEqualButDistinctObjects()
    {
        object first = new string('x', 2), second = new string('x', 2);
        Assert.Equal(first, second);
        var seen = new HashSet<object>(ReferenceIdentityComparer.Instance) { first, second, first };
        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public async Task CompatibilityWaitPreservesCancellationTimeoutAndSharedTask()
    {
        var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        // Explicit static calls ensure the Standard extension is exercised even
        // though the test host itself has the modern Task.WaitAsync API.
        Task<int> wait = Wait(pending.Task, cancellation.Token);
        cancellation.Cancel();
        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
        Assert.False(pending.Task.IsCompleted);
        await Assert.ThrowsAsync<TimeoutException>(() => Wait(pending.Task, TimeSpan.FromMilliseconds(20)));
        Assert.False(pending.Task.IsCompleted);
        pending.SetResult(42);
        Assert.Equal(42, await Wait(pending.Task, cancellation.Token)); // completed wins
        var failure = new InvalidOperationException("original");
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Wait(Task.FromException<int>(failure), CancellationToken.None)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Wait(Task.CompletedTask, TimeSpan.FromMilliseconds(-2)));
        await Assert.ThrowsAsync<TimeoutException>(() =>
            Wait(new TaskCompletionSource<int>().Task, TimeSpan.Zero));
        await Wait(Task.CompletedTask, Timeout.InfiniteTimeSpan);
    }

    private static Task<int> Wait(Task<int> task, CancellationToken token)
    {
        var method = typeof(TaskCompatibility).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .SingleOrDefault(item => item.Name == "WaitAsync" && item.IsGenericMethod);
        return method == null ? task.WaitAsync(token) :
            (Task<int>)method.MakeGenericMethod(typeof(int)).Invoke(null, new object[] { task, token })!;
    }

    private static Task Wait(Task task, TimeSpan timeout)
    {
        var method = typeof(TaskCompatibility).GetMethod("WaitAsync", BindingFlags.NonPublic | BindingFlags.Static,
            null, new[] { typeof(Task), typeof(TimeSpan) }, null);
        return method == null ? task.WaitAsync(timeout) : (Task)method.Invoke(null, new object[] { task, timeout })!;
    }

    [Fact]
    public void StandardAssetOnModernRuntimeRetainsSocketPoolAndExactHttp11()
    {
        using var handler = HttpCompatibility.CreateHandler(3, TimeSpan.FromSeconds(7));
        Assert.IsType<SocketsHttpHandler>(handler);
        Assert.Equal(3, ((SocketsHttpHandler)handler).MaxConnectionsPerServer);
        Assert.Equal(TimeSpan.FromSeconds(7), ((SocketsHttpHandler)handler).PooledConnectionIdleTimeout);
        using var request = new HttpRequestMessage();
        HttpCompatibility.SetHttp11Exact(request);
        Assert.Equal(HttpVersion.Version11, request.Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionExact, request.VersionPolicy);
        using var legacy = HttpCompatibility.CreateLegacyHandler(2);
        Assert.Equal(2, Assert.IsType<HttpClientHandler>(legacy).MaxConnectionsPerServer);
    }

    [Fact]
    public async Task LegacyHttpReadRetainsCharsetAndCancelsOnlyOwnedResponse()
    {
        using var response = new HttpResponseMessage
        { Content = new StringContent("café 中文", Encoding.Unicode) };
        Assert.Equal("café 中文", await HttpCompatibility.ReadLegacyAsStringAsync(response, CancellationToken.None));
        using var stalled = new HttpResponseMessage { Content = new StalledContent() };
        using var cancellation = new CancellationTokenSource();
        Task<string> read = HttpCompatibility.ReadLegacyAsStringAsync(stalled, cancellation.Token);
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.True(((StalledContent)stalled.Content).Disposed);
        // A late error in a custom uncancellable content implementation is observed.
        ((StalledContent)stalled.Content).Completion.TrySetException(new IOException("late failure"));
    }

    [Fact]
    public async Task PreCancelledOwnedConnectDoesNotConnectOrPublish()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var tcp = new TcpClient();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 1);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NetworkCompatibility.ConnectOwnedAsync(tcp, endpoint, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.False(tcp.Connected);
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NetworkCompatibility.ConnectOwnedAsync(socket, endpoint, cancellation.Token));
        Assert.False(socket.Connected);
    }

    private sealed class StalledContent : HttpContent
    {
        internal readonly TaskCompletionSource<bool> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed;
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Completion.Task;
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    [Fact]
    public async Task LegacyIoWaitIsBoundedWithoutDisposingSharedStream()
    {
        using var stream = new UncancellableStream();
        using var cancellation = new CancellationTokenSource();
        Task read = NetworkCompatibility.ReadLegacyAsync(stream, new byte[3], 1, 2, cancellation.Token);
        Task write = NetworkCompatibility.WriteLegacyAsync(stream, new byte[3], cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(2)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(stream.Disposed); // Only the connection owner may close it.
        stream.ReadCompletion.TrySetException(new IOException("late read failure"));
        stream.WriteCompletion.TrySetException(new IOException("late write failure"));
    }

    [Fact]
    public void TrimmedSplitMatchesRuntimeForMixedEmptySegmentsAndResultLimits()
    {
        var random = new Random(17);
        var segments = new[] { "", " ", "\t", "a", " b ", " c:d " };
        for (int i = 0; i < 500; i++)
        {
            string value = string.Join(":", Enumerable.Range(0, random.Next(1, 10))
                .Select(_ => segments[random.Next(segments.Length)]));
            int count = random.Next(0, 5);
            Assert.Equal(value.Split(new[] { ":" }, count,
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                StringCompatibility.SplitTrimmed(value, ":", count));
        }
    }

    private sealed class UncancellableStream : Stream
    {
        internal readonly TaskCompletionSource<int> ReadCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> WriteCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => ReadCompletion.Task;
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token) => WriteCompletion.Task;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
