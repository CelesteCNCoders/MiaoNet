# MiaoNet 服务端架构

`MiaoNet.Server` 是目标框架为 `net10.0` 的控制台程序。`Program` 使用 .NET Generic Host 注册 `MiaoServerService`、TLS 证书服务、认证器、指标服务和内部 HTTP 管理服务。

## 目录

```text
source/MiaoNet.Server/
├── Program.cs
├── Data/              ServerState、ServerPlayer、ServerChannel、ServerMap
├── Server/            主服务、连接管线、认证、证书、选项和指标
├── Http/              HttpListener 管理 API
├── Utils/             服务端序列化辅助
└── docs/              服务端架构、作用域和 HTTP API 文档
```

## 分层

```text
MiaoHttpService (HTTP 管理)
        |
MiaoServerService (accept、握手、心跳、包处理、广播)
        |
ServerState / ServerChannel / ServerMap (玩家和作用域状态)
        |
MiaoClientConnection (每连接的收发、处理和请求响应)
        |
TlsTcpListener / TlsTcpConnection (TLS + TCP)
        |
MiaoNet.Shared (协议和序列化)
```

## 连接生命周期

```text
TCP accept
  -> TLS 握手 (TlsTcpPendingConnection)
  -> 版本检查
  -> MiaoNet 握手和 IMiaoAuthenticator 认证
  -> 创建 ServerPlayer / MiaoClientConnection
  -> 发送 PacketClientInitial
  -> 并行运行接收、处理、发送管线
  -> 任一管线结束时取消连接并移除玩家
  -> 广播 PacketPlayerLeft
```

`MiaoClientConnection` 使用独立的接收、处理和发送任务。接收任务从 TLS 流解析线格式包，处理任务调用 `PacketDispatcher`，发送任务从有界队列批量写出。请求响应使用 `RequestID`，每个连接最多保留 64 个待响应请求并通过配置的超时回调释放；心跳由 `MiaoServerService` 的 `PeriodicTimer` 定期发起，超时连接会被断开。

## 状态和广播作用域

`ServerState` 持有所有连接和频道，使用 `ImmutableDictionary` 配合 `ImmutableInterlocked` 更新。服务端始终保留 ID 为 `0` 的 `main` 频道；玩家通过 `PacketPlayerChannelMove` 提交频道名，服务端负责解析或创建目标频道，空频道会被移除。私有频道只向成员公开真实频道 ID，其他客户端将这些玩家归入本地虚拟私有频道。

每个 `ServerChannel` 持有直接玩家集合和按 `PlayerMapLocation` 索引的 `ServerMap`。`ServerPlayer` 只保存一个当前 `Channel`，以及 `PlayerLocation`、`PlayerState`、图形信息和全局标志。玩家进入/离开地图时，频道负责同步对应 `ServerMap`；地图为空时不创建 `ServerMap`。

广播目标由实现 `IPlayerScope` 的对象提供：

| 作用域 | 典型用途 |
|---|---|
| `ServerState` | 全服加入/离开、频道创建、全局聊天和系统公告 |
| `ServerChannel` | 频道聊天、同频道位置与频道移动通知 |
| `ServerMap` | 同地图帧同步、传送/互动和带状态的位置通知 |

地图状态由 `ServerMap.StateLock` 保护。切地图时服务端在写锁下读取同地图玩家的 `PlayerMovedInitialData`，再分别向同频道其他玩家发送仅位置的通知、向同地图玩家发送带初始状态的通知，避免同一客户端收到互相覆盖的两种状态。

## 包处理

`MiaoServerService.PacketHandling.cs` 注册并处理：

- 帧同步：`PacketPlayerFrame`；
- 位置与频道：`PacketPlayerLocationChanged`、`PacketPlayerChannelMove`；
- 聊天：公开、频道、地图、私聊；
- 表情、玩家音频、全局标志和生命状态；
- 传送、抓取/跳出和烟花。

普通玩家数据通过 `PacketPlayerNotification<T>` 或 `PacketContextualPlayerNotification<T>` 附加发送者 ID 后转发。服务端会校验地图状态、Follower 数量（最多 12）和聊天长度（最多 64 个字符），违规连接会被断开。

## 认证与证书

`IMiaoAuthenticator` 在握手阶段验证客户端数据：

- `UseCeleMiaoAuth=true`：使用 CeleMiao OAuth/BBS 认证；
- 默认：使用 `CustomAuthenticator`，适合开发和简单名字认证。

Debug 下注册 `LocalMiaoCertificateService` 并使用嵌入的 `localhost.pfx`；Release 下注册 `MiaoCertificateService`，从 `CertificatePath` 和 `CertificateKeyPath` 加载证书并监视文件更新。

## HTTP 管理服务

`MiaoHttpService` 使用 `HttpListener`，默认监听 `http://localhost:21474/`。当前端点为 `/status`、`/player`、`/announce`、`/gc` 和 `/metrics`，请求格式和状态码见 [Http/doc.md](../Http/doc.md)。该接口没有内置鉴权，应通过监听地址、防火墙或反向代理限制访问。

## 生产配置

主配置文件为 `appsettings.json`，可用环境变量覆盖（前缀 `MIAONET:`）。关键配置包括 `MiaoServer.Network.ListenEndpoint`、TLS 证书、认证凭据、握手/心跳超时和 `HttpListenerPrefix`。Release 启用 Server GC，并将日志写入 `logs/yyyy-MM-dd.log`。
