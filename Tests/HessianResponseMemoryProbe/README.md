# Hessian response peak-memory probe

This manual experiment measures the public `NativeDubboActuatorSuite.SendQuery()`
path: TCP receive, Hessian2 decoding, normalization, and final JSON serialization.
It adds no memory assertion to CI and needs no registry, Docker, or Java provider.

From the repository root, build once and run without a debugger:

```powershell
dotnet build Tests/HessianResponseMemoryProbe/HessianResponseMemoryProbe.csproj -c Release -p:GeneratePackageOnBuild=false
dotnet Tests/HessianResponseMemoryProbe/bin/Release/net10.0/HessianResponseMemoryProbe.dll --iterations 3 --output artifacts/hessian-memory.json
```

For a smaller run, select one workload and concurrency level:

```powershell
dotnet Tests/HessianResponseMemoryProbe/bin/Release/net10.0/HessianResponseMemoryProbe.dll --scenario containers --concurrency 4 --iterations 1 --output artifacts/hessian-memory-containers.json
```

The default matrix runs three workloads at concurrency 1, 2, and 4, with three
fresh client processes per combination. Requests and loopback providers have
bounded timeouts; failed requests, unexpected JSON sizes, and worker failures
cause a nonzero exit. Progress goes to stderr and the complete report goes to
stdout and the optional output file. Record `git rev-parse HEAD`, CPU model,
physical RAM, and any local modifications alongside a report.

| Scenario | Response body bytes | Normalized nodes | JSON UTF-8 bytes per result |
| --- | ---: | ---: | ---: |
| `scalars` | 2,000,005 | 1,999,999 | 3,999,997 |
| `containers` | 2,000,005 | 1,999,999 | 3,999,997 |
| `json` | 8,388,608 | 17 | 67,108,849 |

`scalars` is a flat list of integer zeros. `containers` holds 999,999 separate
one-item lists containing zero, exposing container overhead at the same node
count. `json` repeats a one-item list containing a large chunked string eight
times through shared references. Its encoded quotes place the JSON 15 bytes
below 64 MiB and the conservative budget charge 5 bytes below that ceiling.
All fixtures use literal wire bytes independent of production tag constants.

The parent process builds the fixture and serves TCP responses; its memory is
excluded from the measured child. Each child warms up one small query, performs
a full GC before taking its baseline, then starts the workload with one
connection per response. The provider waits until every measured request has
arrived before releasing replies. Results retain both `RawResult` and `Result`
until all requests finish and the memory metrics have been read. Synchronizing
arrivals creates overlapping outstanding requests; it does not guarantee that
all decoder instructions run simultaneously on every CPU/runtime.

Metrics have different meanings:

- `ProcessPeakWorkingSetBytes` is the OS-reported lifetime high-water mark for
  resident process memory, including startup and warmup, native/runtime memory,
  and managed data. `BaselineWorkingSetBytes` is a warmed-up snapshot. Their
  difference is descriptive, not an exact isolated workload allocation.
- `SampledManagedPeakBytes` is the largest `GC.GetTotalMemory(false)` observation
  from a dedicated 5 ms sampling thread and the completion snapshot. It can miss
  short-lived peaks and is not the exact managed-heap peak or committed GC size.
- `TotalAllocatedBytes` is cumulative managed allocation during the workload,
  including probe bookkeeping. It is not peak live memory.
- `ManagedBytesAtCompletion` includes retained results and any uncollected
  garbage. No forced collection occurs during the measured requests.

Runtime, operating system, architecture, logical CPU count, GC mode, and explicit
GC environment settings are included in each sample. Use the same configuration
for comparisons and report ranges across repeated runs. The 64 MiB JSON budget
is not a managed-heap limit; these selected structures illustrate resource cost
without proving a universal worst-case memory bound for all valid responses.

## Results

Measured on 2026-11-15 with production code at
`dca50df6a807c98c288f644b3d0354e3eff1236a`, plus the boundary tests and this probe
added in the working tree. No production code or budget constants changed for
this experiment. Environment: Windows 11 build 26200, Intel Core i9-14900HX,
32 GB installed RAM (31.6 GiB usable physical memory), 24 logical processors
reported by .NET, AC power, SDK 10.0.403, runtime .NET 10.0.14 x64, workstation
GC with Interactive latency mode and no explicit GC environment overrides.
The commands above ran in Release without a debugger. All 27 samples passed
their query, node-count, and JSON-size checks.

Ranges below are the minimum and maximum across three fresh client processes
per combination, in MiB (1,048,576 bytes). Warmed-up baseline working set was
34.7-35.3 MiB; the peak figures include that baseline.

| Scenario | Concurrent responses | Process peak working set (MiB) | Sampled managed peak (MiB) |
| --- | ---: | ---: | ---: |
| `scalars` | 1 | 145.8-146.0 | 111.0-111.0 |
| `scalars` | 2 | 255.5-255.7 | 221.4-221.4 |
| `scalars` | 4 | 477.5-477.8 | 442.5-442.6 |
| `containers` | 1 | 272.5-272.8 | 218.3-218.3 |
| `containers` | 2 | 493.5-515.1 | 430.3-430.6 |
| `containers` | 4 | 962.7-963.4 | 860.5-860.8 |
| `json` | 1 | 337.7-337.8 | 504.5-504.5 |
| `json` | 2 | 669.9-670.2 | 1,008.5-1,008.5 |
| `json` | 4 | 1,287.6-1,304.5 | 2,016.5-2,016.5 |

The JSON scenario shows why these metrics must remain separate: logical managed
allocation includes uncollected objects and buffer space, while working set
counts resident pages. A managed estimate can exceed resident process memory.
Neither column is a memory reservation or an upper bound for other inputs.

At the same near-2M node count, small containers use substantially more memory
than scalar values. Four near-ceiling JSON results also consume over 1.2 GiB
of resident process memory in this environment. The current caps therefore
permit appreciable per-response resource use, and memory-constrained consumers
need to account for concurrent requests as well. These observations document
the cost of the chosen caps; they do not establish universally safe defaults
or a universal worst-case heap bound.
