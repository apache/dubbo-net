# Source Navigation: Calls and Updates

[Documentation home](../index.md) · [中文](../../zh/architecture/index.md) · [User guide](../../../README.md)

DubboNet is a consumer of remote Java Providers. The two paths below can be read independently; conditional steps do not run on every request. For API usage, start with the [user guide](../../../README.md); wire details belong to the [native](../protocols/native-dubbo.md) and [Triple](../protocols/triple.md) pages.

## Request path: `QueryAsync` to result

```text
DubboClient.QueryAsync
  ├─ Java types missing → CreateMetadataInvocationAsync → DubboMetadataManager → signature resolution
  └─ QueryInternalAsync → GetRequiredProtocolAsync (best-effort overload check)
       → DubboDriverCollection.GetDubboActuatorSuite(reserve: true)
          ├─ no driver → ConcurrentGetProviderEndPoints → AddDubboServiceDriver → select again
          ├─ driver with no Providers → discover again; retry only if Providers appear
          ├─ candidates present but no available actuator → failure result
          └─ selected → LoadBalancedInvocation.InvokeAsync → actuator SendQuery → DubboRequestResult
```

| Stage | What it does | Module guide |
| --- | --- | --- |
| Call entry | `QueryAsync` prepares arguments; `QueryInternalAsync` coordinates protocol requirements, discovery, and dispatch. | [Client](client.md) |
| Method signature | When Java types need inference, loads definitions, resolves overloads, and constructs the invocation. | [Metadata](metadata.md) |
| Address discovery | Looks up Providers when a service has no driver or endpoints, and receives address changes. | [Registry and discovery](registry.md) |
| Selection and reservation | The driver holds candidates, selects an actuator by policy, and protects in-flight calls. | [Service selection](selection.md) |
| Protocol dispatch | The actuator encodes requests and receives responses; the reservation is released on completion. | [Transport](transport.md) |

Metadata lookup **may itself discover Providers**; a call with an existing driver need not query the registry. The diagram is a map of branches, not a mandatory request pipeline.

## Update path: event to existing state

```text
ZooKeeper: DubboClientZookeeperWatcher.process ─┐
Nacos: HandleNacosProviderChangedAsync ───────────┼─ existing driver → ReflushProviderAsync
                                                  │                     → discover → update driver/metadata cache
                                                  └─ only metadata cached → RefreshCachedMetadataAsync
Reconnect: StartConnecTaskAsync → TryDoConnectRegistryTaskAsync → ReLoadDubboDriverCollection
Shutdown: DubboClient.Dispose → unsubscribe/release registry references, drivers, and actuators
```

[Registry and discovery](registry.md) covers address lookup and notifications; [Client](client.md) covers reconnect and resource release.

## State ownership

| State | Owner | Not the same as |
| --- | --- | --- |
| Callable Provider candidates, selection, and reservation | `DubboServiceDriver` and `DubboDriverCollection` | A cache of registry lookup results |
| Java method and type definitions | `DubboMetadataManager` | A list of callable Providers |
| Registry connection references and client actuator lifetime | `DubboClient`; the underlying connections are shared by storage | Exclusive connection ownership or an individually removable ZooKeeper watch |
