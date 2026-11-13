# 传输：执行器如何发送与接收

[调用链总览](index.md) · [English](../../en/architecture/transport.md)

## 模块职责

传输模块接收已选中的执行器与 `DubboInvocation`，把调用编码为对应协议的请求，管理连接和响应，并返回 `DubboRequestResult`。它负责**收发与协议处理**；[注册与发现](registry.md)提供地址，[服务选择](selection.md)决定调用哪一个端点并维护预留，传输执行器不拥有服务的候选集合或负载策略。

## 从选择到协议执行器

`DubboClient.QueryInternalAsync` 在选中执行器后，通过 `LoadBalancedInvocation.InvokeAsync` 包裹发送。支持结构化调用的执行器接收 `IDubboGenericInvocationActuator.SendQuery(invocation)`；旧接口则接收序列化后的参数和方法路径。无论返回成功还是失败，外层都会完成调用统计并归还本次预留。

| 协议 | 执行器负责的工作 | 细节 |
| --- | --- | --- |
| `dubbo://` | `NativeDubboActuatorSuite` 管理 TCP 连接、Hessian2 请求编码与响应处理。 | [原生 Dubbo 协议](../protocols/native-dubbo.md) |
| `tri://` | `TripleDubboActuatorSuite` 处理 Java Interface Unary HTTP/JSON 请求、响应及超时和取消。 | [Triple 协议](../protocols/triple.md) |

正常注册发现只把 `dubbo://` 和 `tri://` 地址转为 Provider 端点。Driver 对其他 scheme 的 Telnet 回退与[元数据](metadata.md)读取定义时短时使用的 Telnet `ls -l` 不是同一条业务传输路径。

签名无法确定等发送前错误与执行器返回的 `DubboRequestResult` 失败应分开理解。调用结果包含成功状态、结果或错误及请求耗时；执行器还会保留可用的响应附件。

## 连接与释放

执行器持有自己的连接或连接池，并按配置处理请求超时等收发问题。服务 Driver 可复用执行器；发送期间由预留保护在途调用，完成后归还，由共享资源的引用关系决定何时释放连接。客户端关闭时释放自己持有的执行器资源。

## 源码入口

| 文件 | 职责 |
| --- | --- |
| `NativeDubboActuatorSuite.cs` | 原生 Dubbo 请求、响应及连接生命周期。 |
| `TripleDubboActuatorSuite.cs` | Triple 请求、响应、取消和 HTTP 连接资源。 |
| `LoadBalancedInvocation.cs` | 传输调用外层的统计与预留归还。 |
