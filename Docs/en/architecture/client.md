# Client: Call Entry and Orchestration

[Call-path overview](index.md) · [中文](../../zh/architecture/client.md)

## Responsibility

`DubboClient` receives `QueryAsync` calls, turns the service name, method, arguments, and options into an invocation, and coordinates [metadata](metadata.md), [registry discovery](registry.md), [service selection](selection.md), and [transport](transport.md). It decides when to hand work to each module; it does not itself own Java method definitions, implement load balancing, or perform protocol I/O.

The entry points accept a service and method with arguments or a `DubboInvocation` that also carries Java types, version, group, and attachments. The client preserves those call details. Call outcomes are represented by a `DubboRequestResult` containing a success status, result, or error message; invalid arguments or signatures can still throw before dispatch.

## Handoff for a call

The public `QueryAsync` overloads lead to `QueryInternalAsync`. If the caller has not specified Java parameter types, the client first resolves the method signature through metadata and builds a `DubboInvocation`. With explicit types, it can build the invocation directly, although it may still make a best-effort check for overloads that require a particular protocol. A signature-resolution error occurs before dispatch.

`QueryInternalAsync` asks `DubboDriverCollection` for an actuator matching the protocol requirement and reserves it for this call. With no service driver, the client invokes registry discovery; if it returns no error, the endpoint list is given to a new driver before another selection attempt. An empty list does not lead to dispatch. With an existing driver but no Provider endpoints, the client actively looks up Providers and updates the driver only if new endpoints are found. If candidates exist but no actuator is available, it returns a failure rather than interpreting this as “no Provider in the registry.” After selection, the client hands the call to the actuator; `LoadBalancedInvocation` wraps dispatch and releases the reservation on completion.

## Changes and shutdown

Provider notifications enter through the ZooKeeper watcher or Nacos callback. An existing driver is refreshed from discovery; a metadata-only cache is refreshed for definitions. Notifications update existing state rather than issuing another business call. On ZooKeeper disconnection or session expiration, the client attempts to reconnect and rediscovers services after recovery.

`Dispose` ends the client's consumer registration, removes its Nacos Naming subscriptions, and releases the metadata manager, drivers, actuators, and registry connection references. The metadata manager releases its own Nacos Config subscriptions. Underlying registry connections may be shared with other clients, so disposal releases this client's references.

## Source entry points

| File | Responsibility |
| --- | --- |
| `DubboClient.cs` | Public call entry, signature and protocol handoff, discovery and selection coordination, reconnect, and shutdown. |
| `DubboClientZookeeperWatcher.cs` | Passes ZooKeeper Provider and connection events to the client. |
| `LoadBalancedInvocation.cs` | Wraps a reserved dispatch and releases its reservation on completion. |
