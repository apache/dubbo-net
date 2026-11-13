# Triple HTTP/JSON（Java Interface Unary）

[English](../../en/protocols/triple.md) · [中文首页](../index.md)

`tri://` Provider 由 `TripleDubboActuatorSuite` 通过 HTTP/1.1 和 JSON 调用 Dubbo 3.3+ Java Interface 的一元方法。

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

同名方法有多个 Java 签名时，客户端若能从元数据识别重载，就在负载均衡前限定使用 `dubbo://`；若只有 `tri://` Provider，则返回兼容性失败结果。非重载方法仍可使用 Triple HTTP/JSON。

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

## 支持范围

当前支持：

- Dubbo 3.3+ Java Interface
- Unary
- HTTP/1.1
- `application/json`
- ZooKeeper 经典接口级 `tri://` Provider 发现

当前不支持：

- Protobuf/IDL 服务
- Client/Server/Bidirectional Streaming
- gRPC/Triple 二进制 framing
- TLS、HTTP/2、HTTP/3 的客户端协商
- Dubbo 3 应用级服务发现
