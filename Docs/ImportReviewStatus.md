# Apache 导入 PR：历史工具清理与剩余审核项

对应 [PR #1](https://github.com/apache/dubbo-net/pull/1)。本文件记录本轮清理范围，不代表所有授权、审核或发布要求已经完成。

## 清理依据

- 检查库、测试、示例、文档和项目文件的类型、方法与命名空间引用，以及仓库内反射使用。
- 扩展方法按方法名检查调用点，而不仅搜索类名；Telnet 实际使用的是 `TelnetStaticHelper.MyIndexOf`，不是被删除的同名历史扩展。
- 删除没有 DubboNet 使用方的完整工具文件；工具之间自相引用不构成保留理由。
- 对仍在使用的 `MyBytes`，保留 `ByteToHexString`、`HexaDecimal` 和 `ShowHexMode`，删除其余 15 个未使用的方法重载。保留方法的实现和枚举值未改变。

## 已删除

| 范围 | 文件 |
|---|---|
| 重复日志、随机字符串和通用扩展 | `MyCommonHelper/CommonLog.cs`、`MyCommonTool.cs`、`MyExtensionMethods.cs` |
| 未使用的加密工具 | `MyCommonHelper/EncryptionHelper/` 下的 `MyAES.cs`、`MyDES.cs`、`MyEncryption.cs`、`MyRC4.cs`、`MyRSA.cs`、`MyTripleDES.cs`、`MyXOR.cs` |
| 未使用的旧 HTTP 工具及其全部嵌套类型 | `NetService/MyWebTool.cs` |
| 已弃用且未使用的同步 Telnet 实现 | `NetService/Telnet/EsTelnet.cs` |

共删除 12 个完整文件，并精简一个仍在使用的字节工具文件。删除内容可通过 Git 历史恢复，没有改写既有历史。

## 保留及原因

| 内容 | 实际使用 |
|---|---|
| `MyCommonHelper/MyLogger.cs` | 注册中心、消费者注册、元数据、调用及 Telnet 的日志；README 中也公开说明日志级别配置 |
| `MyCommonHelper/MyBytes.cs` 的字节格式化接口及枚举 | Telnet 协商错误信息、字节诊断日志 |
| `MyCommonHelper/EncryptionHelper/MyHash.cs` | 一致性 Hash 的 MurmurHash2；这是非密码学路由散列，不是加密功能 |
| `NetService/Telnet/ExTelnet.cs` | `DubboActuator` 的实际 Telnet 连接、元数据兼容路径及单元测试 |
| `TelnetMemoryStream`、`TelnetOptionHelper`、`TelnetStaticHelper`、`TelnetMessageType` | `ExTelnet` 的接收缓冲、协商处理、连接辅助及消息类型 |

## 这些删除解决的审核问题

1. **进程级 TLS 校验被关闭**：带有全局 `ServicePointManager.ServerCertificateValidationCallback` 修改的 `MyWebTool.MyHttp` 已从源码和程序集移除。
2. **无用加密工具随包分发**：RC4、DES 等历史工具和其他未使用加密封装已删除，保留用于路由的 MurmurHash2。
3. **MyRSA 缺陷**：包含缺少 `throw`、吞异常及硬编码密码校验等问题的整个未使用类已移除，而不是继续对外分发。
4. **来源待确认文件减少**：审核列出的五个文件中，`MyWebTool.cs`、`EsTelnet.cs`、`MyEncryption.cs`、`MyExtensionMethods.cs` 已删除；当前源码树只保留实际使用的 `ExTelnet.cs`。

第 1–3 项覆盖 [Issue #3](https://github.com/apache/dubbo-net/issues/3) 的代码清理要求。第 4 项只是缩小当前源码树的待审查范围，**不等于完成 IP clearance**：历史提交仍保留，贡献与捐赠授权的覆盖范围仍须维护者和贡献者确认。

## 仍开放的七项行内讨论

| 项目 | 剩余原因 / 后续动作 |
|---|---|
| LICENSE 与许可范围 | 确认原 MIT 来源记录的处理及根 LICENSE 末尾 `connect-go` 声明的适用性；文件冲突已解决，不代表授权完成 |
| .NET 6 兼容目标 | 在 [#5](https://github.com/apache/dubbo-net/issues/5) 决定支持中 LTS / 多目标及是否继续兼容 .NET 6；本轮不改变目标框架 |
| 历史来源授权 | `ExTelnet.cs` 仍有原始来源/版权声明；原历史和其他贡献的 SGA/ICLA 范围也未确认，不擅自重标版权 |
| 历史 ASF 文件头 | [#2](https://github.com/apache/dubbo-net/issues/2) 尚未完成；来源明确的文件可分批补齐，相关的自动许可证强制检查仍属于 [#4](https://github.com/apache/dubbo-net/issues/4) 的后续工作 |
| 测试默认公网注册中心 | [#6](https://github.com/apache/dubbo-net/issues/6) 仍需改为本地默认值、显式覆盖及可复现环境；本轮不执行公网集成测试 |
| HTTP 连接池生命周期 | 需结合共享引用、Dispose 和 Provider 变更测试选择优化方案；不是本次删除带来的问题 |
| 开发脚手架组织 | 与维护者确认保留或迁移可选的 VS Code / Docker 开发配置；不随清理删除在用功能 |

另外，Apache 侧工作流审批、保留原始提交历史的最终合并方式仍由有权限的维护者处理。本轮不会发布包或直接更新 upstream `main`。

## 兼容性与验证

- 这是 Apache 导入分支的历史工具 API 清理，不会覆盖已发布的 NuGet 包。
- **直接使用这些历史工具类型或被删除方法的外部程序会发生源码/二进制兼容性变化**；项目内没有调用不代表能够证明所有外部程序也未调用。删除已得到项目所有者明确要求。
- 未修改 `DubboClient` 的业务调用 API、保留的日志接口、MurmurHash2 算法或字节格式化行为。
- 新增回归测试检查已删除类型不在最终程序集内、保留辅助类型、全部 30 种字节格式组合、空值/空数组，以及 Telnet 协商和转义处理。
- 全量构建、单元测试及本地打包结果另见 PR 的本轮更新记录；不得把构建通过作为已完成授权或安全审查的证明。
