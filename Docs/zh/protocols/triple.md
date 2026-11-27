# Triple Java Interface Unary：HTTP/JSON 与二进制 wrapper

[English](../../en/protocols/triple.md) · [中文首页](../index.md)

`TripleDubboActuatorSuite` 默认通过 HTTP/1.1 和 JSON 调用 `tri://` Provider。显式选择 `Hessian2Wrapper` 后，使用 HTTP/2 帧化的 Java 泛化调用。两种模式均使用 Java Dubbo 3.3.6 做互通验证；其他二进制 Provider 版本在独立验证前不作兼容承诺。

## 请求格式

- HTTP 方法：`POST`
- 路径：`/{完整 Java 接口名}/{方法名}`
- Content-Type：`application/json`
- 请求正文：按方法参数顺序组成的 JSON 数组；无参数为 `[]`，单参数也必须保留数组，
  例如 `[false]`

示例：

```http
POST /com.example.DataTypeService/negate HTTP/1.1
Content-Type: application/json
tri-service-version: 1.0.0
tri-service-group: demo
tri-service-timeout: 10000

[false]
```

默认 HTTP/JSON 模式下，元数据识别到多个 Java 签名时，客户端在负载均衡前限定使用 `dubbo://`；仅有 `tri://` 时返回兼容性失败。显式二进制模式会传递精确签名，可以调用这些重载。

## 服务路由和附件

执行器优先使用 `DubboInvocation.Version` 和 `DubboInvocation.Group`；未显式填写时，
使用注册中心 Provider URL 中的 `version`、`group` 和 `timeout`。对应的 Triple 请求头为：

- `tri-service-version`
- `tri-service-group`
- `tri-service-timeout`

`DubboInvocation.Attachments` 会作为 HTTP 请求头发送。协议保留头、伪头、
`Host`、`Content-Length` 和 `Content-Type` 不能由附件覆盖。

响应为 2xx 时保留原始 JSON 正文；非 2xx 时 `QuerySuccess=false`，HTTP 状态和
Provider 返回的错误正文同时写入 `ErrorMeaasge`。响应头会保存在
`ResponseAttachments` 中。

## 截止时间、取消与地址

- Provider URL 的 `timeout` 优先于 `DubboRequestTimeout` 默认值，可比默认值更长或更短；最少为 1 ms。
- 每次调用的截止时间同时覆盖响应头和完整响应体。内部 `HttpClient.Timeout` 为无限，不与调用级截止时间竞争。
- `SendQuery(invocation, cancellationToken)` 遇到调用方取消会返回 `QuerySuccess=false`，并在错误信息中区分调用方取消和请求超时。两者同时发生时优先归为调用方取消。
- 直接创建执行器时，地址/端口构造器支持域名、IPv4、有括号或无括号的 IPv6；`IPEndPoint` 构造器同样支持 IPv6。通过 ZooKeeper 发现的地址仍受[注册模块的 IP 限制](../architecture/registry.md)约束。

## HTTP/JSON 支持范围

当前支持：

- Dubbo 3.3+ Java Interface
- Unary
- HTTP/1.1
- `application/json`
- ZooKeeper 经典接口级 `tri://` Provider 发现

当前不支持：

- Protobuf/IDL 服务
- Client/Server/Bidirectional Streaming
- 自动协商 HTTP/2、HTTP/3
- Dubbo 3 应用级服务发现

## 显式二进制模式

```csharp
using DubboNet.Clients;
using DubboNet.DubboService.DataModle;
using System.Threading;

using var client = new DubboClient("127.0.0.1:2181", new DubboClient.DubboClientConf
{
    TripleWireMode = TripleWireMode.Hessian2Wrapper,
    DubboRequestTimeout = 5_000,
    TripleMaxMessageBytes = 8 * 1024 * 1024,
    TripleUseTls = false // true 使用 HTTPS，并正常验证服务端证书
});
var invocation = new DubboInvocation("com.example.EchoService", "echo",
    new[] { "java.lang.String" }, new object[] { "hello" })
{ Version = "1.0.0", Group = "demo" };
DubboRequestResult result = await client.QueryAsync(invocation, CancellationToken.None);
```

直接创建执行器时，在 `DubboActuatorSuiteConf` 上设置同名的三个 Triple 选项。配置在构造时捕获，属于客户端/执行器配置，与共享注册中心的消费者注册配置相互独立。

二进制模式在负载均衡前限定使用符合版本/分组要求的 `tri://` Provider，重载也使用这条路径。失败后不会切换 Native 或 JSON 重发。`tri://` 本身不能证明编码能力：需要由使用者确认 Provider 开放 Java 泛化调用和 Hessian2。本实现不做业务 RPC 自动重试、协议探测、降级或结果不确定时的请求重放。

### 线协议与实现归属

请求为 `POST /{Java 接口}/$invoke`，严格使用 HTTP/2，携带 `application/grpc+proto`、`te: trailers`、`generic: true`、服务版本/分组和剩余 `grpc-timeout`。正文为五字节 gRPC 消息前缀，加 Apache 固定的 `TripleRequestWrapper` Protobuf 包装。三个独立 Hessian 参数依次为实际方法名、精确 Java 参数类型数组和泛化参数数组。包装中的 `hessian4` 是 Java Dubbo 对 Hessian2 使用的线协议别名。

参数准备复用既有 Java 泛化值转换，包括 POJO map、枚举名和数组；不会把经典 Dubbo 的 16 字节帧头或整个 `$invoke` 请求体装进 wrapper。参数及类型保持原顺序。重载和 null 参数应像 Native 调用一样显式提供 Java 类型。

二进制编码会先擦除源码式泛型实参。例如，wrapper 使用 `java.util.List` 匹配 Java 方法，并按该类型转换参数值。元数据保留 `java.util.List<java.lang.Long>` 的完整定义。无参调用给出空类型列表时，可以直接发送，省去元数据查询。

二进制选路不再将完整参数列表预先序列化为 JSON。非 Hash 策略不需要请求内容键；`ConsistentHash` 由客户端请求编码器准备一次有大小限制的 wrapper，取其精确字节的 SHA-256 摘要并转成 Base64，再交给既有 MurmurHash2 环。传输层复用同一个私有快照，避免普通单次遍历集合被消费两次。键包含 Java 方法、精确类型和参数值，不包含 HTTP trace/deadline metadata。Native/HTTP-JSON 的原有取键策略不变；开启二进制模式时，与这些模式的亲和节点可能不同。POCO 投影仍保留既有的 JSON 映射和自定义转换器行为。

公开的 `TripleDubboActuatorSuite` 持有共享 HTTP 客户端；`Triple/BinaryTripleTransport.cs` 管理单次请求/响应；`TripleUnaryFraming.cs` 负责有界 Unary 帧读取；`Hessian2WrapperCodec.cs` 负责固定包装格式。HTTP/2 连接、TLS、流控及 stream id 交给 .NET。Google.Protobuf 从已有 Nacos 依赖图提升为直接依赖；不要求用户生成 IDL，也不引入原生 gRPC 运行时。

### 状态、异常和 metadata

- 读到响应 EOF 后才检查 trailers。HTTP 200 不代表 RPC 成功；`grpc-status` 必须存在，也支持仅含 trailers 的错误。有消息体时，状态必须位于 trailers；只有无消息体的 trailers-only 响应才允许在首个头部块中携带状态。成功的 Unary 必须恰好包含一条消息。
- Java 包装异常可能使用 `grpc-status=0` 与非零 `tri-exception-code` 返回。按 Java 客户端约定将其标记为失败，并在 `Result`/`RawResult` 保留异常内容；不会根据普通业务对象的 `class` 字段猜测失败。
- `QuerySuccess` 仍表示传输及协议结果。新增 `GrpcStatusCode`、`TripleExceptionCode` 和 `ResponseHttpVersion` 暴露真实二进制响应，Native 专属状态字段保持 null。响应 metadata 与尾部 metadata 分别保存在 `ResponseAttachments`、`ResponseTrailers`。
- Hessian 可返回 NaN 和正负无穷。二进制调用的 `Result` 使用 JSON 字符串 `"NaN"`、`"Infinity"`、`"-Infinity"` 表示它们。`RawResult` 保留浮点值，强类型结果可将它们读回浮点数，也支持集合元素。
- 附件名使用小写 ASCII，值使用可打印 ASCII。`-bin` 值按 Base64 字符串传入，接受带/不带 padding，不会重复编码；接收值保留为 Base64。`content-language` 等普通内容头会随请求传递。路由、帧格式、gRPC 和连接保留头不能被覆盖。
- 二进制模式只使用本次调用显式传入的 `cookie` 附件。响应的 `set-cookie` 可从 `ResponseAttachments` 获取，由调用方决定如何处理。
- 缺失/重复/非法状态、未知响应内容类型或序列化器、多余消息、截断帧均明确失败。首期只声明 identity 压缩，拒绝需要解压的消息。

### 截止时间、连接与资源边界

二进制 `DubboClient` 调用将 `DubboRequestTimeout` 作为包含元数据及发现等待的完整调用上限。客户端在私有调用副本中保存剩余期限，并用 `grpc-timeout` 传给 Provider。更短的 Provider timeout 仍限制 RPC。`DubboNet.Clients` 下的 `QueryAsync(invocation, cancellationToken)` 扩展方法及其强类型版本在两种模式下均提供完整调用上限。`QueryAsync(null, default)` 等既有调用仍优先匹配原来的实例重载。默认 HTTP/JSON 路径在请求 JSON 序列化完成后启动 Provider 传输超时，覆盖响应头和完整响应体。

直接调用执行器的期限覆盖编码、发送、响应头和完整响应体。取消仅终止当前 HTTP/2 stream/response，不释放共享客户端，也不取消其他调用等待的共享注册中心操作。EOF 及值/JSON 解码后再次检查取消，避免同步完成的读取掩盖已发生的取消。失败结果区分调用方取消、超时和执行器释放。断连的业务请求不重放，后续新请求可正常建立/复用连接。执行器释放时取消自身在途请求，并仅释放自己拥有的池；注入的共享客户端仍可供其他执行器使用。客户端关闭仍遵循既有的在途预留生命周期。`MaxConnections` 限制连接数，不是 RPC stream 的并发数。

`TripleMaxMessageBytes` 默认限制单条 wrapper 消息为 8 MiB，不包含五字节前缀。请求在写入期间检查上限，响应在按声明长度分配前检查。响应 Hessian 值限制为 64 层嵌套。二进制 Triple 与 Native 一样，按实际 Hessian 数据长度计算规范化预算：展开节点不超过 `min(2_000_000, max(100_000, 2 * dataBytes))`，JSON 字节估算不超过 `min(64 MiB, max(32 MiB, 8 * dataBytes))`；共享引用每次展开都计入预算。已规范化的树直接序列化，不重复做一次规范化。每个 metadata 头部/尾部块限制为 8 KiB。

二进制请求在库自身管理的规范化遍历和写入期间检查取消。参数嵌套最多 64 层（包含泛化参数数组），每次调用的规范化最多访问 2,000,000 个容器、键、值或引用。JSON 参数投影也在递归复制子节点前检查相同的遍历边界；其计数器独立，避免投影和规范化对同一逻辑树重复扣减预算。使用者 getter 和转换器仍沿用既有的同步 POCO 映射。这些保护不再依赖一次多余的完整请求 JSON 序列化。

### 运行时与验收边界

- 使用 .NET 8/10 原生资产，以及 .NET 6/8/10 上的 Standard DLL 验证 HTTP/2 和 trailers。Standard 缺少的 API 集中在 `Compatibility/HttpCompatibility.cs` 适配，不把目标框架等同于运行时能力。
- 本实现不支持旧 .NET Framework handler 的二进制传输，在发送前返回明确的能力失败；已有 Native/HTTP-JSON 能力不受影响。Standard 可引用不代表所有旧宿主/操作系统都能运行 HTTP/2。
- 支持 h2c 和 HTTPS，HTTPS 使用运行时正常证书校验，不修改进程 TLS 或系统信任库。HTTP/3、用户 Protobuf/IDL、所有 Streaming 和完整 gRPC 兼容仍不属于本期。
- Java 3.3.6 工程独立生成 wrapper/Hessian 固定样本；端到端为 ZooKeeper/Nacos 分别增加全部 25 个方法，并断言 HTTP/2 和 gRPC 状态，Native 或 JSON 路径不能满足二进制覆盖。原有版本/方法用例保留。真实 NuGet 消费冒烟在 .NET 6/8/10 执行二进制调用，Framework 验证明确的不支持结果。
