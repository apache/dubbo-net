# 客户端：调用入口与流程编排

[调用链总览](index.md) · [English](../../en/architecture/client.md)

## 模块职责

`DubboClient` 接收调用方的 `QueryAsync` 请求，把服务名、方法名、参数和调用选项整理为一次调用，并协调[元数据](metadata.md)、[注册与发现](registry.md)、[服务选择](selection.md)和[传输](transport.md)。它决定何时交接到下一个模块，但不自己保存 Java 方法定义，也不执行负载均衡或协议收发。

入口既接受服务与方法名、参数，也接受包含 Java 类型、版本、分组和 attachments 的 `DubboInvocation`。客户端保留这些调用信息；调用结果以含成功状态、结果或错误信息的 `DubboRequestResult` 表示，发送前的参数或签名错误仍可能抛出异常。

## 一次调用如何交接

公开的 `QueryAsync` 重载最终进入 `QueryInternalAsync`。调用方未明确给出 Java 参数类型时，客户端先通过元数据解析方法签名并构造 `DubboInvocation`；显式指定类型时可直接构造调用，仍可能尽力检查同名重载的协议要求。签名无法确定的错误发生在发送之前。

`QueryInternalAsync` 向 `DubboDriverCollection` 请求与协议要求匹配的执行器，并预留本次调用。服务尚无 Driver 时，客户端调用注册发现；发现未报错便将端点列表交给新 Driver，再尝试选择，列表为空也不会发送请求。已有 Driver 但没有 Provider 端点时，客户端会主动重查，取得新端点才更新 Driver 并重选。若候选存在但没有可用执行器，则返回失败结果，而不是把它当作“注册中心没有 Provider”。选中后，客户端把调用交给执行器；`LoadBalancedInvocation` 包裹发送过程，并在完成时归还预留。

## 变化与关闭

Provider 变化通知经 ZooKeeper watcher 或 Nacos 回调进入客户端：已有 Driver 时重新发现并更新候选，只有元数据缓存时刷新定义。通知更新已有状态，不重新发起业务调用。ZooKeeper 连接断开或会话过期时，客户端尝试重连；连接恢复后逐服务重新发现。

`Dispose` 结束客户端的消费者注册，移除自己的 Nacos Naming 订阅，并释放元数据管理器、Driver、执行器和注册连接引用。元数据的 Nacos Config 订阅由元数据管理器释放；底层注册连接可与其他客户端共享，因此释放的是本客户端持有的引用。

## 源码入口

| 文件 | 职责 |
| --- | --- |
| `DubboClient.cs` | 公开调用入口、签名与协议交接、发现与选择协调、重连及关闭。 |
| `DubboClientZookeeperWatcher.cs` | 将 ZooKeeper Provider 变化与连接事件交给客户端处理。 |
| `LoadBalancedInvocation.cs` | 包裹一次已预留的发送，并在完成时归还预留。 |
