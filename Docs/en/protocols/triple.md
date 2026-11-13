# Triple HTTP/JSON (Java Interface Unary)

[中文](../../zh/protocols/triple.md) · [English home](../index.md)

For a `tri://` Provider, `TripleDubboActuatorSuite` calls Dubbo 3.3+ Java Interface unary methods over HTTP/1.1 and JSON.

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

When a Java method has multiple signatures, the client restricts selection to `dubbo://` before load balancing if metadata reveals the overloads. With only `tri://` Providers, it returns a compatibility failure result. Non-overloaded methods can still use Triple HTTP/JSON.

## Service routing and attachments

The actuator prefers `DubboInvocation.Version` and `DubboInvocation.Group`. When not explicitly set, it uses `version`, `group`, and `timeout` from the registered Provider URL. The corresponding Triple headers are `tri-service-version`, `tri-service-group`, and `tri-service-timeout`.

`DubboInvocation.Attachments` become HTTP headers. Attachments cannot override protocol-reserved headers, pseudo-headers, `Host`, `Content-Length`, or `Content-Type`.

For a 2xx response, the raw JSON body is preserved. For a non-2xx response, `QuerySuccess=false` and `ErrorMeaasge` contains both the HTTP status and Provider error body. Response headers are kept in `ResponseAttachments`.

## Deadline, cancellation, and addresses

- The Provider URL's `timeout` takes precedence over the default `DubboRequestTimeout` and can be longer or shorter; the minimum is 1 ms.
- The per-call deadline covers both response headers and the complete body. The internal `HttpClient.Timeout` is infinite so it does not compete with the per-call deadline.
- `SendQuery(invocation, cancellationToken)` returns `QuerySuccess=false` on caller cancellation and distinguishes cancellation from request timeouts in its error message. If both occur, caller cancellation takes precedence.
- For a directly constructed actuator, host/port constructors accept DNS names, IPv4, and bracketed or unbracketed IPv6. The `IPEndPoint` constructor also supports IPv6. ZooKeeper-discovered addresses follow the [registry module’s IP restriction](../architecture/registry.md).

## Support

Supported:

- Dubbo 3.3+ Java Interface.
- Unary calls.
- HTTP/1.1 and `application/json`.
- Classic ZooKeeper interface-level discovery of `tri://` Providers.

Not supported:

- Protobuf/IDL services.
- Client/server/bidirectional streaming.
- Binary gRPC/Triple framing.
- Client-side negotiation of TLS, HTTP/2, or HTTP/3.
- Dubbo 3 application-level service discovery.
