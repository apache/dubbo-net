# 原生 Dubbo TCP（Hessian2）

[English](../../en/protocols/native-dubbo.md) · [中文首页](../index.md)

`dubbo://` Provider 由 `NativeDubboActuatorSuite` 通过 TCP 发送 Dubbo 帧。请求体使用 Hessian2 编码的 `$invoke` 泛化调用。

## 协议与路由字段

Provider URL 中与这条调用有关的字段：

- `dubbo=2.0.2`：Dubbo 经典协议版本。原生请求体的首字段也写入 `2.0.2`。
- `version=...`：服务版本，会写入请求体的 service version 和 attachments。
- `group=...`：服务分组，会写入 attachments。
- `dubbo://`：选择原生 TCP 执行器；`tri://` 的 HTTP/JSON 请求见 [Triple](triple.md)。

## 16 字节帧头

| 偏移 | 长度 | 含义 |
| --- | ---: | --- |
| 0 | 2 | Magic，固定为 `0xda 0xbb` |
| 2 | 1 | Flag：bit 7=request、bit 6=two-way、bit 5=event、低 5 位=serialization id |
| 3 | 1 | Response status；请求帧为 `0`，成功响应为 `20` |
| 4 | 8 | Request ID，大端序 `int64` |
| 12 | 4 | Body 长度，大端序 `int32` |

普通 Hessian2 双向请求的 flag 是 `0xc2`。普通 Hessian2 响应的低 5 位为 `2`。

## 泛化请求体

Body 依次编码以下 Hessian2 值：

1. Dubbo protocol version：`"2.0.2"`
2. Service path：完整接口名
3. Service version：没有版本时为 `null`
4. Method：`"$invoke"`
5. Parameter descriptor：`"Ljava/lang/String;[Ljava/lang/String;[Ljava/lang/Object;"`
6. 实际方法名
7. Java 参数类型名数组，例如 `["java.lang.String", "java.lang.Long"]`
8. 参数值数组
9. Attachments Map

Java 参数类型由调用方指定，或由[元数据模块](../architecture/metadata.md)推断；泛化调用不要求 .NET 持有 Java 接口 JAR。

默认 attachments 包含：

- `path`
- `interface`
- `dubbo=2.0.2`
- `generic=true`
- `timeout`
- 可选的 `version`、`group` 和调用方自定义字段

复杂 POJO 参数以泛化 Map 发送，并包含 `class=<Java FQCN>`。例如参数类型为 `com.foo.User` 时，匿名对象 `{ name = "demo" }` 会转换为：

```json
{
  "class": "com.foo.User",
  "name": "demo"
}
```

## 响应体

当 status 不是 `20` 时，Body 是 Hessian2 字符串，内容通常是 Dubbo/RpcException 错误详情。

当 status 为 `20` 时，第一个 Hessian2 int 是结果类型：

| 值 | 结构 |
| ---: | --- |
| 0 | Exception |
| 1 | Value |
| 2 | Null value |
| 3 | Exception + attachments |
| 4 | Value + attachments |
| 5 | Null value + attachments |

解码后的值会递归转换成 .NET 标量、`List<object>` 和 `Dictionary<string, object>`；同时保留 POJO 的 `class`、嵌套集合、二进制值和响应 attachments。循环引用会表示为 `"[Circular reference]"`，避免 JSON 序列化递归溢出。

`DubboRequestResult` 可取得：

- `Result`：JSON 字符串
- `RawResult`：JSON 化之前的动态 .NET 值
- `RequestId`
- `ResponseStatus`
- `SerializationId`
- `ResponseAttachments`
- `RequestElapsed`

经典 Dubbo 响应默认不包含 Provider 方法执行耗时，因此原生结果的 `ServiceElapsedAvailable` 为 `false`。

## 支持范围与连接行为

- 只实现 Hessian2（serialization id `2`）。
- 单个 TCP 连接通过 Request ID 复用并发请求；写入串行化，响应可以乱序返回。
- Body 默认限制为 8 MiB。
- 会应答 Provider 发来的双向 event/heartbeat 帧。
- 连接写入失败后不自动重发业务请求；后续请求重新建连。
