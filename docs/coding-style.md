# MiaoNet 编码规范

项目的基础格式由 `source/.editorconfig` 和构建属性决定。提交前应以现有文件风格和分析器结果为准。

## 基本风格

- C# 使用 `LangVersion=preview`（测试项目为 `latest`）。
- 启用 Nullable Reference Types，正确表达可空状态，避免无理由使用 `!`。
- 使用 4 个空格缩进和 Allman 花括号风格。
- 公共类型和成员使用 PascalCase；局部变量、参数和私有字段使用 camelCase；接口使用 `I` 前缀。
- 保留有上下文的异常和日志，不静默吞掉异常。

## 项目约定

- 协议包放在 `source/MiaoNet.Shared/Packet/Packets/`。
- 在 `source/MiaoNet.Shared/AssemblyInfo.cs` 的 `PacketRegistry` 列表末尾追加新包。重排或插入会改变线上协议 ID。
- 二进制数据结构实现 `IRefBinarySerializable<T>` 或 `IContextualRefBinarySerializable<T, TContext>`；包实现对应的 contextless/contextual 接口。
- 请求和响应通过 `PacketRequest<TResponse>`、`PacketResponse` 及相同的 `RequestID` 关联。
- 平台差异使用现有条件编译符号：`MIAO_CLIENT`、`MIAO_SERVER`、`MIAO_MOCKCLIENT` 和 `INSPECTOR`。
- 调试或部署行为通过 MSBuild 属性控制：`UseLocalhostPfx`、`UseCeleMiaoAuth`；不要硬编码个人环境路径或凭据。
- `MiaoNet.Shared`、`MiaoNet.ClientShared` 和 `ChatInputBox` 以源码形式链接到消费者。改动后应构建实际消费者并运行相关测试。
- 受游戏引擎限制需要 Hook 或 patch 时，先检查 `Game/` 和 `ModInterop/` 中是否已有对应生命周期与兼容模式。

## 验证

根据改动范围运行测试与构建：

```bash
dotnet test source/MiaoNet.UnitTest/MiaoNet.UnitTest.csproj
dotnet build source/MiaoNet.Server/MiaoNet.Server.csproj
dotnet build source/MiaoNet.Client/MiaoNet.Client.csproj \
  -p:CelesteRootPath=/path/to/Celeste
```
