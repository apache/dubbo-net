# 服务选择：候选、预留与归还

[调用链总览](index.md) · [English](../../en/architecture/selection.md)

## 模块职责

服务选择将[注册发现](registry.md)返回的地址变成**可调用的 Provider 候选**，按服务维护 Driver，并为每次调用挑选和预留执行器。注册模块只负责获取地址；[元数据](metadata.md)只保存 Java 方法定义。一个服务的候选和方法调用统计由该服务的 Driver 管理，执行器连接可以跨服务复用。

## 从端点到发送

`DubboDriverCollection.AddDubboServiceDriver` 按服务建立或更新 Driver，并把端点关联到可复用的执行器。`GetDubboActuatorSuite` 向客户端区分：尚无 Driver、Driver 没有端点、有候选但没有可用执行器，以及选中执行器。前两种情况可以触发[客户端](client.md)主动发现；资源不可用不代表注册中心无 Provider。

`DubboServiceDriver.SelectAndReserve` 先排除禁用或权重不大于零的端点，再按协议、服务版本和分组过滤，按配置的策略从剩余候选中选择。可用策略包括加权随机、平滑加权轮询、一致性 Hash、最少活跃、最短响应和 P2C；活跃数与响应耗时来自客户端对方法调用的统计。

选择与增加在途引用处于同一同步边界，防止 Provider 被移除时正在发送的调用失去所用资源。

## 更新与释放

Provider 变化时，Driver 用新的发现结果更新候选；明确没有 Provider 时可以清空候选。已经预留的调用仍由 `LoadBalancedInvocation.InvokeAsync` 包裹：发送结束后记录成功或失败及耗时，并归还预留。在途调用归还后，移除的 Provider 所用执行器才能按引用关系释放。客户端关闭时也会释放持有的 Driver 与共享执行器资源。

## 源码入口

| 文件 | 职责 |
| --- | --- |
| `DubboDriverCollection.cs` | 按服务管理 Driver、交接发现结果与复用执行器。 |
| `DubboServiceDriver.cs` | 维护候选、过滤与负载选择、预留及方法统计。 |
| `LoadBalancedInvocation.cs` | 包裹一次发送，完成统计并归还预留。 |
