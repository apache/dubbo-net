# Transport: How Actuators Send and Receive

[Call-path overview](index.md) · [中文](../../zh/architecture/transport.md)

## Responsibility

Transport receives the selected actuator and a `DubboInvocation`, encodes the call for its protocol, manages connections and responses, and returns a `DubboRequestResult`. It owns **protocol I/O**. [Registry discovery](registry.md) supplies addresses; [service selection](selection.md) chooses an endpoint and maintains the reservation. Transport actuators do not own a service's candidate set or load-balancing strategy.

## From selection to protocol actuator

After selecting an actuator, `DubboClient.QueryInternalAsync` wraps dispatch in `LoadBalancedInvocation.InvokeAsync`. Structured actuators receive `IDubboGenericInvocationActuator.SendQuery(invocation, cancellationToken)`; the older interface receives serialized arguments and a method path. The wrapper completes statistics and releases the reservation when the actual send ends. Cancelling an outer wait does not prematurely release an in-flight reservation.

| Protocol | Actuator work | Details |
| --- | --- | --- |
| `dubbo://` | `NativeDubboActuatorSuite` manages TCP connections, Hessian2 request encoding, and responses. | [Native Dubbo protocol](../protocols/native-dubbo.md) |
| `tri://` | `TripleDubboActuatorSuite` handles default HTTP/JSON or explicit Java Unary HTTP/2 wrapper/Hessian2, deadlines, status/trailers and cancellation. | [Triple protocol](../protocols/triple.md) |

Normal registry discovery turns only `dubbo://` and `tri://` addresses into Provider endpoints. The driver's Telnet fallback for other schemes is not the short-lived Telnet `ls -l` used by [metadata](metadata.md) to read definitions.

Pre-dispatch errors such as unresolved signatures are different from `DubboRequestResult` failures returned by an actuator. Call outcomes include a success status, result or error, and request duration; actuators also retain response attachments when available.

## Connections and release

Actuators hold their own connections or pools and handle configured request timeouts and other I/O concerns. Service drivers may reuse actuators. A reservation protects an in-flight call until dispatch completes, and shared-resource references determine when a connection can be released. Client shutdown releases the actuator resources it holds.

## Source entry points

| File | Responsibility |
| --- | --- |
| `NativeDubboActuatorSuite.cs` | Native Dubbo requests, responses, and connection lifetime. |
| `TripleDubboActuatorSuite.cs` | Triple requests, responses, cancellation, and HTTP connection resources. |
| [BinaryTripleTransport.cs](../../../DubboService/Triple/BinaryTripleTransport.cs) | HTTP/2 request/response ownership, metadata, RPC status and Java exception indicators. |
| [TripleUnaryFraming.cs](../../../DubboService/Triple/TripleUnaryFraming.cs) | Bounded five-byte-prefix messages, partial reads and Unary cardinality. |
| [Hessian2WrapperCodec.cs](../../../DubboService/Triple/Hessian2WrapperCodec.cs) | Fixed Apache wrappers and Java generic argument encoding. |
| `LoadBalancedInvocation.cs` | Statistics and reservation release around a transport call. |
