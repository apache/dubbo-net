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

This Nacos gRPC restriction is separate from DubboNet's Triple transport. Default
Triple remains Java-interface unary HTTP/1.1 JSON. Explicit binary Java wrappers
use HTTP/2 and trailers on modern hosts; the current implementation rejects legacy
Framework handlers before sending. See the [binary Triple contract](en/protocols/triple.md#explicit-binary-mode).

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
Modern hosts additionally execute a real HTTP/2 binary-wrapper round trip;
Framework verifies the explicit unsupported-platform result. Kestrel is a
test-only framework reference, not a dependency of the core package.
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

### Binary Triple Unary validation (2026-11-21)

Phase one adds Java-interface generic `$invoke`, fixed Triple Protobuf wrappers
and Hessian2 over HTTP/2. It does not add user Protobuf/IDL contracts or streaming.
HTTP/JSON remains the default; binary mode is an explicit construction option.

Local verification on macOS arm64, SDK 10.0.201:

| Verification | Library asset / host | Result |
| --- | --- | --- |
| Full unit suite | net8/net8, net10/net10, Standard/net8, Standard/net10 | 693 passed per combination, no failures or skips |
| Full Provider-method E2E | net10/net10, Standard/net6, net8/net8, Standard/net8, Standard/net10 | 206 passed per combination, no failures or skips |
| Real packed NuGet smoke | Standard/net6, net8/net8, net10/net10 | Native, HTTP/JSON, body deadline and real binary HTTP/2 round trips passed |
| Solution restore/build | All solution projects, Release | Passed, including Framework 4.8 cross-compilation |
| Java wrapper/Hessian contract | Apache Dubbo 3.3.6 fixture | Independent golden-byte test passed |

E2E retains the existing 156 cases and adds 50 cases: all 25 Java 3.3.6 methods
through each of ZooKeeper and Nacos. Binary cases assert HTTP/2 and a gRPC status;
Native or HTTP/JSON cannot accidentally satisfy them. Temporary local Java
Providers/ZooKeeper and an explicitly supplied Nacos registry were used, with
unique registry groups and sequential host runs. No remote addresses are defaulted
in source. The 3.3.6 fixture is the current binary interoperability boundary;
older versions remain covered for their existing transports.

Loopback HTTP/2 tests additionally verify cancellation of one stream while
concurrent/later calls use the same connection, stalled-body deadlines, interrupted
responses without RPC replay, TLS with a fixture-only certificate trust callback,
all six load-balancing modes filtering to Triple, malformed/bounded frames and
metadata, Java wrapped exceptions, and typed-result metadata preservation.

The full end-to-end workflow runs these cases on its existing .NET 6/8/10 and
forced-Standard paths. The Java build now also runs the golden-byte test. Existing
workflow triggers and required job names are unchanged. Windows Framework runtime
execution and Docker/GitHub workflows were not run locally; their checks are still
required. Framework binary mode is explicitly unsupported and its Windows smoke
checks that capability result while retaining Native/HTTP-JSON coverage.

### Main synchronization follow-up (2026-11-27)

The Triple work was reapplied onto Apache main `4e3741e`, including metadata/registry
ownership separation (#36), payload-sized Hessian expansion budgets (#43), and
the Java setup Action update (#46). Client calls now use the extracted metadata
resolver and Provider discovery entry points while retaining whole-call deadlines
and caller cancellation. Binary Triple uses the same payload-sized normalization
policy as Native and serializes the checked tree without a second normalization
pass. Five additional wrapper cases cover large compact lists and shared-reference
node/JSON expansion boundaries. The new ZooKeeper discovery E2E case uses the
test catalog's configurable registry root, including its required trailing slash.

After synchronization, the full unit suite passed **746/746** in each of the four
native/Standard combinations above. Provider-method/discovery E2E passed
**207/207** in each of the five runtime/asset combinations, with no failures or
skips. The first local E2E attempt advertised the VPN interface address and was
aborted; these passing results came from the subsequent physical-LAN fixture run.
The full solution restored and built successfully, and freshly packed NuGet
consumers passed on .NET 6/8/10. Temporary Java/ZooKeeper processes were stopped;
the isolated Nacos test groups were checked for zero remaining instances and
their test metadata was removed. Windows runtime and hosted CI execution remain
outside this local verification.

### Triple boundary review follow-up (2026-11-27)

The review reproduced and fixed cancellation at synchronous response EOF being
reported as success, and message-bearing responses incorrectly accepting a
leading `grpc-status` without trailing status. It also found a disposal timing
window after response headers: closing the HTTP pool alone did not always stop a
stalled body. The actuator now links calls to its own disposal token, cancels only
its own requests, retains caller-cancellation precedence, and leaves an injected
shared HTTP client usable by other actuators. Disposal is idempotent.

Four regression cases cover these boundaries, including real HTTP/2 stalled-body
disposal and isolation between two actuators using one shared client. Request
wrapper encoding also removes an intermediate `ToArray` copy by copying only the
written buffer range into `ByteString`; the golden request/response contracts and
message limits remain unchanged.

After these fixes, local Release validation passed **750/750** unit cases in each
of the four native/Standard combinations, **207/207** E2E cases in each of the five
runtime/asset combinations (including **50/50** binary Triple cases per run), and
fresh NuGet package smoke on .NET 6/8/10. Solution restore/build passed. Temporary
fixtures and isolated remote test records were cleaned up. These are local results;
hosted CI and Windows runtime verification have not been claimed.

### Binary request preparation review (2026-11-27)

Eighteen regression cases initially failed: client selection serialized binary
arguments to JSON before dispatch, consuming single-use collections and rejecting
Hessian-supported scalar values such as a `double.NaN` request. Java source-style
arrays and some collection declarations also entered a POJO projection that
enumerated and discarded their JSON representation. Binary non-hash selection
now avoids the whole-request JSON pass. Consistent hash prepares one bounded
wrapper, derives a Base64 SHA-256 digest of those exact bytes, and reuses the
private snapshot for dispatch. Its key includes the method, Java types and
arguments, excluding HTTP metadata. Native/HTTP-JSON retain their existing key
policy; switching wire modes can change affinity.

Generic argument preparation now recognizes collection serializer shapes without
consuming the sequence, and does not project an already converted `JsonElement`
again. Binary preparation checks cancellation, depth and a two-million-visit
budget before recursively cloning JSON children or normalizing argument values.
Projection and normalization have separate counters to avoid charging the same
logical tree twice. Existing synchronous user getters/custom converters retain
their original POCO mapping behavior.

The 52 added unit cases cover single-use arguments across all six load-balancing
modes, actual wire values, deterministic selection across two HTTP/2 nodes,
pre-dispatch cancellation, oversized payloads, deep JSON and large projections.
Existing Java collection/array E2E calls now also use single-use enumerables;
four additional real-Java binary consistent-hash cases cover arrays and lists
through both registries.

Final local Release verification:

| Verification | Library asset / host | Result |
| --- | --- | --- |
| Full unit suite | net8/net8, net10/net10, Standard/net8, Standard/net10 | 802 passed per combination, no failures or skips |
| Full E2E suite | net10/net10, Standard/net6, net8/net8, Standard/net8, Standard/net10 | 211 passed per combination, including 54 binary cases; no failures or skips |
| Freshly packed NuGet smoke | Standard/net6, net8/net8, net10/net10 | Native, HTTP/JSON, body deadlines and binary HTTP/2 passed |
| Solution restore/build | All solution projects, Release | Passed, including Framework 4.8 cross-compilation |

An earlier E2E attempt had nine existing Nacos/Java 3.2.20 Provider discovery
failures during a registry connection reconnect; all binary cases in that host
passed. That failed attempt is not counted as a passing run. The final complete
rerun above used prebuilt assets and ready Java fixtures, without skipping cases
or introducing RPC retry. All owned Java/ZooKeeper processes were stopped and
isolated Nacos groups were checked for zero active instances and cleaned. These
results are local; hosted CI and Windows runtime still require their own runs.

### Binary invocation entry points (2026-11-27)

An inferred call to Java 3.3.6 `listUsers` originally failed because the metadata
type `java.util.List<java.lang.Long>` reached Java's method lookup verbatim.
Binary wrapper encoding now erases generic type arguments before preparing the
wire signature and argument values. ZooKeeper and Nacos E2E cases exercise the
public inference overload with single-use collections and verify the returned IDs.
Loopback tests cover dispatching an exact empty signature without a metadata read,
and forwarding `content-language` while reserving `content-encoding`.

The four local unit combinations each passed **803** cases. The five E2E
combinations each passed **213** cases, including **56** binary Triple calls per
combination. Tests ran on macOS with local Java Providers and ZooKeeper plus the
supplied Nacos registry. The temporary processes and isolated registry records
were cleaned up. Release solution build and freshly packed NuGet smoke on
.NET 6/8/10 also passed. Windows runtime and GitHub Actions remain pending.

### Triple response and timeout review (2026-11-28)

The Java 3.3.6 fixture returns NaN when asked to average an empty array. That
result originally decoded successfully with gRPC status 0 and then failed while
producing JSON. Binary Triple now represents non-finite values as JSON strings
and parses them back into typed floating-point models. Loopback HTTP/2 also
confirmed that the owned handler forwarded a response cookie on the next call.
Binary calls now carry only explicitly supplied cookies.

A slow serializer reproduced a change to the default HTTP/JSON timeout. Its
Provider transport timer again starts after request serialization. The network
deadline tests wait for the fixture to receive the request or send the response
headers before checking cancellation at the respective stage.

Local Release verification passed **806/806** unit tests in each of the four
library asset and runtime combinations. Each of the five end-to-end combinations
passed **215/215** tests, including **58** binary cases and the NaN result through
both ZooKeeper and Nacos. Solution build and fresh NuGet package checks on
.NET 6/8/10 passed. These results are local to macOS. Hosted CI and Windows
runtime validation remain pending.

### Clean source review (2026-11-28)

Compiling `QueryAsync(null, default)` and its typed equivalent against Apache main
and the candidate exposed a change in overload selection. The candidate picked
the new cancellation overload instead of the existing default-endpoint call.
The new cancellation entry points now use extension methods in `DubboNet.Clients`.
An external consumer of the freshly packed NuGet confirmed that both legacy
expressions resolve as they did on main and that the explicit cancellation call
resolves to the extension.

With an isolated registry prefix, the previous readiness probe searched the
default root and failed. The fixture launch, readiness probe, integration
program, xUnit suite and QueryDemo now share the prefix. The local fixture
runner also executes the Java wrapper contract test.

The clean source build passed **807/807** unit cases in each of four combinations
and **215/215** E2E cases in each of five combinations, including **58** binary
cases per E2E run. The JDK 8 build passed its wrapper contract test. Both registry
readiness checks and all eight integration and demo runs passed across the
native and Standard assets. The package smoke passed on .NET 6/8/10. Fixtures
and their isolated registry records were cleaned up after local verification.
