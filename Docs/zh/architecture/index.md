# 源码导航：从入口追踪调用与更新

[文档首页](../index.md) · [English](../../en/architecture/index.md) · [使用说明](../../../readme_cn.md)

DubboNet 是调用 Java Provider 的消费端。下面是两条可分别阅读的路径；只有标明条件的步骤才会发生。想学习如何使用 API，请去[使用说明](../../../readme_cn.md)；协议字节在[原生](../protocols/native-dubbo.md)和[Triple](../protocols/triple.md)页面。

## 请求路径：`QueryAsync` 到结果

```text
DubboClient.QueryAsync
  ├─ Java 类型未给出 → CreateMetadataInvocationAsync → DubboMetadataManager → 签名解析
  └─ QueryInternalAsync → GetRequiredProtocolAsync（尽力检查重载约束）
       → DubboDriverCollection.GetDubboActuatorSuite(reserve: true)
          ├─ 无 Driver → ConcurrentGetProviderEndPoints → AddDubboServiceDriver → 再次选择
          ├─ Driver 存在但 Provider 为空 → 重新发现；有节点才更新、重选
          ├─ 有候选但执行器不可用 → 返回失败结果
          └─ 已选中 → LoadBalancedInvocation.InvokeAsync → 执行器 SendQuery → DubboRequestResult
```

| 阶段 | 做什么 | 模块说明 |
| --- | --- | --- |
| 调用入口 | `QueryAsync` 整理参数，`QueryInternalAsync` 协调协议要求、发现与发送。 | [客户端](client.md) |
| 方法签名 | 需要推断 Java 类型时获取方法定义、匹配重载，构造调用。 | [元数据](metadata.md) |
| 地址发现 | 服务没有 Driver 或端点时查询 Provider，监听地址变化。 | [注册与发现](registry.md) |
| 选择与预留 | Driver 维护候选，按策略选择执行器并保护在途调用。 | [服务选择](selection.md) |
| 协议发送 | 执行器编码请求、收取响应；调用完成后归还预留。 | [传输](transport.md) |

元数据查询**也可能先发现 Provider**；已有 Driver 的调用不必每次查询注册中心。上图是分支导航，不是每次请求的固定执行顺序。

## 更新路径：事件到已有状态

```text
ZooKeeper: DubboClientZookeeperWatcher.process ─┐
Nacos: HandleNacosProviderChangedAsync ───────────┼─ 已有 Driver → ReflushProviderAsync
                                                  │                  → 发现地址 → 更新 Driver／元数据缓存
                                                  └─ 仅有元数据缓存 → RefreshCachedMetadataAsync
断线恢复: StartConnecTaskAsync → TryDoConnectRegistryTaskAsync → ReLoadDubboDriverCollection
关闭: DubboClient.Dispose → 取消订阅／释放连接引用、Driver 与执行器
```

[注册与发现](registry.md)说明地址查询与通知，[客户端](client.md)说明重连与资源释放。

## 状态归属

| 状态 | 持有者 | 不等于 |
| --- | --- | --- |
| 可调用 Provider 候选、选择与预留 | `DubboServiceDriver` 及 `DubboDriverCollection` | 注册发现的查询结果缓存 |
| Java 方法与类型定义 | `DubboMetadataManager` | 可调用 Provider 列表 |
| 注册连接引用及客户端执行器寿命 | `DubboClient`；底层连接由共享 storage 复用 | 将连接独占或把 watcher 当成可单独退订 |
