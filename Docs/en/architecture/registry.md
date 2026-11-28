# Registry and Discovery: Where Provider Addresses Come From

[Call-path overview](index.md) · [中文](../../zh/architecture/registry.md)

## Responsibility

Registry discovery looks up Provider addresses for a Java interface. It reads interface-level ZooKeeper `providers` nodes or Nacos Naming instances, converts them into endpoints for its caller, and receives address-change notifications. It **discovers addresses** rather than storing the candidates for business calls. The [service driver](selection.md) maintains and selects those candidates; [metadata](metadata.md) handles Java method definitions, and [transport](transport.md) sends requests.

## Lookup and handoff

When a business call has no service driver, `DubboClient.QueryInternalAsync` looks up Providers through `DubboClient.DiscoverProvidersAsync` → `ProviderDiscovery.GetProviderEndPointsAsync`. Discovery returns a backend-neutral `ProviderRegistrySnapshot`: available endpoints, a confirmed empty snapshot, or a status such as temporary unavailability or invalid request. Only an `Available` snapshot is handed to a new driver. Without endpoints, no request is sent. A call with an existing driver but no endpoints also actively looks up Providers. Having candidates but no available actuator is a selection problem, not a reason to rediscover addresses.

Before reading Java method definitions, metadata may also find Providers through `GetProviderEndPointsForMetadataAsync`. This reuses discovery without creating a business-call driver. `ProviderDiscovery` uses a lazy task to share one in-flight lookup per service and removes it on completion; it does not maintain a second callable-candidate list. Temporary failures preserve existing driver candidates; only a confirmed empty snapshot clears them.

| Source | How addresses are found | How changes are reported |
| --- | --- | --- |
| ZooKeeper | `ZooKeeperProviderRegistry` reads `dubbo://` and `tri://` children under the interface's `providers` path and normalizes endpoints. It accepts hosts that parse as IP addresses and does not perform DNS resolution. | A shared adapter watcher converts one-shot children watches into service-level snapshot invalidations. Empty provider directories are watched too; if the directory does not exist, the service directory is watched. |
| Nacos Naming | `NacosProviderRegistry` queries instances by interface and group, using only healthy, enabled instances. | `MyNacos` Naming subscriptions emit the same service-level invalidation notification. |

An empty snapshot, temporary unavailability, invalid requests, and authorization failures have distinct states. ZooKeeper children filtered out as unusable produce an empty endpoint snapshot; SDK/connection failures are not represented as empty results. ZooKeeper `BadArguments`, Nacos invalid-parameter responses map to invalid request; ZooKeeper `NoAuth`/`AuthFailed` and Nacos `NO_RIGHT` responses map to permission denied.

## Changes and state updates

ZooKeeper and Nacos events are converted to service-level snapshot invalidations. Discovery rereads a complete snapshot after an event. If an event races the initial read, refresh is scheduled after the in-flight read completes so coalescing cannot swallow the invalidation. If a driver exists, discovery updates its candidates. If only cached metadata exists, the cache is refreshed. With neither, the notification does not create a new driver. Notifications do not replay business requests.

An `Available` snapshot updates the driver; a confirmed `Empty` snapshot clears existing candidates and refreshes cached metadata; `Unavailable` and other failures are logged without clearing candidates. ZooKeeper connection loss and session expiration remain handled by the existing connection watcher; after recovery, services are reread and watches reinstalled.

## Subscription and connection lifetime

`NacosProviderRegistry` owns its Naming subscriptions and removes only those subscriptions when disposed. An in-flight read may finish after discovery disposal, but its endpoints are rejected rather than published. The metadata manager independently owns its Nacos Config subscriptions. The shared ZooKeeper adapter routes provider-directory events and stops dispatch after disposal; connection state events are forwarded to the existing connection watcher.

`MultiMyZookeeperStorage` and `MultiMyNacosStorage` reuse underlying connections by address and count references; a metadata center at the same address can also reuse the registry connection. Disposing a client releases its own connection references rather than closing a shared connection still used by other clients.

## Source entry points

| File | Responsibility |
| --- | --- |
| [`DubboClient.cs`](../../../Clients/DubboClient.cs) | Coordinates discovery for calls and metadata, result handoff, driver/metadata refresh and lifecycle. |
| [`ProviderDiscovery.cs`](../../../Registry/ProviderDiscovery.cs) | Backend-neutral snapshot coordination, in-flight coalescing and handoff after changes. |
| [`ProviderRegistryAdapter.cs`](../../../Registry/ProviderRegistryAdapter.cs) | ZooKeeper/Nacos lookup, snapshot normalization, invalidation notifications and subscription lifetime. |
| [`DubboClientZookeeperWatcher.cs`](../../../Clients/DubboClientZookeeperWatcher.cs) | ZooKeeper disconnect, recovery and session-expiration handling. |
| [`MyZookeeper.cs`](../../../Registry/MyZookeeper.cs) / [`MyNacos.cs`](../../../Registry/MyNacos.cs) | Registry SDK queries, connection retries, and Nacos Naming notifications. |
| [`MultiMyZookeeperStorage.cs`](../../../Registry/MultiMyZookeeperStorage.cs) / [`MultiMyNacosStorage.cs`](../../../Registry/MultiMyNacosStorage.cs) | Connection reuse by address and reference counting. |
