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
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace HessianResponseMemoryProbe;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--worker")
            {
                Console.WriteLine(JsonSerializer.Serialize(await Measure(
                    int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]), long.Parse(args[4]))));
                return 0;
            }

            if (args.Contains("--help"))
            {
                Console.WriteLine("--scenario all|scalars|containers|json --concurrency 1,2,4 --iterations 3 --output results.json");
                return 0;
            }

            var options = ParseOptions(args);
            string[] scenarios = options.GetValueOrDefault("--scenario", "all") == "all"
                ? ["scalars", "containers", "json"]
                : [options["--scenario"]];
            int[] concurrencyLevels = options.GetValueOrDefault("--concurrency", "1,2,4")
                .Split(',').Select(int.Parse).ToArray();
            int iterations = int.Parse(options.GetValueOrDefault("--iterations", "3"));
            if (iterations < 1 || concurrencyLevels.Any(value => value < 1 || value > 16))
                throw new ArgumentException("Iterations must be positive; concurrency must be between 1 and 16.");

            var results = new List<object>();
            foreach (string scenario in scenarios)
            {
                Fixture fixture = BuildFixture(scenario);
                foreach (int concurrency in concurrencyLevels)
                {
                    for (int iteration = 1; iteration <= iterations; iteration++)
                    {
                        Console.Error.WriteLine($"{scenario}: concurrency={concurrency}, iteration={iteration}/{iterations}");
                        JsonElement measurement = await RunWorker(fixture, concurrency);
                        results.Add(new
                        {
                            Scenario = scenario,
                            Concurrency = concurrency,
                            Iteration = iteration,
                            PayloadBytesPerResponse = fixture.Body.Length,
                            NormalizedNodesPerResponse = fixture.Nodes,
                            JsonUtf8BytesPerResponse = fixture.JsonBytes,
                            Measurement = measurement
                        });
                    }
                }
            }

            string report = JsonSerializer.Serialize(new
            {
                RecordedAtUtc = DateTimeOffset.UtcNow,
                Results = results
            }, JsonOptions);
            if (options.TryGetValue("--output", out string? output))
            {
                string path = Path.GetFullPath(output);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, report);
            }
            Console.WriteLine(report);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>();
        string[] names = ["--scenario", "--concurrency", "--iterations", "--output"];
        for (int index = 0; index < args.Length; index += 2)
        {
            if (!names.Contains(args[index]) || index + 1 == args.Length)
                throw new ArgumentException($"Invalid or incomplete option: {args[index]}. Use --help.");
            options.Add(args[index], args[index + 1]);
        }
        return options;
    }

    private static async Task<JsonElement> RunWorker(Fixture fixture, int concurrency)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task provider = ServeRun(listener, fixture.Body, concurrency, timeout.Token);
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath),
            "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (string argument in new[] { "--worker", port.ToString(), concurrency.ToString(), fixture.Nodes.ToString(), fixture.JsonBytes.ToString() })
            start.ArgumentList.Add(argument);
        using Process worker = Process.Start(start) ?? throw new InvalidOperationException("Cannot start worker.");
        try
        {
            Task<string> stdout = worker.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> stderr = worker.StandardError.ReadToEndAsync(timeout.Token);
            Task exit = worker.WaitForExitAsync(timeout.Token);
            Task completed = await Task.WhenAny(provider, exit).WaitAsync(timeout.Token);
            await completed;
            if (completed == exit && worker.ExitCode != 0)
                throw new InvalidOperationException($"Worker exited with {worker.ExitCode}: {await stderr}");
            await Task.WhenAll(provider, exit).WaitAsync(timeout.Token);
            string output = await stdout;
            string errors = await stderr;
            if (worker.ExitCode != 0)
                throw new InvalidOperationException($"Worker exited with {worker.ExitCode}: {errors}");
            using JsonDocument document = JsonDocument.Parse(output);
            return document.RootElement.Clone();
        }
        finally
        {
            timeout.Cancel();
            try
            {
                if (!worker.HasExited) worker.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The worker can exit between checking HasExited and terminating it.
            }
        }
    }

    private static async Task ServeRun(TcpListener listener, byte[] body, int concurrency, CancellationToken token)
    {
        // Warm up the public transport and serializer before the worker takes its baseline.
        using (TcpClient warmup = await listener.AcceptTcpClientAsync(token))
            await Reply(warmup.GetStream(), new byte[] { 0x91, 0x90 }, Task.CompletedTask, token);

        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int pending = 0;
        async Task ServeOne()
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(token);
            await Reply(client.GetStream(), body, ready.Task, token, () =>
            {
                if (Interlocked.Increment(ref pending) == concurrency) ready.SetResult();
            });
        }
        await Task.WhenAll(Enumerable.Range(0, concurrency).Select(_ => ServeOne()));
    }

    private static async Task Reply(NetworkStream stream, byte[] body, Task ready, CancellationToken token, Action? received = null)
    {
        byte[] requestHeader = new byte[16];
        await stream.ReadExactlyAsync(requestHeader, token);
        int requestLength = BinaryPrimitives.ReadInt32BigEndian(requestHeader.AsSpan(12, 4));
        if (requestLength < 0 || requestLength > 64 * 1024)
            throw new InvalidDataException($"Unexpected probe request size: {requestLength}.");
        await stream.ReadExactlyAsync(new byte[requestLength], token);
        received?.Invoke();
        await ready.WaitAsync(token);
        byte[] header = new byte[16];
        header[0] = 0xda;
        header[1] = 0xbb;
        header[2] = 0x02; // Hessian2 response.
        header[3] = 0x14; // OK.
        requestHeader.AsSpan(4, 8).CopyTo(header.AsSpan(4, 8));
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(12, 4), body.Length);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(body, token);
    }

    private static async Task<object> Measure(int port, int concurrency, int expectedNodes, long expectedJsonBytes)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var endpoint = new IPEndPoint(IPAddress.Loopback, port);
        await Query(endpoint, timeout.Token);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        long baselineWorkingSet = process.WorkingSet64;
        long baselineManaged = GC.GetTotalMemory(false);
        long sampledManagedPeak = baselineManaged;
        int samples = 0;
        bool stop = false;
        var sampler = new Thread(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                sampledManagedPeak = Math.Max(sampledManagedPeak, GC.GetTotalMemory(false));
                samples++;
                Thread.Sleep(5);
            }
        })
        { IsBackground = true, Name = "managed-heap-sampler" };
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var clock = Stopwatch.StartNew();
        DubboRequestResult[] results;
        sampler.Start();
        try
        {
            results = await Task.WhenAll(Enumerable.Range(0, concurrency)
                .Select(_ => Query(endpoint, timeout.Token)));
        }
        finally
        {
            Volatile.Write(ref stop, true);
            sampler.Join();
        }
        clock.Stop();
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        long managedAtCompletion = GC.GetTotalMemory(false);
        sampledManagedPeak = Math.Max(sampledManagedPeak, managedAtCompletion);
        process.Refresh();
        long processPeakWorkingSet = process.PeakWorkingSet64;
        foreach (DubboRequestResult result in results)
        {
            // All fixtures produce ASCII-only JSON, so UTF-16 length equals its UTF-8 byte count.
            if (result.Result.Length != expectedJsonBytes)
                throw new InvalidDataException($"Unexpected JSON size: {result.Result.Length}, expected {expectedJsonBytes}.");
            if (CountNodes(result.RawResult) != expectedNodes)
                throw new InvalidDataException($"Unexpected normalized node count, expected {expectedNodes}.");
        }
        GC.KeepAlive(results); // Retain every RawResult and JSON string together through measurement.
        return new
        {
            Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            LogicalProcessors = Environment.ProcessorCount,
            ServerGC = GCSettings.IsServerGC,
            GCLatencyMode = GCSettings.LatencyMode.ToString(),
            GCEnvironment = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
                .Where(entry => entry.Key.ToString()!.StartsWith("DOTNET_GC", StringComparison.OrdinalIgnoreCase)
                    || entry.Key.ToString()!.StartsWith("COMPlus_GC", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(entry => entry.Key.ToString()!, entry => entry.Value?.ToString()),
            BaselineWorkingSetBytes = baselineWorkingSet,
            ProcessPeakWorkingSetBytes = processPeakWorkingSet,
            BaselineManagedBytes = baselineManaged,
            SampledManagedPeakBytes = sampledManagedPeak,
            ManagedBytesAtCompletion = managedAtCompletion,
            TotalAllocatedBytes = allocated,
            ManagedSampleIntervalMilliseconds = 5,
            ManagedSampleCount = samples,
            ElapsedMilliseconds = clock.Elapsed.TotalMilliseconds
        };
    }

    private static int CountNodes(object value)
    {
        int nodes = 1;
        if (value is List<object> items)
            foreach (object item in items) nodes += CountNodes(item);
        return nodes;
    }

    private static async Task<DubboRequestResult> Query(IPEndPoint endpoint, CancellationToken token)
    {
        using var suite = new NativeDubboActuatorSuite(endpoint,
            new DubboActuatorSuiteConf { DubboRequestTimeout = 120_000 });
        var invocation = new DubboInvocation("com.foo.DemoService", "query", [], []);
        DubboRequestResult result = await suite.SendQuery(invocation, token);
        if (!result.QuerySuccess || result.RawResult == null || result.Result == null)
            throw new InvalidOperationException(result.ErrorMeaasge ?? "Probe query failed.");
        return result;
    }

    private sealed record Fixture(byte[] Body, int Nodes, long JsonBytes);

    private static Fixture BuildFixture(string scenario)
    {
        if (scenario == "scalars" || scenario == "containers")
        {
            int count = scenario == "scalars" ? 1_999_998 : 999_999;
            int itemBytes = scenario == "scalars" ? 1 : 2;
            byte[] body = new byte[7 + count * itemBytes];
            body[0] = 0x91; // Value.
            body[1] = 0x58; // Fixed-length, untyped list.
            body[2] = (byte)'I';
            BinaryPrimitives.WriteInt32BigEndian(body.AsSpan(3, 4), count);
            for (int offset = 7; offset < body.Length; offset += itemBytes)
            {
                if (itemBytes == 2) body[offset] = 0x79; // One-item list.
                body[offset + itemBytes - 1] = 0x90; // Integer zero.
            }
            return new Fixture(body, 1 + count * itemBytes, 1L + count * (itemBytes == 1 ? 2 : 4));
        }
        if (scenario != "json") throw new ArgumentException($"Unknown scenario: {scenario}.");

        // A single string in a list, repeated eight times using references to that list.
        // The 128 string-chunk headers and other wire bytes leave exactly an 8 MiB body.
        const int stringLength = 8 * 1024 * 1024 - 3 * 128 - 18;
        const int quoteCount = 79;
        byte[] shared = new byte[8 * 1024 * 1024];
        shared[0] = 0x91;
        shared[1] = 0x57; // Variable-length root list (ref 0).
        shared[2] = 0x79; // One-item list (ref 1).
        int position = 3;
        int remaining = stringLength;
        int quotes = quoteCount;
        while (remaining > 0)
        {
            int length = Math.Min(remaining, ushort.MaxValue);
            shared[position++] = remaining == length ? (byte)'S' : (byte)'R';
            BinaryPrimitives.WriteUInt16BigEndian(shared.AsSpan(position, 2), (ushort)length);
            position += 2;
            shared.AsSpan(position, length).Fill((byte)'x');
            int chunkQuotes = Math.Min(quotes, length);
            shared.AsSpan(position, chunkQuotes).Fill((byte)'"');
            quotes -= chunkQuotes;
            position += length;
            remaining -= length;
        }
        for (int index = 1; index < 8; index++)
        {
            shared[position++] = 0x51;
            shared[position++] = 0x91;
        }
        shared[position++] = (byte)'Z';
        if (position != shared.Length) throw new InvalidOperationException("Invalid JSON fixture length.");
        // Each quote becomes six-byte \u0022 in Result JSON; no JSON parse/copy is needed in the worker.
        return new Fixture(shared, 17, 8L * stringLength + 41 + 8L * quoteCount * 5);
    }
}
