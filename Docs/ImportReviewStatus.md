# Apache 导入 PR：历史工具、执行器清理与剩余审核项

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
| 当前仓库已无运行时调用的旧普通 HTTP/JSON 执行器 | `DubboService/HttpDubboActuatorSuite.cs` |

共删除 13 个完整文件，并精简一个仍在使用的字节工具文件。删除内容可通过 Git 历史恢复，没有改写既有历史。

## 保留及原因

| 内容 | 实际使用 |
|---|---|
| `MyCommonHelper/MyLogger.cs` | 注册中心、消费者注册、元数据、调用及 Telnet 的日志；README 中也公开说明日志级别配置 |
| `MyCommonHelper/MyBytes.cs` 的字节格式化接口及枚举 | Telnet 协商错误信息、字节诊断日志 |
| `MyCommonHelper/EncryptionHelper/MyHash.cs` | 一致性 Hash 的 MurmurHash2；这是非密码学路由散列，不是加密功能 |
| `NetService/Telnet/ExTelnet.cs` | `DubboActuator` 的实际 Telnet 连接、元数据兼容路径及单元测试 |
| `TelnetMemoryStream`、`TelnetOptionHelper`、`TelnetStaticHelper`、`TelnetMessageType` | `ExTelnet` 的接收缓冲、协商处理、连接辅助及消息类型 |

`DubboActuatorProtocolType.Http`、兼容参数序列化分支及对应的模拟执行器测试保留，避免把协议扩展点与某个具体历史实现一并删除。当前自动服务发现工厂只创建 Native、Triple、Telnet 执行器；`tri://` 使用独立的 `TripleDubboActuatorSuite`，不依赖被删除的旧普通 HTTP 类。

## 这些删除解决的审核问题

1. **进程级 TLS 校验被关闭**：带有全局 `ServicePointManager.ServerCertificateValidationCallback` 修改的 `MyWebTool.MyHttp` 已从源码和程序集移除。
2. **无用加密工具随包分发**：RC4、DES 等历史工具和其他未使用加密封装已删除，保留用于路由的 MurmurHash2。
3. **MyRSA 缺陷**：包含缺少 `throw`、吞异常及硬编码密码校验等问题的整个未使用类已移除，而不是继续对外分发。
4. **来源待确认文件减少**：审核列出的五个文件中，`MyWebTool.cs`、`EsTelnet.cs`、`MyEncryption.cs`、`MyExtensionMethods.cs` 已删除；当前源码树只保留实际使用的 `ExTelnet.cs`。
5. **旧 HTTP Suite 的连接池生命周期问题**：当前生产代码、测试和示例都没有实例化该历史实现，已按所有者要求删除整个 `HttpDubboActuatorSuite`，并清理配置 XML 文档中的失效引用。该条通过移除旧实现解决，不是声称修复了其 `Dispose`，也不代表改造了 Triple 连接池。

第 1–3 项覆盖 [Issue #3](https://github.com/apache/dubbo-net/issues/3) 的代码清理要求。第 4 项只是缩小当前源码树的待审查范围，**不等于完成 IP clearance**：历史提交仍保留，贡献与捐赠授权的覆盖范围仍须维护者和贡献者确认。

## 个人信息与历史注释清理

- 删除 `ExTelnet.cs` 中旧的个人创建记录注释块，包括个人手机号、邮箱和版权行；不改变代码行为。
- 原版权年份及权利人移至 `NOTICE`，保留来源记录；未改动原 MIT 许可证正文。原作者确认授权后，不再保留与此次 Apache-2.0 贡献声明冲突的旧 `All rights reserved.` 限制语句。
- Git 历史未改写，历史版本仍保留原注释；本次不涉及从历史记录彻底移除个人信息。

## 作者授权确认与 ASF 文件头

- 项目所有者在确认 `ExTelnet.cs` 原作者及贡献权利的问题后，明确答复“是我本人授权贡献”，并要求处理来源及 ASF 文件头两项审核意见。此确认用于说明该来源并非未经许可引入的陌生第三方代码。
- 按原作者明确要求迁移个人版权记录至 `NOTICE`，移除源码个人联系方式；不改动运行时代码。
- 经说明 ASF 文件头与个人版权注释的区别后，所有者明确要求完成这两项并更新 PR。该轮为 69 个历史 C# 文件机械补齐标准 ASF 文件头，保留已有的 3 个；该提交时全部 72 个受版本控制的 C# 文件都有文件头，原有 BOM 与运行时代码保持不变。本轮新增测试辅助文件同样包含 ASF 文件头。
- 逐文件剥离新增文件头及已授权移除的个人记录后比较代码，确认没有运行时逻辑变更；仅 `BaseTest.cs`、`Usings.cs` 额外补了原先缺少的文件末尾换行。
- 将固定版本 SkyWalking Eyes 文件头检查接入 push / PR 自动检查。只读权限、不自动修复或推送、不发表机器人评论；未通过排除 C# 文件来规避检查。检查覆盖的非 C# 文本文件同样补齐适用文件头。
- **作者授权确认不等于 ASF 已完成 SGA/ICLA 归档或整个历史的 IP clearance。** 此类行政接收记录及其他贡献者覆盖范围仍交由维护者核验，在许可范围待办中继续保留，不据此宣称整个捐赠流程已完成。

## 本轮处理的两项行内讨论

1. **历史代码来源及授权**：记录所有者本人对所问 `ExTelnet.cs` 代码的作者身份及贡献授权确认；原五个被点名文件中，四个此前已作为无用工具删除，仍在使用的 `ExTelnet.cs` 本轮保留、清理个人联系方式并添加 ASF 文件头。原版权归属迁入 `NOTICE`，原 MIT 文本及既有 Git 历史保留。SGA/ICLA 归档及其他贡献的范围不在此冒充完成。
2. **历史 ASF 文件头**：补齐标准头并启用自动检查，以实际 SkyWalking Eyes `header check` 及全量构建、测试结果作为 PR 回复依据；不将文件头通过等同于授权行政流程通过。

## 本轮 .NET 10、运行时地址与开发配置整理

- 根据所有者明确决定，类库、单元测试、集成程序和 QueryDemo 统一改为 `net10.0`，CI 使用 .NET 10 SDK。README 明确这是最低目标升级，不再承诺当前分支生成的包可供 .NET 6/7/8/9 应用直接引用；未替换已发布包。
- 两个可执行测试/示例工程共享仅限测试的运行参数解析：默认 ZooKeeper `localhost:2181`、Nacos `nacos://localhost:8848`、直连主机 `localhost`。支持 `--host`、`--registry`、`--provider-host`、`--nacos`、相应环境变量及旧的位置参数；不内置个人公网地址。
- `--print-config` 和 `--help` 在 DNS/注册中心/RPC 操作前返回。主机名在直连前解析成 IP，并优先 IPv4，避免将 `localhost` 直接传给只接受 IP 的 Native 执行器；Provider 主机可独立于注册中心指定。
- 删除未被源码、项目或 CI 引用的 `.vscode/tasks.json`、`.vscode/launch.json`、`compose.yaml`、`compose.debug.yaml`。删除的是 VS Code 一键任务与 Compose 调试便利入口，不影响 `dotnet` 命令行、库 API 或 CI；Git 历史仍可恢复这些文件。
- 保留可独立使用的集成 Dockerfile 和 `.dockerignore`。Docker 基础镜像升级为 .NET 10，复用官方镜像的非 root 用户，容器构建不生成 NuGet 包；CI 补充构建镜像及 `--network none --print-config` 检查，不在 CI 中调用公网测试服务。
- 新增 33 项离线配置回归，完整单元测试为 **343 通过、0 失败、0 跳过**；包括本机默认值、命令行/环境覆盖、IPv6、非法参数、独立 Provider 和测试辅助类型不进入类库 API。
- 实测时仅在命令行显式传入所有者提供的测试主机：ZooKeeper 四版本矩阵 **54/54**，Nacos 两版本及目录 API **32/32**，均包含经典 Dubbo TCP 与 Triple 直连；实际结果不是来自无参公网默认值。
- QueryDemo 的十种 QueryAsync 重载及两种直连已分别使用 ZooKeeper、Nacos 验证；Nacos 额外验证发现示例未指定测试环境的元数据分组，已在示例配置中与版本根路径对齐，不修改类库的默认元数据规则。
- `--print-config` 已在无运行参数、本机 Nacos、不可解析域名场景验证返回配置而不发起连接；缺少参数值返回退出码 2。本地 nupkg 仅包含 `lib/net10.0`，新测试辅助类型不进入类库 API；没有发布包。

## 新增 11 条行内意见的处理（2026-11-01）

本轮在同一个导入 PR 中处理新增意见，不关闭重提，也不重写原始提交：

| 意见 | 处理 |
|---|---|
| Triple 双重超时 | 关闭内部 HttpClient 超时，以调用级 linked CTS 覆盖响应头与完整响应体；Provider timeout 继续优先于客户端默认值，可更长或更短 |
| 取消与超时混淆 | 保持 Native/Triple 的失败结果约定；Triple 明确区分调用方取消和超时（含毫秒数），同时取消时优先归为调用方取消，并在 README/Triple 文档说明 |
| build 工作流中的个人分支 | push 只保留 main，PR/main 和手动触发保持不变 |
| version/group 严格匹配 | README 兼容表明确升级后的精确匹配及无匹配时不回退 |
| 候选缓存清空策略 | 注释说明调用方可控键、1024 是缓存防御上限，以及完整清空后可低成本重建 |
| Nacos 快捷参数忽略环境地址 | --nacos 和 nacos 简写均尊重显式 Nacos 环境地址；遇到 ZooKeeper 地址明确报冲突，保留 --host 的显式覆盖规则 |
| 64 层限制的含义 | 错误信息区分 wire 嵌套与引用展开深度，文档说明浅层 wire 也可能展开为深链；本轮不增加公开配置 API |
| license 工作流中的个人分支 | 同样只保留 main 的 push，增加失败时查看 Actions 日志文件路径的提示 |
| Hessian 分块内部递归 | 字符串/二进制改为迭代拼接，二进制不再逐层复制完整后缀；长度、UTF-8、终止 tag 与游标边界均有回归 |
| Hessian 共享引用放大 | 返回值/异常与 attachments 共用 100,000 节点和 32 MiB 保守 JSON 体积预算，每次展开均计费；不把合法共享引用误标为循环 |
| Triple IPv6 地址 | 使用 UriBuilder，覆盖 IPv4/域名/带括号或不带括号 IPv6 和 IPEndPoint 入口 |

验证结果：

- .NET 10 Release 构建及完整单元测试通过：**388 通过、0 失败、0 跳过**（保留原 343 项，新增 45 项）。包含 20,000 个空/非空分块、异常响应字符串、DAG 放大、体积/节点预算、边界深度、公开工厂的真实 HTTP 响应头/正文超时和取消、IPv6 构造与条件回环测试，以及 Nacos 环境覆盖。
- SkyWalking Eyes 0.9.0 文件头检查：**79 有效、0 无效**，包含全部 76 个 C# 文件；未增加 C# 排除项。`git diff --check` 通过。
- 两个测试/示例程序的无参配置检查仍返回 localhost，不执行 DNS/网络操作。未将实测主机写入任何源码默认值。
- 本轮按所有者指定主机显式运行外网集成：ZooKeeper **8 通过 / 46 失败**（54 项），Nacos **8 通过 / 24 失败**（32 项）。注册中心/元数据可读，但正常 RPC 被断开或超时，不能沿用上一轮“全部通过”的结论；已有异常场景只断言失败，其通过也不证明收到 Provider 业务异常。
- QueryDemo 在两种注册中心下均未取得有效 RPC 结果，尽管旧示例程序的进程退出码为 0；不将退出码当成成功验证。
- 工程外的同一双协议直连探针对比修改前的 **74bbca3** 和当前实现：相同请求和 2 秒配置均在 Native/Triple 失败，Native 均超时，旧 Triple 返回通用取消、新 Triple 返回带时长的超时原因。尚未取得远端具体故障原因或完整成功的外网回归，需要恢复可用 Provider/网络后复测；不能据此声称生产兼容性已全部验证。
- 本地无 Docker 命令；容器构建及无网络配置检查交由 PR CI 执行，其结果以实际工作流为准，不宣称本地验证通过。

## 仍待确认的许可行内讨论

| 项目 | 剩余原因 / 后续动作 |
|---|---|
| LICENSE 与许可范围 | 确认原 MIT 来源记录的处理、根 LICENSE 末尾 `connect-go` 声明的适用性及所需 SGA/ICLA 归档/贡献范围；文件冲突与作者声明已处理，不代表整个行政接收流程已完成 |

另外，Apache 侧工作流审批、保留原始提交历史的最终合并方式仍由有权限的维护者处理。本轮不会发布包或直接更新 upstream `main`。

## 兼容性与验证

- 这是 Apache 导入分支的历史工具 API 清理，不会覆盖已发布的 NuGet 包。
- **直接使用这些历史工具类型或被删除方法的外部程序会发生源码/二进制兼容性变化**；项目内没有调用不代表能够证明所有外部程序也未调用。删除已得到项目所有者明确要求。
- 删除旧公开 `HttpDubboActuatorSuite` 同样属于有意的公开 API 移除；不能据此断言历史版本或所有外部调用方从未使用过它。
- 未修改 `DubboClient` 的业务调用 API、保留的日志接口、MurmurHash2 算法或字节格式化行为。
- 新增回归测试检查已删除类型不在最终程序集内、保留辅助类型、全部 30 种字节格式组合、空值/空数组，以及 Telnet 协商和转义处理。
- 全量构建、单元测试及本地打包结果另见 PR 的本轮更新记录；不得把构建通过作为已完成授权或安全审查的证明。

### 上一轮文件头与授权清理验证

- 使用官方 SkyWalking Eyes 0.9.0 发布工具，下载包 SHA-512 校验通过；实际 `header check` 结果为 75 个有效文件、0 个无效文件，其中包括全部 72 个 C# 源文件和 3 个 Git/Docker 文本文件。
- 在工程外的临时目录使用同一配置验证：缺少文件头的 C# 文件使检查返回失败；补齐后通过，再次补齐没有产生修改。该兼容检查没有加入工程。
- SDK 6.0.428 与 10.0.201 的全量 Release 构建均通过，完整单元测试均为 310 通过、0 失败、0 跳过。示例和集成程序仅构建，没有执行公网调用；原有编译器、平台及 .NET 6 EOL 警告仍存在。
- 本地打包确认新 `NOTICE` 与保留的完整 MIT 文本进入 nupkg，MIT 文本仍与原始 master 的 LICENSE 字节一致；未发布包。
