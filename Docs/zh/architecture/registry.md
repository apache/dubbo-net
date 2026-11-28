# 注册与发现：Provider 地址从哪里来

[调用链总览](index.md) · [English](../../en/architecture/registry.md)

## 模块职责

注册与发现按 Java 接口查找 Provider 地址：从 ZooKeeper 的接口级 `providers` 节点或 Nacos Naming 获取实例，转换为调用方可用的端点，并接收地址变化通知。它负责**发现地址**，不保存业务调用的候选集合；候选的维护与选择由[服务 Driver](selection.md)负责。Java 方法定义由[元数据](metadata.md)处理，请求的发送由[传输](transport.md)处理。

## 查询与交接

业务调用找不到服务 Driver 时，`DubboClient.QueryInternalAsync` 通过 `DubboClient.DiscoverProvidersAsync` → `ProviderDiscovery.GetProviderEndPointsAsync` 查询 Provider。发现层返回后端无关的 `ProviderRegistrySnapshot`：可用端点、明确空快照，或暂时不可用/无效请求等状态。只有 `Available` 快照交给新 Driver 并继续选择；没有端点时不会发送请求。已有 Driver 但没有端点时，调用会主动重新发现。Driver 有候选但执行器不可用是选择阶段的问题，不属于重新发现的条件。

元数据读取 Java 方法定义前，也可能通过 `GetProviderEndPointsForMetadataAsync` 查找 Provider。这条路径复用地址发现，但不会创建业务调用的 Driver。`ProviderDiscovery` 按服务名用惰性任务保证同一在途查询只启动一次，完成后移除在途记录；它不维护第二份可调用节点列表。瞬时失败保留既有 Driver 候选；确认空快照才清空已有候选。

| 来源 | 地址如何取得 | 变化如何通知 |
| --- | --- | --- |
| ZooKeeper | `ZooKeeperProviderRegistry` 读取接口 `providers` 路径下的 `dubbo://`、`tri://` 子节点并规范化端点；只收录 host 能解析为 IP 地址的记录，不做 DNS 解析。 | 适配器用共享 watcher 将一次性 children watch 转成服务级快照失效通知。空的 `providers` 目录同样安装 watch；目录尚未创建时观察服务目录。 |
| Nacos Naming | `NacosProviderRegistry` 按接口及分组查询实例，只使用健康、已启用的实例。 | 通过 `MyNacos` 的 Naming 订阅产生同一种服务级失效通知。 |

空快照、暂时不可用、无效请求和权限错误由状态区分。ZooKeeper 子节点全部被过滤时是空端点快照；SDK/连接异常不伪装成空结果。ZooKeeper 的 `BadArguments`、Nacos 无效参数映射为无效请求；ZooKeeper 的 `NoAuth`/`AuthFailed` 与 Nacos 的 `NO_RIGHT` 映射为权限错误。

## 变化与状态更新

ZooKeeper 与 Nacos 的事件都被转换为服务级快照失效通知。发现层在收到通知后重新读取完整快照；通知若与初始读取并发，会等当前读取结束再触发刷新，避免被在途查询合并吞掉。若服务已有 Driver，就重新发现并更新候选；若只有元数据缓存，就刷新该缓存；两者都没有时，不因通知创建 Driver。变化通知不会重放业务请求。

`Available` 快照更新 Driver；确认 `Empty` 时清空已有 Driver 候选并刷新缓存元数据；`Unavailable` 等失败状态只记录，不清空候选。ZooKeeper 断线或会话过期仍由原连接 watcher 处理；会话恢复后按服务重新读取并安装 watches。

## 订阅与连接生命周期

Nacos Naming 订阅由 `NacosProviderRegistry` 持有；适配器释放时只移除自身订阅。发现组件释放后，在途读取即使完成也不会发布端点。元数据 Manager 的 Nacos Config 订阅独立管理。ZooKeeper 的共享 watch adapter 路由服务目录事件；释放时停止投递变化通知，连接状态事件仍转交原连接 watcher。

`MultiMyZookeeperStorage` 和 `MultiMyNacosStorage` 按地址复用底层连接并维护引用计数；元数据中心地址相同时也可复用注册连接。客户端关闭时释放自己持有的连接引用，不关闭其他客户端仍在使用的共享连接。

## 源码入口

| 文件 | 职责 |
| --- | --- |
| [`DubboClient.cs`](../../../Clients/DubboClient.cs) | 协调业务调用与元数据查询的发现、结果交接、变化后的 Driver/元数据刷新及生命周期。 |
| [`ProviderDiscovery.cs`](../../../Registry/ProviderDiscovery.cs) | 后端无关快照协调、在途查询复用和变化后刷新交接。 |
| [`ProviderRegistryAdapter.cs`](../../../Registry/ProviderRegistryAdapter.cs) | ZooKeeper/Nacos Provider 查询、规范化快照、变化失效通知和订阅生命周期。 |
| [`DubboClientZookeeperWatcher.cs`](../../../Clients/DubboClientZookeeperWatcher.cs) | ZooKeeper 连接断线、恢复和会话过期的连接状态处理。 |
| [`MyZookeeper.cs`](../../../Registry/MyZookeeper.cs) / [`MyNacos.cs`](../../../Registry/MyNacos.cs) | 注册中心 SDK 查询、连接重试及 Nacos Naming 通知。 |
| [`MultiMyZookeeperStorage.cs`](../../../Registry/MultiMyZookeeperStorage.cs) / [`MultiMyNacosStorage.cs`](../../../Registry/MultiMyNacosStorage.cs) | 按地址复用连接并管理引用计数。 |
