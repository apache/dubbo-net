# Registry and Discovery: Where Provider Addresses Come From

[Call-path overview](index.md) · [中文](../../zh/architecture/registry.md)

## Responsibility

Registry discovery looks up Provider addresses for a Java interface. It reads interface-level ZooKeeper `providers` nodes or Nacos Naming instances, converts them into endpoints for its caller, and receives address-change notifications. It **discovers addresses** rather than storing the candidates for business calls. The [service driver](selection.md) maintains and selects those candidates; [metadata](metadata.md) handles Java method definitions, and [transport](transport.md) sends requests.

## Lookup and handoff

When a business call has no service driver, `DubboClient.QueryInternalAsync` looks up Providers through `ConcurrentGetProviderEndPoints`. If discovery returns no error, it hands the endpoint list to a new driver and continues selection. The list may be empty; without a usable endpoint, no request is sent. A call with an existing driver but no endpoints also actively looks up Providers: new endpoints update the driver and trigger another selection attempt; otherwise the call fails. Having candidates but no available actuator is a selection problem, not a reason to rediscover addresses.

Before reading Java method definitions, metadata may also find Providers through `GetProviderEndPointsForMetadataAsync`. This reuses discovery without creating a business-call driver. `ConcurrentGetProviderEndPoints` attempts to reuse an in-flight lookup task for the same service and removes the task on completion. Each lookup hands endpoints or an error to its caller rather than maintaining another callable-candidate list inside registry discovery.

| Source | How addresses are found | How changes are reported |
| --- | --- | --- |
| ZooKeeper | Reads `dubbo://` and `tri://` children under the interface's `providers` path. It accepts only endpoints whose hosts parse as IP addresses; domain and host names are not resolved through DNS. | Installs a one-shot children watch when children exist; an event triggers discovery for the service. With no children, discovery reports no Provider, and a later business call actively retries. |
| Nacos Naming | Queries instances by interface and group, using only healthy, enabled instances. | Registers a Naming change callback for the client and refreshes the affected service on changes. |

Discovery returns endpoints or an error; a missing path, no usable Provider, and an ordinary query failure are distinct outcomes. ZooKeeper may also return an empty list if all children are filtered out, or throw after exhausted connection retries.

## Changes and state updates

The ZooKeeper watcher identifies the service from the changed path; Nacos callbacks handle the corresponding service. If it has a driver, discovery updates its candidates. If it only has cached metadata, the cache is refreshed. With neither a driver nor cached metadata, the notification does not create a new driver. Notifications do not replay business requests.

After rediscovery returns endpoints, `ReflushProviderAsync` updates the existing driver and requests a refresh of cached service metadata. An explicit no-Provider or no-healthy-Provider result clears an existing driver's candidates; an ordinary lookup error is logged without clearing them. On ZooKeeper disconnection or session expiration, the watcher's reconnect path attempts rediscovery for each service after the connection recovers.

## Subscription and connection lifetime

Nacos Naming subscriptions use `DubboClient` as the subscriber; disposing the client removes its Naming subscriptions. The metadata manager owns and removes its separate Nacos Config subscriptions. ZooKeeper children watches are one-shot notifications, and their callback checks whether the client has been disposed.

`MultiMyZookeeperStorage` and `MultiMyNacosStorage` reuse underlying connections by address and count references; a metadata center at the same address can also reuse the registry connection. Disposing a client releases its own connection references rather than closing a shared connection still used by other clients.

## Source entry points

| File | Responsibility |
| --- | --- |
| `DubboClient.cs` | Discovery entry points for calls and metadata, result handoff, driver/metadata refresh after changes, and subscription cleanup. |
| `DubboClientZookeeperWatcher.cs` | Entry point for ZooKeeper child changes, disconnections, and session expiration. |
| `MyZookeeper.cs` / `MyNacos.cs` | Registry SDK queries, connection retries, and Nacos Naming notifications. |
| `MultiMyZookeeperStorage.cs` / `MultiMyNacosStorage.cs` | Connection reuse by address and reference counting. |
