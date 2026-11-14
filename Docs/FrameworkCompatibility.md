# Framework compatibility and validation

This change addresses [#11](https://github.com/apache/dubbo-net/issues/11).
It does not change the public invocation API or publish a package.

## Contract, runtime and SDK are different

The core package has three assets:

| Consumer | Normally selected asset | Validation scope |
| --- | --- | --- |
| .NET 10 | net10.0 | Full unit and provider-method E2E suites |
| .NET 8 | net8.0 | Full unit and provider-method E2E suites |
| .NET 9 | net8.0 | Compatible selection; not an independently tested runtime |
| .NET 6 | netstandard2.0 | Full provider-method E2E compatibility check, plus local-package Native/Triple smoke |
| .NET 7 | netstandard2.0 | Compatible selection; not an independently tested runtime |
| .NET Framework 4.8 | netstandard2.0 | Windows package smoke job for Native/Triple and body deadlines; see registry limitations below |
| Other Standard 2.0 implementations | netstandard2.0, if their full dependency graph is compatible | Not verified (including Mono/Unity/mobile/AOT) |

The full unit and E2E suites also explicitly select the **Standard DLL** on
.NET 8 and .NET 10. Merely running tests on .NET 10 would otherwise select the
modern DLL and leave compatibility code untested.
E2E target frameworks run sequentially: xUnit collection locks do not extend to
another host process, and these tests intentionally share stateful Java fixtures.
The workflow runs native .NET 10 first, then the Standard consumer on .NET 6,
then native .NET 8. The existing forced-Standard runs on .NET 10/8 remain.
Each run has separate log/TRX names; patch-only roll-forward prevents a missing
.NET 6 runtime from being replaced by a newer major during validation.

.NET Standard is a reference API contract, not a runtime to install.
The application supplies the actual runtime. Building the repository still uses
the .NET 10 SDK and a pinned C# 12 language version; examples remain net10.0.
Unit projects use net8.0 and net10.0; E2E also targets net6.0 as a Standard consumer.
Compiler-only init/record/nullability
markers for Standard are internal, not additions to the library's public API.

NuGet compatibility is not a vendor lifecycle promise. Prefer a runtime supported
by its vendor; running on an out-of-support runtime is the application's decision.
In particular, .NET 6 compatibility checks do not suppress SDK end-of-support warnings
or extend Microsoft's runtime support lifecycle.
These targets describe packages built from this branch, not historical NuGet releases.

## Dependency audit

All direct libraries have a Standard 2.0-compatible asset. The complete resolved
consumer graph, not just DubboNet's own target, must be checked.

| Dependency | Version | Reason |
| --- | --- | --- |
| DistributedLock.ZooKeeper | 1.0.0 | Existing registry/session support, unchanged |
| nacos-sdk-csharp | 1.3.10 | Existing Naming/Config SDK, unchanged; gRPC limitations below |
| NHessian | 0.4.3 | Existing Hessian binding, unchanged |
| Microsoft.Extensions.DependencyInjection | 8.0.1 | Directly used by MyNacos; avoid relying on an SDK transitive dependency |
| System.Text.Json | 8.0.6 (Standard only) | Explicit Standard dependency; modern assets use the shared framework without relying on package pruning to remove a redundant direct reference |
| Microsoft.Bcl.AsyncInterfaces | 8.0.0 (Standard only) | Existing public IAsyncDisposable contract |
| System.Memory | 4.5.5 (Standard only) | BinaryPrimitives/Span used by bounded protocol parsing |
| System.Threading.Tasks.Extensions | 4.5.4 (Standard only) | ValueTask on the Standard API surface |
| Microsoft.Extensions.DependencyModel | 8.0.2 (Standard only) | Prevent Nacos' old 3.1 dependency group from selecting Newtonsoft.Json 9.0.1 on Framework |

That last difference is visible only when restoring a **real Framework consumer**.
Auditing the netstandard2.0 project alone does not discover every package that a
Framework consumer's NuGet graph will choose. The package smoke restores treat
NU1901–NU1904 advisory warnings as errors. Do not solve advisory failures by
suppressing audit warnings.

Nacos still brings some older Microsoft.Extensions 3.1 packages in its Standard
dependency group. A clean advisory scan is not a claim that those upstream packages
remain supported; broader SDK/dependency modernization is separate work. Modern
assets keep the SDK's modern dependency group.

## Compatibility implementation and behavior

Compatibility code is internal under [Compatibility](../Compatibility).
Non-obvious call-site changes also have comments.

- **Waiting:** the Standard Task.WaitAsync equivalent abandons only the wait,
  never cancels a shared registry operation. Timers/registrations are disposed;
  late failures are observed; the caller's cancellation token is retained.
- **TCP:** old array-based async APIs use the existing buffer, not a copied one.
  Legacy I/O waits remain bounded even if an implementation ignores its token.
  Only an unpublished connect attempt is disposed to cancel connection setup.
  A partial-frame write still faults its channel; cancelling an ordinary RPC
  does not dispose another caller's shared connection.
- **HTTP:** net8/net10 keep SocketsHttpHandler, memory-based I/O and native
  cancellation APIs. A Standard DLL on modern .NET binds the same handler and
  cancellation-aware HttpContent method at runtime. Legacy HttpClientHandler
  retains per-handler maximum connections, but has no equivalent per-handler
  idle-eviction control: MasterConnectionAliveTime follows runtime defaults
  there, with a warning. No global ServicePointManager/TLS settings are changed.
- **HTTP body deadlines:** the legacy fallback disposes only its owned response
  and bounds its read wait. It retains HttpContent charset/BOM decoding, never
  cancels/disposes the shared HttpClient or ignores certificate validation.
- **Load balancing:** the Standard random source uses per-thread state and
  unbiased 64-bit rejection sampling, preserving long cumulative weights.
  Time intervals use a monotonic clock, not wall time or wrapping TickCount.
- **Collections:** removing a discovery task by key/value remains atomic, so an
  older caller cannot remove a newer task. Dictionary copies retain ordinal keys.
- **Protocol parsing:** numeric endpoint parsing never performs DNS; trimmed
  splitting is tested against the runtime's semantics; Hessian chunk copies are
  bounded, allocation-free array writes with existing size/depth budgets.
- **TCP keepalive:** Standard uses named runtime options when present. Older
  runtimes without those options retain OS keepalive timings/retries and log the
  limitation, rather than guessing OS-specific numeric constants.
- **Packaging:** multi-target outer builds explicitly include LICENSE/NOTICE and
  historical licensing documents. Local artifact/package caches are excluded
  from the root source glob (some dependencies contain .cs files).

Runtime light-up is isolated to the Standard path. Trimming/NativeAOT and private
reflection policies have not been validated; do not infer their support from the TFM.

## Registry and Framework boundaries

ZooKeeper uses its existing session/ephemeral-node behavior. Consumer registration
remains optional, shared per registry, and runtime-reconfigurable; retry, session
recovery and shutdown unit tests run for each library asset.

Nacos V2 uses Grpc.Net.Client and bidirectional streaming to its gRPC server port
(in addition to HTTP port 8848). A netstandard2.0 assembly does **not** make
HTTP/2/gRPC streaming work on every .NET Framework/Windows combination.
The existing SDK does not automatically configure the Windows handlers/TLS needed
by restricted legacy environments. Full Framework + Nacos 2 Naming/Config support
is therefore **not claimed**, nor is Framework + ZooKeeper certified by the
Native/Triple loopback smoke job. Test the application's exact registry/runtime/OS
before rollout.

This Nacos gRPC restriction is separate from DubboNet's Triple transport:
Triple here is Java-interface unary **HTTP/1.1 JSON**, not framed gRPC.

References: [Microsoft .NET Standard guidance](https://learn.microsoft.com/en-us/dotnet/standard/net-standard),
[Microsoft gRPC supported platforms](https://learn.microsoft.com/en-us/aspnet/core/grpc/netstandard?view=aspnetcore-10.0),
[Nacos SDK gRPC implementation](https://github.com/nacos-group/nacos-sdk-csharp/blob/7e08305f0acac13bf9db5ecfcde925ee87401c36/src/Nacos/V2/Remote/GRpc/GrpcClient.cs).

## Reproducing validation

Normal builds require the .NET 10 SDK; unit tests require .NET 8/10 runtimes,
and the full E2E suite also requires the .NET 6 runtime:

```bash
dotnet msbuild Tests/PackageCompatibilitySmoke/Prepare.proj
dotnet build DubboNet.sln -c Release -p:GeneratePackageOnBuild=false
dotnet test Tests/UnitTestForDubboNet/UnitTestForDubboNet.csproj \
  -c Release -p:GeneratePackageOnBuild=false
dotnet test Tests/UnitTestForDubboNet/UnitTestForDubboNet.csproj \
  -c Release -p:DubboNetTestTargetFramework=netstandard2.0 \
  -p:GeneratePackageOnBuild=false
```

The test-only selector restricts the library's advertised targets to one, so
restore chooses a consistent transitive graph. Test binaries and obj files are
isolated by selected library target. Unit tests and all network test/example
programs assert the loaded assembly's TargetFrameworkAttribute before doing RPC.
Packing with the selector set is rejected: release packages must contain all assets.

After starting the local fixture environment, run the E2E project normally
(sequentially .NET 10, .NET 6, .NET 8) and force Standard on the modern hosts:

```bash
export DUBBONET_E2E_ENABLED=true
export DUBBONET_E2E_ZOOKEEPER_ADDRESS=127.0.0.1:2181
export DUBBONET_E2E_NACOS_ADDRESS=nacos://127.0.0.1:8848
dotnet test Tests/DubboNetEndToEndTests/DubboNetEndToEndTests.csproj \
  -c Release -p:GeneratePackageOnBuild=false
for framework in net10.0 net8.0; do
  dotnet test Tests/DubboNetEndToEndTests/DubboNetEndToEndTests.csproj \
    -c Release -f "$framework" -p:DubboNetTestTargetFramework=netstandard2.0 \
    -p:GeneratePackageOnBuild=false
done
```

Use explicit runtime arguments/environment variables for any non-local registry.
No public test IP is a project, documentation or CI default.

A smoke project consumes the **actual packed NuGet**, never a project reference.
It is included in the solution for IDE use. On a fresh checkout, prepare its local
package **before** solution/IDE restore. Run preparation again after library edits
when checking the new package; ordinary restore intentionally does not compile or pack:

```bash
dotnet msbuild Tests/PackageCompatibilitySmoke/Prepare.proj
dotnet restore DubboNet.sln
dotnet run --project Tests/PackageCompatibilitySmoke/PackageCompatibilitySmoke.csproj \
  -c Release -f net8.0 --no-restore -- net8.0
dotnet run --project Tests/PackageCompatibilitySmoke/PackageCompatibilitySmoke.csproj \
  -c Release -f net10.0 --no-restore -- net10.0
# Optional, requires the .NET 6 runtime:
dotnet run --project Tests/PackageCompatibilitySmoke/PackageCompatibilitySmoke.csproj \
  -c Release -f net6.0 --no-restore -- netstandard2.0
# Windows only, actual Framework execution:
dotnet build Tests/PackageCompatibilitySmoke/PackageCompatibilitySmoke.csproj \
  -c Release -f net48 --no-restore
Tests/PackageCompatibilitySmoke/bin/Release/net48/PackageCompatibilitySmoke.exe netstandard2.0
```

Preparation works on Windows/macOS/Linux with the .NET 10 SDK. It reads the core
version, packs all three assets under a new private prerelease identity, writes
`artifacts/compatibility/PackageVersion.props`, and restores the consumer with
advisory warnings treated as errors. Unique identities prevent NuGet from reusing
an older DLL cached under the same validation version; no global cache is cleared.

The project explicitly selects its own `NuGet.Config` even during solution restore,
adds the local feed and a project-private package cache, and maps the exact
`DubboNet` ID exclusively to that feed. Other dependencies come from nuget.org.
It does not change user/global NuGet settings, publish anything, or fall back to a
public release. Missing local packages fail with the preparation command.
VS Code uses the same settings without needing command-line source overrides;
if an old diagnostic remains after preparation, reload the project/window.
See [NuGet source-mapping behavior](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping).

Each package smoke verifies selected asset identity, Native Hessian2 and Triple
HTTP/JSON round trips, and a stalled HTTP body deadline, all against loopback.
The Windows job must execute successfully before claiming Framework runtime
validation; cross-compiling net48 on macOS is not execution.

Build and test keeps its existing required job name and adds full Standard unit
tests and package smoke checks. E2E retains temporary local Java/registry fixtures,
existing method cases, examples, logs and unconditional cleanup, and adds Standard
runs. Its .NET 6 check also executes the prepared real NuGet consumer, covering
package dependency resolution as well as project-reference behavior. Both workflows
and the local E2E runner prepare the package before restoring
the solution. No existing tests are skipped or removed.

## Local validation snapshot (2026-11-11)

On macOS arm64 with SDK 10.0.201 and the installed .NET 6/8/10 hosts:

- Full unit suite: **592 passed, 0 failed, 0 skipped in each of four combinations**
  (net8/net8, net10/net10, net8/Standard, net10/Standard). All 556 original cases
  remain; 36 compatibility cases were added.
- Provider-method E2E: **156 passed, 0 failed, 0 skipped in each of five combinations**:
  net10/net10, net6/Standard, net8/net8, net10/Standard, net8/Standard. These were
  rerun through the permanent E2E project against explicitly supplied remote
  ZooKeeper/Nacos fixtures, with hosts run sequentially. Testhost diagnostics
  confirmed .NET 10.0.5 / 6.0.36 / 8.0.15, and generated metadata/dependency graphs
  confirmed the .NET 6 consumer selected Standard; no runtime-major substitution.
- Standard integration executable: ZooKeeper **54/54**, Nacos **32/32**.
  QueryDemo: **12/12** for each registry with Standard and net10 library assets.
- Separate, uncommitted remote smoke: Standard consumer registration, shared
  client leases, disable/re-enable without new RPC, and final deregistration passed
  on both registries. Only uniquely identified test consumers were inspected;
  their absence was verified during final cleanup.
- Actual local NuGet consumers on .NET 6/8/10 passed asset selection, Native/Triple
  round trips and stalled-body deadlines. Framework 4.8 cross-compilation passed.
  The full consumer dependency graph, including Framework, had no reported NuGet
  advisories at validation time; the known Newtonsoft.Json 9.0.1 path was removed.
- Each library asset retained **61 exported types / 1041 public/protected
  declarations**, with no differences from the original public surface.
  The package contains all three DLL/XML pairs and licensing documents, not test
  sources or package-cache sources. Partial-target packing is rejected.

Windows execution and Docker-based CI were **not run on this macOS machine**.
The newly configured Windows smoke and temporary-container E2E workflow still
need their real GitHub runs; local validation is not represented as a CI approval.

### Package restore / IDE follow-up

Project-local source configuration and explicit preparation were verified with
ordinary solution restore/build (the smoke project included), repeated preparation
with distinct package identities, and .NET 6/8/10 package execution. Native unit
suites still passed 592/592 on both .NET 8 and .NET 10.

A source-only checkout under a path containing spaces produced the expected
actionable error before preparation, then prepared its own new DubboNet package
and restored/built the solution successfully. Its initial fully uncached dependency
download hit upstream NuGet EOF failures; the successful retry reused trusted
third-party dependency caches, but deliberately did not copy any DubboNet package.
No cache deletion, signature bypass or public-release substitution was used.

### Review follow-up validation (2026-11-14)

The JSON package reference is now explicitly Standard-only. Both the default pack
and a pack with `RestoreEnablePackagePruning=false` retain System.Text.Json 8.0.6
in the Standard dependency group and omit it from the modern groups. This does
not alter dependency declarations made by Nacos itself.

Empty/null string separators now follow the BCL string-array overload without
entering a non-advancing loop. Telnet's own connection deadline again retains the
original duration-bearing error and state-report messages; cancellation semantics
of the shared socket helper are unchanged. Twenty additional cases cover these
boundaries, unrelated cancellation, ordinary connection failure and a real
loopback connection.

- Full unit suite: **622 passed, 0 failed, 0 skipped in each of four combinations**
  (net8/net8, net10/net10, net8/Standard, net10/Standard).
- Provider-method E2E: **156 passed, 0 failed, 0 skipped in each of five combinations**
  (net10/net10, net6/Standard, net8/net8, net10/Standard, net8/Standard).
- The prepared NuGet consumers on .NET 6/8/10 passed asset selection, Native/Triple
  round trips and stalled-body deadlines. Framework 4.8 cross-compilation passed;
  Framework runtime execution still requires Windows CI.
- Standard and modern assets retain the same **61 exported types / 1041
  public/protected declarations**, with no public API differences.

These are local results; the updated commit must also pass its GitHub checks.
