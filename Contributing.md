# 参与 MiaoNet

欢迎通过报告问题、提出设计建议、完善文档、补充测试或提交代码参与 MiaoNet。

开始开发前，请先阅读[开发上手指南](docs/developing-MiaoNet.md)并跑通与你改动相关的项目。代码应遵循[编码规范](docs/coding-style.md)。涉及协议、共享数据结构、连接流程或大范围重构的改动，建议先通过 GitHub Issue 或 Discussion 与维护者确认设计。

## 提交问题

Bug 报告和功能建议请参考[反馈规范](docs/how-to-issue.md)。提交前先搜索已有 Issue，并提供版本、复现步骤、期望行为和相关日志。

## 提交代码

1. Fork 仓库，从 `wip` 分支创建功能分支。
2. 使用 `feat/`、`fix/` 或 `refactor/` 等能说明目的的分支名。
3. 保持改动聚焦；协议包 ID 列表只能追加，不能重排已有项。
4. 运行与改动相关的构建和测试，并在 PR 描述中记录结果。
5. 向 `wip` 分支提交 Pull Request，说明行为变化、兼容性影响和测试方式。

完整测试需要 .NET 10 SDK；客户端构建还需要安装 Everest 的 Celeste。常用命令：

```bash
dotnet test source/MiaoNet.UnitTest/MiaoNet.UnitTest.csproj
dotnet build source/MiaoNet.Server/MiaoNet.Server.csproj
dotnet build source/MiaoNet.Client/MiaoNet.Client.csproj \
  -p:CelesteRootPath=/path/to/Celeste
```
