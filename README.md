# MiaoNet

MiaoNet 是面向大量玩家场景的 Celeste 联机项目，也是对 CelesteNet 思路的一次重写。为避免与早期基于 CelesteNet 的 `Miao.CelesteNet` 混淆，也可称为 MiaoNet+。

项目仍处于早期开发阶段，主要开发分支为 `wip`。当前仓库包含 Everest 客户端 Mod、独立服务端、共享协议代码、模拟客户端、聊天组件、测试和数据包检查工具。

## 编译时认证模式

`Release` 默认启用 CeleMiao OAuth 认证，并要求服务端配置
`MiaoServer:Authentication` 下的 `ClientID`、`ClientSecret` 和
`EncryptionPassword`；任一配置缺失或为空时，服务端会拒绝启动。
`Debug` 默认保留无需论坛账号的本地开发认证。

认证模式会同时影响客户端登录流程和服务端认证器，两端应使用相同设置构建。如需显式覆盖默认值，可向 MSBuild 传入
`-p:UseCeleMiaoAuth=true` 或 `-p:UseCeleMiaoAuth=false`。发布到公网的构建不应关闭该选项。

## 项目结构

| 路径 | 说明 |
|---|---|
| `docs/` | 开发、贡献与问题反馈文档 |
| `source/MiaoNet.Client/` | Everest 客户端 Mod，包括连接、同步、聊天、命令、Ghost 和 Mod 资源 |
| `source/MiaoNet.ClientShared/` | 客户端与 MockClient 共用的 TLS/TCP 连接代码 |
| `source/MiaoNet.Server/` | 基于 .NET Generic Host 的独立服务端，包括认证、TLS、状态、管理 API 和指标 |
| `source/MiaoNet.Shared/` | 客户端与服务端共享的协议、数据结构和二进制序列化代码 |
| `source/ChatInputBox/` | 可复用的聊天输入、历史记录、标签页和补全组件，附 Everest 示例项目 |
| `source/MiaoNet.MockClient/` | 本地连接与基础压测用模拟客户端 |
| `source/MiaoNet.UnitTest/` | MSTest 测试项目 |
| `source/PacketDumpInspector/` | MiaoNet 数据包转储检查工具 |

更详细的入口见：

- [开发上手指南](docs/developing-MiaoNet.md)
- [客户端架构](source/MiaoNet.Client/docs/client-arch.md)
- [服务端架构](source/MiaoNet.Server/docs/server-arch.md)
- [共享包系统](source/MiaoNet.Shared/docs/packet-system.md)
- [ChatInputBox](source/ChatInputBox/ChatInputBox/docs/chatinputbox.md)

## 环境要求

- .NET 10 SDK。仓库的 `global.json` 允许从 8.0 向最新主版本滚动，但服务端和测试项目以 `net10.0` 为目标，因此完整构建需要 .NET 10。
- 构建客户端时需要 Celeste，以及 Everest 4465 或更高版本。
- Git。

## 快速开始

```bash
git clone https://github.com/CelesteCNCoders/MiaoNet.git
cd MiaoNet
```

构建客户端时传入 Celeste 根目录。该目录应直接包含 `Celeste.dll` 和 `Celeste.Mod.mm.dll`：

```bash
dotnet build source/MiaoNet.Client/MiaoNet.Client.csproj \
  -p:CelesteRootPath=/path/to/Celeste
```

构建完成后会将程序集写入 `source/MiaoNet.Client/ModFolder/Code/`，并在 `Celeste/Mods/` 下创建指向 `ModFolder` 的 `MiaoNet_link`。启动 Celeste 后即可由 Everest 加载。

服务端和测试不依赖 Celeste：

```bash
dotnet build source/MiaoNet.Server/MiaoNet.Server.csproj
dotnet test source/MiaoNet.UnitTest/MiaoNet.UnitTest.csproj
```

完整的本地服务端、MockClient 和生产配置流程见[开发上手指南](docs/developing-MiaoNet.md)。

## 项目进度

当前计划与进度见 [GitHub Issue #2](https://github.com/CelesteCNCoders/MiaoNet/issues/2)。

## 服务端 HTTP 接口与管理后台

服务端内置了一个基于 `HttpListener` 的 HTTP 服务(见 `source/MiaoNet.Server/Http`):

- 公开接口: `GET /status`、`GET /metrics`
- 受保护接口(需要在请求头携带 `X-Api-Token`, 值为配置项 `MiaoServer:ApiToken`; 为空则不校验并会在启动时警告):
  `DELETE /player`(按 `cid`/`aid` 踢人)、`POST /announce`(广播公告)、`/gc`
- 管理后台(配置节 `MiaoServer:AdminPanel`, 默认关闭): 通过论坛 OAuth 登录,
  仅拥有论坛 `miaonet.adminPanel` 权限的用户可进入(旧版论坛插件无此字段时回退为仅论坛管理员).
  论坛 oauth-center 中为 `GET /api/miaonet/admin-user` 配置了 scope 时,
  需把该 scope 标识填入 `AdminPanel:Scope`, 否则登录时会因 `insufficient_scope` 失败.
  入口为 `GET /admin/login`, 页面为单页应用, 包含
  仪表盘 / 玩家 / 聊天 / 日志 / 指标 五个标签页, 数据通过以下 JSON API 实时刷新
  (均需登录会话 Cookie, 未登录返回 401 JSON):
  - `GET /admin/api/players`: 在线玩家与频道列表
  - `POST /admin/api/kick`: 踢出并临时冻结玩家, 请求体 `{ "authID": ..., "connectionID": ..., "reason": "...", "freezeMinutes": ... }`
    (id 至少给一个, 原因与冻结分钟数必填; 冻结期间该账号无法登录)
  - `POST /admin/api/announce`: 广播公告, 请求体 `{ "message": "..." }`
  - `GET /admin/api/logs?after=<id>&limit=<n>`: 实时服务器日志(环形缓冲区, 最多保留最近 1000 条)
  - `GET /admin/api/chat?after=<id>`: 实时玩家聊天与服务器公告(私聊不记录)
  - `GET /admin/api/metrics`: 当前指标快照与最近约 1 小时的时间序列(每 5 秒采样)

注意: 默认监听前缀 `MiaoServer:HttpListenerPrefix` 为 `http://localhost:21474/`, 只监听本机回环地址.
若要对公网开放管理后台或 API, 需将其改为如 `http://+:21474/` 的前缀(或配置反向代理),
并且**务必**配置强随机的 `MiaoServer:ApiToken`.

## 参与贡献

提交代码前请阅读 [Contributing.md](Contributing.md) 和[编码规范](docs/coding-style.md)。问题反馈格式见[反馈规范](docs/how-to-issue.md)。

## License

许可文本见 [LICENSE.txt](LICENSE.txt)。项目部分参考了 [CelesteNet](https://github.com/0x0ade/CelesteNet) 的实现、约定与部分图片资源。

## Credits

- sky scale：绘制直播模式和合影模式图标（`live_mode.png`、`group_photo_mode.png`）。
