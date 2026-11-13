# Metadata: From Java Definitions to Invocation Signatures

[Call-path overview](index.md) · [中文](../../zh/architecture/metadata.md)

## Responsibility

Metadata obtains Java interface method names, parameter types, and overload definitions, then matches caller arguments to a signature. It caches **method definitions**, not callable Provider candidates, and does not perform protocol I/O. [Registry discovery](registry.md) finds Providers, while [service selection](selection.md) maintains and selects callable candidates.

## Loading definitions and resolving signatures

`DubboMetadataManager` loads and caches definitions per service. Depending on Provider version and available sources, it reads from the metadata center or a short-lived Telnet `ls -l` query; Telnet can also supplement incomplete definitions. Locating a Provider may reuse registry discovery without creating a business-call driver.

The metadata center reuses the registry connection by default. For a separate center, `DubboClientConf.MetadataCenterAddress` configures its address and `MetadataRootPath` its location.

Method signatures may come from the metadata center or Telnet. The `FullServiceDefinition.types` definitions used for POJOs and collections come only from the metadata center, without a Telnet fallback. Source-style generic arguments in Telnet output undergo Java type erasure before use as `$invoke` `parameterTypes`; for example, `java.util.Map<String, Long>` becomes `java.util.Map`.

When `QueryAsync` does not specify Java parameter types, `DubboClient.CreateMetadataInvocationAsync` obtains definitions, uses `DubboMethodMetadataResolver.Resolve` to match arguments and overloads, and hands the resulting `DubboInvocation` with Java types back to the client for dispatch. If available definitions do not contain a matching method, or the signature is ambiguous, `DubboMetadataException` may be raised before dispatch. When definitions are unavailable and the call has no arguments, the client can still dispatch with an empty parameter signature; this fallback does not apply when argument types need resolution. With explicit types, the client can construct the invocation directly; it may still make a best-effort check for protocol requirements of same-name overloads rather than requiring every call to load metadata successfully.

## Cache and subscription lifetime

The manager caches definitions by service. Provider or metadata changes refresh **existing cache entries**; this cache does not become the driver's address list. The manager owns and removes its Nacos Config subscriptions on disposal, separately from the Nacos Naming subscriptions used for Provider discovery. A signature exception before dispatch is different from a `DubboRequestResult` failure returned by transport.

## Source entry points

| File | Responsibility |
| --- | --- |
| `DubboMetadataManager.cs` | Loading definitions, choosing sources, per-service caching, refresh, and Config subscriptions. |
| `DubboMethodMetadataResolver.cs` | Matches caller arguments against Java signatures and overloads. |
| `DubboTelnetMetadataResolver.cs` | Parses and supplements method definitions returned by Telnet. |
| `DubboClient.cs` | Constructs invocations, exposes method signatures and type definitions, and checks protocol requirements for explicit calls. |
