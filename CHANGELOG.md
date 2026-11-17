# Changelog / 变更索引

This file is a Git index, not a second implementation history. Each link below resolves to a merged PR or Git object; source and tests remain the authority for current behavior. For usage, see the [English README](README.md) or [中文 README](readme_cn.md); for code-reading routes, see [English](Docs/en/architecture/index.md) and [中文](Docs/zh/architecture/index.md). Design rationale belongs in the [RFC convention](Docs/en/rfc.md) and future decision-specific RFCs.

本文件只提供 Git 索引，不重复编写实施过程。下列链接均指向已合并 PR 或 Git 对象；当前行为仍以源码和测试为准。使用说明参见英文或中文 README，读代码从中英文代码地图进入，架构取舍参见 [RFC 约定](Docs/zh/rfc.md)。

## Unreleased / 未发布

**No release is asserted here.** The project file declares a package version, but that alone is not a verified Git tag or published release. The [comparison since the import merge](https://github.com/apache/dubbo-net/compare/e10f6d50e97e15bb1164da3c8beccc2380534db5...main) is a moving view of `main`, not a release boundary. When a real tag or release exists, add its verified link and a short user-visible summary; do not relabel these entries as a historical release.

**此处不声明已经发布。** 项目文件中的包版本号不能单独证明 Git tag 或 Release 已发布。[导入合并后至 main 的比较视图](https://github.com/apache/dubbo-net/compare/e10f6d50e97e15bb1164da3c8beccc2380534db5...main)会随 `main` 变化，不是发行版本边界。将来有可核验的 tag/release，再补链接及简短的用户可见摘要，不倒填虚构版本。

- Import provenance / 导入来源：[PR #1](https://github.com/apache/dubbo-net/pull/1), [merge commit](https://github.com/apache/dubbo-net/commit/e10f6d50e97e15bb1164da3c8beccc2380534db5). Outstanding authorization questions and historical external verification are tracked in [import status](Docs/import-status.md) / 待核授权事项和历史外网验证边界见[导入状态](Docs/import-status.md)。
- Protocol wire constants and Hessian2 reader boundaries / 协议字节常量与 Hessian2 读取边界：[PR #18](https://github.com/apache/dubbo-net/pull/18), [PR #21](https://github.com/apache/dubbo-net/pull/21).
- Registry paths, Provider URL keys and defaults / 注册路径、Provider URL 字段与默认值：[PR #22](https://github.com/apache/dubbo-net/pull/22), [PR #24](https://github.com/apache/dubbo-net/pull/24), [PR #28](https://github.com/apache/dubbo-net/pull/28).
- Telnet and metadata naming / Telnet 与元数据命名：[PR #29](https://github.com/apache/dubbo-net/pull/29), [PR #30](https://github.com/apache/dubbo-net/pull/30).
- Reproducible end-to-end validation / 可复现端到端验证：[PR #23](https://github.com/apache/dubbo-net/pull/23); see the [environment guide](Tests/EndToEndTestEnvironment/README.md) for how to run it / 运行方法见环境指南。
