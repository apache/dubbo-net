# 注册与发现：Provider 地址从哪里来

[调用链总览](index.md) · [English](../../en/architecture/registry.md)

## 模块职责

注册与发现按 Java 接口查找 Provider 地址：从 ZooKeeper 的接口级 `providers` 节点或 Nacos Naming 获取实例，转换为调用方可用的端点，并接收地址变化通知。它负责**发现地址**，不保存业务调用的候选集合；候选的维护与选择由[服务 Driver](selection.md)负责。Java 方法定义由[元数据](metadata.md)处理，请求的发送由[传输](transport.md)处理。

## 查询与交接

业务调用找不到服务 Driver 时，`DubboClient.QueryInternalAsync` 通过 `ConcurrentGetProviderEndPoints` 查询 Provider；只要发现未返回错误，就把端点列表交给新 Driver 并继续选择。列表也可能为空；没有可用端点时不会发送请求。已有 Driver 但没有端点时，调用会主动重新发现：有新端点就更新 Driver 并重新选择，没有则返回失败。Driver 有候选但执行器不可用是选择阶段的问题，不属于重新发现的条件。

元数据读取 Java 方法定义前，也可能通过 `GetProviderEndPointsForMetadataAsync` 查找 Provider。这条路径复用地址发现，但不会创建业务调用的 Driver。`ConcurrentGetProviderEndPoints` 尽量复用同一服务尚未完成的查询任务，完成后移除在途记录；每次发现把端点列表或错误交给调用方，不在注册模块中维护另一份可调用节点列表。

| 来源 | 地址如何取得 | 变化如何通知 |
| --- | --- | --- |
| ZooKeeper | 读取接口 `providers` 路径下的 `dubbo://`、`tri://` 子节点；只收录 host 能解析为 IP 地址的端点，不对域名或主机名做 DNS 解析。 | 有子节点时安装一次性 children watch；事件到达后按服务重新发现。空节点时返回无 Provider，后续业务调用会主动重查。 |
| Nacos Naming | 按接口及分组查询实例，只使用健康、已启用的实例。 | 为客户端登记 Naming 变更回调，变化时刷新对应服务。 |

查询返回端点列表或错误信息；路径不存在、没有可用 Provider 与一般查询失败是不同的结果。ZooKeeper 子节点都被过滤时也可能返回空列表，连接重试耗尽则可能抛异常。

## 变化与状态更新

ZooKeeper watcher 从发生变化的路径识别服务；Nacos 回调按对应服务处理变化。若服务已有 Driver，就重新发现并更新候选；若只有元数据缓存，就刷新该缓存；两者都没有时，不因通知创建 Driver。变化通知不会重放业务请求。

重新发现得到端点后，`ReflushProviderAsync` 更新已有 Driver，并请求刷新已缓存的服务元数据。明确没有 Provider 或没有健康 Provider 时，已有 Driver 的候选更新为空；普通查询错误只记录，不清空候选。ZooKeeper 断线或会话过期后，watcher 的重连路径会在连接恢复时逐服务尝试重新发现。

## 订阅与连接生命周期

Nacos Naming 订阅以 `DubboClient` 为订阅者，客户端释放时移除自己的 Naming 订阅。元数据 Manager 的 Nacos Config 订阅由它自己管理和释放，两类订阅彼此独立。ZooKeeper 的 children watch 是一次性通知，回调会检查客户端是否已释放。

`MultiMyZookeeperStorage` 和 `MultiMyNacosStorage` 按地址复用底层连接并维护引用计数；元数据中心地址相同时也可复用注册连接。客户端关闭时释放自己持有的连接引用，不关闭其他客户端仍在使用的共享连接。

## 源码入口

| 文件 | 职责 |
| --- | --- |
| `DubboClient.cs` | 业务调用与元数据查询的发现入口、结果交接、变化后的 Driver/元数据刷新及订阅释放。 |
| `DubboClientZookeeperWatcher.cs` | ZooKeeper 子节点变化、断线和会话过期的通知入口。 |
| `MyZookeeper.cs` / `MyNacos.cs` | 注册中心 SDK 查询、连接重试及 Nacos Naming 通知。 |
| `MultiMyZookeeperStorage.cs` / `MultiMyNacosStorage.cs` | 按地址复用连接并管理引用计数。 |
