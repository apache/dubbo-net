# Service Selection: Candidates, Reservation, and Release

[Call-path overview](index.md) · [中文](../../zh/architecture/selection.md)

## Responsibility

Service selection turns addresses from [registry discovery](registry.md) into **callable Provider candidates**. It maintains a driver per service, selects an actuator for each call, and reserves it. Discovery finds addresses; [metadata](metadata.md) stores Java method definitions. Each service driver owns its candidates and per-method call statistics, while actuator connections can be shared across services.

## From endpoints to dispatch

`DubboDriverCollection.AddDubboServiceDriver` creates or updates a service driver and associates endpoints with reusable actuators. `GetDubboActuatorSuite` distinguishes no driver, a driver with no endpoints, candidates with no available actuator, and successful selection. The first two can cause the [client](client.md) to discover Providers; unavailable actuator resources do not mean the registry has no Provider.

`DubboServiceDriver.SelectAndReserve` first excludes disabled endpoints and those with nonpositive weight, then filters by protocol, service version, and group. It selects from the remainder using weighted random, smooth weighted round-robin, consistent hash, least active, shortest response, or P2C; active counts and latency come from client-side per-method call statistics.

Selection and incrementing the in-flight reference share a synchronization boundary, protecting a call's resources if a Provider is removed during dispatch.

## Updates and release

On Provider changes, the driver updates its candidates from discovery; an explicit no-Provider result can clear them. A reserved call is wrapped by `LoadBalancedInvocation.InvokeAsync`: after dispatch it records success or failure and elapsed time, then releases the reservation. Once in-flight calls finish, resources for a removed Provider can be released according to their references. Closing the client also releases its drivers and shared actuator resources.

## Source entry points

| File | Responsibility |
| --- | --- |
| `DubboDriverCollection.cs` | Manages service drivers, hands off discovery results, and reuses actuators. |
| `DubboServiceDriver.cs` | Maintains candidates, filters and load-balances them, reserves calls, and tracks per-method statistics. |
| `LoadBalancedInvocation.cs` | Wraps a dispatch, completes statistics, and releases its reservation. |
