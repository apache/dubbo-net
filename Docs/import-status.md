# Import status / 导入状态

This is a compact record of **outstanding questions and historical verification**, not a legal determination or a claim that ASF IP clearance is complete. See the [import PR #1](https://github.com/apache/dubbo-net/pull/1) and [import merge](https://github.com/apache/dubbo-net/commit/e10f6d50e97e15bb1164da3c8beccc2380534db5) for provenance. Maintainers must verify the current status with the relevant records; a passing file-header check or merged PR alone does not settle authorization.

本页简要保留**待核事项与历史验证边界**，不构成法律结论，也不表示 ASF IP clearance 已完成。来源见[导入 PR #1](https://github.com/apache/dubbo-net/pull/1)及[导入合并提交](https://github.com/apache/dubbo-net/commit/e10f6d50e97e15bb1164da3c8beccc2380534db5)。实际进展须由维护者核对相关记录；文件头检查通过或 PR 合并不等于授权流程完成。

## Outstanding / 待核

- Confirm the applicability of the `connect-go` statement at the end of the root `LICENSE`, and how the original MIT provenance is accounted for. / 确认根目录 `LICENSE` 末尾 `connect-go` 声明的适用性及原 MIT 来源记录的处理。
- Confirm SGA/ICLA filing and the authorization scope covering other contributors and the imported history. Author confirmation for a specific file is not proof of clearance for the entire import. / 核对 SGA/ICLA 归档、其他贡献者及导入历史的授权范围；个别文件的作者确认不能证明整体接收流程完成。

## Historical external verification / 历史外网验证

The import review recorded **8 passed / 46 failed** ZooKeeper cases (54 total) and **8 passed / 24 failed** Nacos cases (32 total) on the explicitly configured external environment; normal RPCs were disconnected or timed out. These were results of that run, **not current CI results or evidence of full production compatibility**. Retest against available Providers and a working network before making a compatibility claim. Details remain in [PR #1](https://github.com/apache/dubbo-net/pull/1) and the [original review record in Git history](https://github.com/apache/dubbo-net/blob/e10f6d50e97e15bb1164da3c8beccc2380534db5/Docs/ImportReviewStatus.md).

导入审查记录中的显式外网测试结果为 ZooKeeper **8 通过 / 46 失败**（共 54 项），Nacos **8 通过 / 24 失败**（共 32 项）；正常 RPC 发生断连或超时。这是**当时的实测记录，不是当前 CI 结果，也不能证明生产兼容性已完整验证**。需要在可用 Provider 与网络环境中重新验证后再作兼容性声明。详情见 [PR #1](https://github.com/apache/dubbo-net/pull/1)及 [Git 历史中的原审查记录](https://github.com/apache/dubbo-net/blob/e10f6d50e97e15bb1164da3c8beccc2380534db5/Docs/ImportReviewStatus.md)。
