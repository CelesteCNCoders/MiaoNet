# MiaoNet Server 架构概览

## 项目结构

```
source/MiaoNet.Server/
├── Program.cs                          — 入口，DI 注册，Host 构建
├── AppEvents.cs                        — EventId 常量（日志分类）
├── Data/
│   ├── ServerState.cs                  — 全局状态容器（玩家列表、频道列表）
│   ├── ServerPlayer.cs                 — 玩家游戏状态
│   ├── ServerChannel.cs                — 频道及其成员管理
│   ├── ServerMapUnit.cs                — 地图单元（同地图玩家集合）
│   └── HandshakeResult.cs              — 握手结果
├── Server/
│   ├── MiaoServerService.cs            — 主服务（BackgroundService），监听连接、心跳
│   ├── MiaoServerService.Handshake.cs  — 版本校验 + 握手流程
│   ├── MiaoServerService.PacketHandling.cs — 各类包 handler
│   ├── MiaoServerService.Interface.cs  — IMiaoServerService 实现
│   ├── IMiaoServerService.cs           — 对外接口（供 Http 模块调用）
│   ├── MiaoClientConnection.cs         — 单个客户端连接（收/发/处理管线）
│   ├── MiaoClientConnectionFactory.cs  — 连接工厂委托
│   ├── Connection/
│   │   ├── INetworkListener.cs         — 监听器抽象
│   │   ├── INetworkConnection.cs       — 连接抽象（Stream + Shutdown）
│   │   ├── IPendingNetworkConnection.cs— 待完成连接（TLS 握手前）
│   │   ├── TlsTcpListener.cs           — TLS/TCP 监听器实现
│   │   ├── TlsTcpConnection.cs         — TLS/TCP 连接实现
│   │   └── TlsTcpPendingConnection.cs  — TLS 握手中间状态
│   ├── Authentication/
│   │   ├── IMiaoAuthenticator.cs       — 认证器接口
│   │   ├── CeleMiaoAuthenticator.cs    — 正式认证（OAuth2）
│   │   ├── CustomAuthenticator.cs      — 自定义认证
│   │   └── TestAuthenticator.cs        — 测试用直接通过
│   ├── Certificate/
│   │   ├── IMiaoCertificateService.cs  — TLS 证书服务接口
│   │   ├── MiaoCertificateService.cs   — 正式证书（自动续期）
│   │   └── LocalMiaoCertificateService.cs — 本地开发证书
│   ├── Options/                        — 配置类（appsettings 映射）
│   │   ├── MiaoServerOptions.cs
│   │   ├── NetworkOptions.cs
│   │   ├── AuthenticationOptions.cs
│   │   └── CertificateOptions.cs
│   ├── Utils/
│   │   ├── ReaderWriterLockSlimExtensions.cs — using 模式锁扩展
│   │   └── TokenBucket.cs              — 令牌桶限流（烟花等）
│   └── MiaoMetrics*.cs                 — 指标收集
├── Http/
│   ├── MiaoHttpService.cs              — HTTP 管理接口（BackgroundService）
│   └── MiaoHttpService.EndPoints.cs    — /status, /player, /announce, /gc, /metrics
├── Utils/
│   └── RefBinarySerialization.cs       — 二进制序列化辅助
└── ScopeTree/                          — Scope Tree 实现（见 scope-tree.md）
```

## 分层设计

```
┌─────────────────────────────────────────────────┐
│  HTTP 管理层 (MiaoHttpService)                   │  /status /player /announce
├─────────────────────────────────────────────────┤
│  游戏逻辑层 (MiaoServerService.PacketHandling)   │  各类包 handler
├─────────────────────────────────────────────────┤
│  状态管理层 (ServerState / ScopeTree)            │  玩家、频道、地图状态
├─────────────────────────────────────────────────┤
│  连接层 (MiaoClientConnection)                   │  收包/发包/request-response
├─────────────────────────────────────────────────┤
│  网络层 (TlsTcpListener / TlsTcpConnection)      │  TLS + TCP
└─────────────────────────────────────────────────┘
```

## 连接生命周期

```
TCP Accept
    │
    ▼
TLS Handshake (TlsTcpPendingConnection.CompleteAsync)
    │
    ▼
Version Check (客户端发送版本号，服务端比对)
    │
    ▼
MiaoNet Handshake (客户端发送认证数据，服务端调用 IMiaoAuthenticator)
    │
    ▼
创建 ServerPlayer + MiaoClientConnection
    │
    ▼
发送 PacketClientInitial (在线玩家列表、频道列表)
    │
    ▼
HandleClientConnectAsync — 三个并发 Task:
    ├── HandleClientReceivingAsync  (从 Stream 读入 Pipe)
    ├── HandleClientProcessingAsync (从 Pipe 解析包 → PacketDispatcher)
    └── HandleClientSendingAsync    (从 sendChannel 批量写出)
    │
    ▼
任一 Task 结束 → CancellationToken 取消 → 连接关闭
    │
    ▼
RemovePlayer → 广播 PacketPlayerLeft
```

## 包系统

### 序列化格式

```
┌────────┬────────┬──────────────────┐
│ size   │ id     │ payload          │
│ u16 LE │ u16 LE │ size bytes       │
└────────┴────────┴──────────────────┘
```

### 包分类

- `IContextualPacket` — 需要序列化上下文（PooledString 等）的包
- `IContextlessPacket` — 无需上下文的包（继承 IContextualPacket）
- `PacketRequest<TResponse>` — 请求包（带 RequestID）
- `PacketResponse` — 响应包

### 注册与分发

- `PacketRegistry`（静态类，Shared 层）— 包 ID ↔ 类型映射，序列化/反序列化方法
- `PacketDispatcher`（Server 层）— 根据包类型分发到注册的 handler

### Request-Response 模型

`MiaoClientConnection` 维护 `pendingRequests` 字典（RequestID → 回调）。`RequestAsync` 发送请求包并注册回调，对端回复后 `OnResponse` 触发回调。用于心跳（Ping/Pong）、传送请求等。

## 主服务 MiaoServerService

`BackgroundService`，负责：

1. **监听连接** — `ExecuteAsync` 循环 accept，每个连接 fire-and-forget `HandlePendingConnectionAsync`
2. **心跳管理** — `HandleConnectionsHeartbeats` 定时 Ping 所有连接，超时断开
3. **包处理** — 通过 `PacketDispatcher` 将包分发到 `HandlePacketAsync` 重载
4. **广播** — 提供 `BroadcastAsync`、`BroadcastOthersAsync`、`BroadcastContextuallyToAsync` 等方法

### 广播方法

| 方法 | 目标 |
|------|------|
| `BroadcastAsync` | 所有连接 |
| `BroadcastOthersAsync` | 排除自己 |
| `BroadcastContextuallyOthersAsync` | 排除自己（上下文包） |
| `BroadcastContextuallyToAsync` | 指定连接集合 + predicate |
| `BroadcastToOthersAsync` | predicate 过滤 + 排除自己 |

## 状态管理

### ServerState

全局状态容器，持有：
- `players: ImmutableDictionary<int, MiaoClientConnection>` — 在线玩家
- `channels: ImmutableDictionary<int, ServerChannel>` — 活跃频道

所有集合通过 `ImmutableInterlocked.Update` 进行无锁 CAS 更新。

### ScopeTree（重构后）

见 [scope-tree.md](scope-tree.md)。替代原有分散的玩家位置追踪，提供统一的移动原语和广播目标查询。

## HTTP 管理接口

`MiaoHttpService` 是独立的 `BackgroundService`，使用 `HttpListener` 提供管理 API：

| 端点 | 用途 |
|------|------|
| `GET /status` | 返回在线玩家数、频道列表及各频道玩家 |
| `DELETE /player?cid=&reason=` | 按连接 ID 踢人 |
| `DELETE /player?aid=&reason=` | 按认证 ID 踢人 |
| `GET /announce?msg=` | 全服广播系统消息 |
| `POST /gc` | 触发 Full GC |
| `GET /metrics` | 返回流量指标和 GC 信息 |

## 认证

`IMiaoAuthenticator.AuthenticateAsync(authData, isAuthorize, token)` 返回 `AuthenticationResult`。

编译开关选择实现：
- `USE_CELEMIAO_AUTH` → `CeleMiaoAuthenticator`（OAuth2 / BBS 认证）
- 否则 → `CustomAuthenticator`（自定义或测试认证）

认证在握手阶段完成，认证失败直接关闭连接。

## 并发模型

- **连接级**：每个 `MiaoClientConnection` 有独立的 receive/process/send 管线，互不阻塞
- **发包**：`Channel<IContextualPacket>` 无界单读，write 端从任意线程投递，read 端批量写出
- **状态更新**：`ImmutableInterlocked` CAS 更新全局集合，无需全局锁
- **地图操作**（ScopeTree 后）：`MapScope` 内置 `Channel<Func<Task>>` 工作队列，保证同地图操作顺序执行
