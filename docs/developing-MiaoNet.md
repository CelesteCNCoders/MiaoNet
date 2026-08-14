# MiaoNet 开发上手指南

MiaoNet 是 Celeste 的 C/S 联机项目。客户端作为 Everest Mod 运行，服务端基于 .NET Generic Host，双方复用同一套协议和序列化源码。

## 前置要求

- .NET 10 SDK。客户端、MockClient 和工具以 `net8.0` 为目标，服务端和测试以 `net10.0` 为目标。
- 构建客户端需要 Celeste 和 Everest 4465+。
- Git。
- 支持 .NET 的 IDE，例如 Visual Studio、Rider 或 VS Code。

仓库使用 `global.json`，允许 SDK 从 8.0 滚动到更新的主版本。可用以下命令确认环境：

```bash
dotnet --version
dotnet sln MiaoNet.slnx list
```

`MiaoNet.Shared`、`MiaoNet.ClientShared` 和 `ChatInputBox` 是 `Microsoft.Build.NoTargets` 源码项目，实际代码通过 `Compile Include` 链接进消费者。若 IDE 对这些项目的分析异常，可暂时卸载对应的 NoTargets 项目；这不影响消费者项目的构建。

## 设置 Celeste 路径

客户端构建需要 `CelesteRootPath` 指向游戏根目录，该目录应直接包含 `Celeste.dll`、`Celeste.Mod.mm.dll` 和 Everest 依赖程序集。推荐在命令行传入：

```bash
dotnet build source/MiaoNet.Client/MiaoNet.Client.csproj \
  -p:CelesteRootPath=/path/to/Celeste
```

Windows 的 Steam 默认路径通常是：

```text
C:\Program Files (x86)\Steam\steamapps\common\Celeste
```

Linux 的 Steam 安装通常位于：

```text
~/.local/share/Steam/steamapps/common/Celeste
```

如需长期覆盖，可在 IDE 的项目构建参数中配置该属性。不要把个人绝对路径提交到共享的 `Directory.Build.props` 或项目文件。

## 项目概览

| 项目 | 目标框架 | 用途 |
|---|---|---|
| `MiaoNet.Client` | `net8.0` | Everest 客户端 Mod |
| `MiaoNet.Server` | `net10.0` | 独立服务端 |
| `MiaoNet.Shared` | `net8.0` NoTargets | 协议与共享数据源码 |
| `MiaoNet.ClientShared` | `net8.0` NoTargets | 客户端连接源码 |
| `MiaoNet.MockClient` | `net8.0` | 模拟连接与基础压测 |
| `ChatInputBox` | `net8.0` NoTargets | 聊天 UI 源码组件 |
| `ChatInputBoxExample` | `net8.0` | ChatInputBox Everest 示例 |
| `MiaoNet.UnitTest` | `net10.0` | MSTest 测试 |
| `PacketDumpInspector` | `net8.0` | 数据包转储检查工具 |

## 构建与测试

构建客户端：

```bash
dotnet build source/MiaoNet.Client/MiaoNet.Client.csproj \
  -p:CelesteRootPath=/path/to/Celeste
```

客户端项目使用 `ModAssetsCopyType=link`。构建后输出会进入 `source/MiaoNet.Client/ModFolder/Code/`，并在 `Celeste/Mods/MiaoNet_link` 创建符号链接。Windows 创建目录链接可能需要开发者模式或管理员权限。

构建服务端并运行测试：

```bash
dotnet build source/MiaoNet.Server/MiaoNet.Server.csproj
dotnet test source/MiaoNet.UnitTest/MiaoNet.UnitTest.csproj
```

构建整个解决方案也会包含两个 Everest 项目，因此仍需有效的 `CelesteRootPath`：

```bash
dotnet build MiaoNet.slnx -p:CelesteRootPath=/path/to/Celeste
```

## 本地联调

### 编译属性

属性定义在 `source/Directory.Build.props`，并转换为同名的大写条件编译符号：

| MSBuild 属性 | 默认值 | 行为 |
|---|---|---|
| `UseLocalhostPfx` | Debug 为 `true`，其他配置为 `false` | 内嵌 `source/localhost.pfx`；客户端连接 `127.0.0.1:21473` 并接受本地证书 |
| `UseCeleMiaoAuth` | `false` | 为 `true` 时使用 CeleMiao OAuth 认证，否则使用 `CustomAuthenticator` |

客户端 Debug 构建还会定义 `PACKET_TRACING`，用于输出数据包追踪日志。

### 启动服务端

```bash
dotnet run --project source/MiaoNet.Server/MiaoNet.Server.csproj
```

Debug 服务端默认读取：

- `source/MiaoNet.Server/appsettings.json`
- `source/MiaoNet.Server/appsettings.Development.json`
- `source/MiaoNet.Server/content.json`
- 前缀为 `MIAONET:` 的环境变量

默认游戏连接监听 `0.0.0.0:21473`，内部 HTTP 管理接口监听 `http://localhost:21474/`。命令行工作目录必须能找到上述 JSON 文件；`dotnet run --project` 会使用项目目录。HTTP 端点见[服务端 API 文档](../source/MiaoNet.Server/Http/doc.md)。

### 启动 MockClient

服务端运行后执行：

```bash
dotnet run --project source/MiaoNet.MockClient/MiaoNet.MockClient.csproj
```

输入实例数量后，每个实例会以随机名称连接 `127.0.0.1:21473`，进入 `Celeste/LostLevels`，发送帧更新，并响应 Ping 与传送请求。MockClient 使用本地开发证书配置，适合验证连接、广播和基础压力，不替代真实游戏客户端测试。

### 推荐流程

1. 启动 Debug 服务端。
2. 可选启动一个或多个 MockClient。
3. Debug 构建客户端并启动 Celeste。
4. 验证登录、频道/地图切换、聊天标签页、Ghost 同步与断线清理。

## 生产配置

Release 构建默认关闭本地证书模式。服务端需在 `MiaoServer` 配置节提供真实证书路径：

```json
{
  "MiaoServer": {
    "Certificate": {
      "CertificatePath": "/path/to/cert.pem",
      "CertificateKeyPath": "/path/to/key.pem"
    }
  }
}
```

服务端会监听证书文件变化并重新加载。认证、网络、超时和 HTTP 前缀的完整默认值见 `source/MiaoNet.Server/appsettings.json`；公告文本来自 `content.json`。生产环境还会将日志写入 `logs/yyyy-MM-dd.log`。

## 开发约定

- 主开发分支是 `wip`，从它创建功能分支并向它提交 PR。
- 遵循[编码规范](coding-style.md)。
- 修改协议前先阅读[包系统](../source/MiaoNet.Shared/docs/packet-system.md)。包注册 ID 取决于 `AssemblyInfo.cs` 中的顺序，已有类型不能重排。
- 修改共享源码时，至少构建一个实际消费者；NoTargets 项目本身不生成运行程序集。
- 架构入口见[客户端文档](../source/MiaoNet.Client/docs/client-arch.md)、[服务端文档](../source/MiaoNet.Server/docs/server-arch.md)和[共享接口说明](../source/MiaoNet.Shared/docs/shared-interface.md)。

适合初次参与的任务包括补充共享序列化测试、完善 MockClient 协议覆盖、修正文档以及处理范围较小的 Issue。
