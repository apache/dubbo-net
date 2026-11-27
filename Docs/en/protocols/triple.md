# Triple Java Interface Unary: HTTP/JSON and Binary Wrappers

[中文](../../zh/protocols/triple.md) · [English home](../index.md)

For a `tri://` Provider, `TripleDubboActuatorSuite` defaults to Java Interface unary HTTP/1.1 JSON. Explicit `Hessian2Wrapper` mode adds framed HTTP/2 Java generic invocation. Both modes have been exercised against Java Dubbo 3.3.6; other binary-provider versions are not claimed without interoperability tests.

## Request format

- HTTP method: `POST`.
- Path: `/{full Java interface name}/{method name}`.
- Content-Type: `application/json`.
- Body: a JSON array in method-parameter order. No arguments use `[]`; even a single argument remains an array, such as `[false]`.

Example:

```http
POST /com.example.DataTypeService/negate HTTP/1.1
Content-Type: application/json
tri-service-version: 1.0.0
tri-service-group: demo
tri-service-timeout: 10000

[false]
```

In the default HTTP/JSON mode, when metadata reveals multiple Java signatures, the client restricts selection to `dubbo://` before load balancing. With only `tri://` Providers, it returns a compatibility failure result. Explicit binary mode carries the exact signature and supports these overloads.

## Service routing and attachments

The actuator prefers `DubboInvocation.Version` and `DubboInvocation.Group`. When not explicitly set, it uses `version`, `group`, and `timeout` from the registered Provider URL. The corresponding Triple headers are `tri-service-version`, `tri-service-group`, and `tri-service-timeout`.

`DubboInvocation.Attachments` become HTTP headers. Attachments cannot override protocol-reserved headers, pseudo-headers, `Host`, `Content-Length`, or `Content-Type`.

For a 2xx response, the raw JSON body is preserved. For a non-2xx response, `QuerySuccess=false` and `ErrorMeaasge` contains both the HTTP status and Provider error body. Response headers are kept in `ResponseAttachments`.

## Deadline, cancellation, and addresses

- The Provider URL's `timeout` takes precedence over the default `DubboRequestTimeout` and can be longer or shorter; the minimum is 1 ms.
- The per-call deadline covers both response headers and the complete body. The internal `HttpClient.Timeout` is infinite so it does not compete with the per-call deadline.
- `SendQuery(invocation, cancellationToken)` returns `QuerySuccess=false` on caller cancellation and distinguishes cancellation from request timeouts in its error message. If both occur, caller cancellation takes precedence.
- For a directly constructed actuator, host/port constructors accept DNS names, IPv4, and bracketed or unbracketed IPv6. The `IPEndPoint` constructor also supports IPv6. ZooKeeper-discovered addresses follow the [registry module’s IP restriction](../architecture/registry.md).

## HTTP/JSON support

Supported:

- Dubbo 3.3+ Java Interface.
- Unary calls.
- HTTP/1.1 and `application/json`.
- Classic ZooKeeper interface-level discovery of `tri://` Providers.

Not supported:

- Protobuf/IDL services.
- Client/server/bidirectional streaming.
- Automatic negotiation of HTTP/2 or HTTP/3.
- Dubbo 3 application-level service discovery.

## Explicit binary mode

```csharp
using DubboNet.Clients;
using DubboNet.DubboService.DataModle;
using System.Threading;

using var client = new DubboClient("127.0.0.1:2181", new DubboClient.DubboClientConf
{
    TripleWireMode = TripleWireMode.Hessian2Wrapper,
    DubboRequestTimeout = 5_000,
    TripleMaxMessageBytes = 8 * 1024 * 1024,
    TripleUseTls = false // true uses HTTPS with normal certificate validation
});
var invocation = new DubboInvocation("com.example.EchoService", "echo",
    new[] { "java.lang.String" }, new object[] { "hello" })
{ Version = "1.0.0", Group = "demo" };
DubboRequestResult result = await client.QueryAsync(invocation, CancellationToken.None);
```

For a directly constructed actuator, set the same three Triple options on `DubboActuatorSuiteConf`. Configuration is captured at construction; it is client/actuator configuration, independently of shared registry consumer-registration settings.

Binary mode requires eligible `tri://` Providers and filters before load balancing, including for overloads. It does not select Native Dubbo or HTTP/JSON when binary invocation fails. A `tri://` URL alone is not proof of a Provider's codecs: the operator must enable Java generic invocation and Hessian2 on the Provider. There is no application-level RPC retry, protocol probing, downgrade, or replay after an uncertain failure.

### Wire contract and implementation

The request is `POST /{Java interface}/$invoke`, with HTTP/2 required exactly, `application/grpc+proto`, `te: trailers`, `generic: true`, service version/group headers and a remaining `grpc-timeout`. The body is a five-byte gRPC message prefix followed by the fixed Apache `TripleRequestWrapper` Protobuf schema. Its three independently Hessian-encoded arguments are the target method, its exact Java parameter-type array, and its generic argument array. The wrapper's `hessian4` name is Java Dubbo's wire alias for Hessian2.

This reuses the existing generic Java value preparation, including POJO maps, enum names and arrays. It does not put the classic Dubbo 16-byte frame or its entire `$invoke` request body inside a wrapper. Caller arguments/types retain their order. Nulls and overloaded signatures should use explicit Java types as in existing Native calls.

The binary encoder writes erased parameter types for Java method lookup. For example, `java.util.List<java.lang.Long>` becomes `java.util.List` in the wrapper. The erased type also guides value conversion, while the published metadata keeps its generic type details. Calls with no arguments and an empty type list can dispatch without a metadata lookup.

Binary selection does not serialize the complete argument list to JSON. Non-hash strategies do not need a request-content key. For `ConsistentHash`, the client asks the codec to prepare the bounded wrapper once, derives a Base64 SHA-256 digest of its exact bytes, and passes that key to the existing MurmurHash2 ring. The transport sends the same private snapshot, so ordinary single-use collections are not consumed twice. The key includes the Java method, exact types and argument values, and excludes HTTP trace/deadline metadata. The existing Native/HTTP-JSON key policy is unchanged; enabling binary mode can change affinity relative to those modes. POCO projection retains the existing JSON-based mapping and custom-converter behavior.

The public `TripleDubboActuatorSuite` owns the shared HTTP client. `Triple/BinaryTripleTransport.cs` owns one request/response, `TripleUnaryFraming.cs` owns bounded unary message reading, and `Hessian2WrapperCodec.cs` owns the fixed wrapper schema. .NET owns HTTP/2 sessions, TLS, flow control and stream identifiers. Google.Protobuf is a direct dependency already present in the Nacos graph; no generated user IDL or native gRPC runtime is needed.

### Status, exceptions and metadata

- Read the entire response through EOF before inspecting trailers. HTTP 200 alone is insufficient: `grpc-status` is mandatory, including for trailers-only errors. A response containing a message must carry status in its trailers; a leading status is accepted only for a trailers-only response without a message. Successful Unary requires exactly one message.
- Java can report a wrapped exception with `grpc-status=0` and nonzero `tri-exception-code`. These are failures, matching the Java client; the decoded exception is preserved in `Result`/`RawResult`. Ordinary business values are not classified by their `class` field.
- `QuerySuccess` remains the combined transport/protocol outcome. `GrpcStatusCode`, `TripleExceptionCode` and `ResponseHttpVersion` expose the actual binary response; native-only status fields remain null. Response metadata and trailers are retained separately in `ResponseAttachments` and `ResponseTrailers`.
- Hessian can return floating-point values such as NaN and infinity. Binary `Result` represents these as JSON strings (`"NaN"`, `"Infinity"`, `"-Infinity"`). `RawResult` retains the floating-point values, and a floating-point result model reads them back as numbers, including within collections.
- Attachments use lowercase ASCII metadata names and printable ASCII values. `-bin` values are Base64 strings (padded or unpadded input accepted), not strings to encode a second time. Received binary metadata stays Base64. Ordinary content headers such as `content-language` are forwarded as metadata. Reserved routing, framing, gRPC and connection headers cannot be overridden.
- Cookies in binary mode are scoped to the call. Pass any required `cookie` explicitly in the attachments. The response `set-cookie` is available in `ResponseAttachments` for the caller to handle.
- Missing/duplicate/malformed status, unsupported response content types/serialization, extra messages and truncated frames fail explicitly. Binary mode currently advertises only identity compression and rejects compressed messages.

### Deadline, connection lifetime and limits

Binary `DubboClient` calls use `DubboRequestTimeout` as a whole-call upper bound, including metadata and discovery waits. The client carries the remaining time on a private invocation copy and sends it as `grpc-timeout`. A shorter Provider timeout still bounds the RPC. The `QueryAsync(invocation, cancellationToken)` extensions in `DubboNet.Clients` provide the same whole-call bound in either mode. Existing instance overloads keep their precedence for calls such as `QueryAsync(null, default)`. The default HTTP/JSON path starts the Provider transport timeout after request JSON serialization and applies it through the full response body.

Direct actuator deadlines cover encoding, sending, headers and the complete response. Cancellation stops one HTTP/2 stream/response, not the shared client or registry operation. Cancellation is checked again after EOF and value/JSON decoding, so a synchronously completed read cannot hide an observed cancellation. Failure results distinguish caller cancellation, deadlines and actuator disposal. Interrupted calls are not replayed; later calls can establish/reuse a connection normally. Disposing an actuator cancels its in-flight calls and releases only the pool it owns; an injected shared client stays usable by other actuators. Client shutdown still respects the existing in-flight reservation lifetime. `MaxConnections` is a connection setting, not an RPC-stream concurrency limit.

`TripleMaxMessageBytes` defaults to 8 MiB per wrapper message, excluding the five-byte prefix. Request writing and response declared lengths are checked before unbounded buffer growth. Response Hessian values are limited to 64 nested levels. Like Native, binary Triple sizes its normalization budget from the actual Hessian data length: at most `min(2_000_000, max(100_000, 2 * dataBytes))` expanded nodes and `min(64 MiB, max(32 MiB, 8 * dataBytes))` estimated JSON bytes. Every repeated reference expansion is charged. The already normalized tree is serialized once, without another normalization pass. Metadata is limited to 8 KiB per header/trailer block.

Binary request preparation checks cancellation during library-controlled normalization and writing. Argument nesting is limited to 64 levels including the generic argument array, and normalization visits at most 2,000,000 containers/keys/values/references per call. JSON argument projection has its own equivalent traversal guard before recursively cloning children; its counter is separate so projection and normalization do not double-charge one logical tree. User getters and converters still use the existing synchronous POCO mapping. These traversal checks are explicit, rather than depending on a redundant whole-request JSON pass.

### Runtime and acceptance boundary

- Native .NET 8/10 assets and the Standard DLL hosted on .NET 6/8/10 exercise HTTP/2 and trailers. Standard-only API gaps are isolated in `Compatibility/HttpCompatibility.cs`; modern capabilities are bound there rather than assumed from the TFM.
- Legacy .NET Framework handlers are not a supported binary transport in this implementation. They return an explicit capability failure before sending, without affecting existing Native/HTTP-JSON support. Standard API compatibility does not imply HTTP/2 capability on every runtime/OS.
- h2c and HTTPS are available; HTTPS uses the runtime's normal certificate validation. No process-wide TLS setting or trust store is changed. HTTP/3, user Protobuf/IDL services, all Streaming modes and universal gRPC compatibility remain outside this phase.
- The Java 3.3.6 fixture produces an independent wrapper/Hessian golden payload. E2E adds all 25 methods over both ZooKeeper and Nacos and asserts HTTP/2 plus a gRPC status, so Native or JSON calls cannot satisfy binary coverage. Existing provider/version tests remain intact. Package smoke exercises a real packed binary call on .NET 6/8/10 and an explicit unsupported-platform result on Framework.
