# API 手册

## 游戏命令
- `/shareclear`，别名 `/sc`，无参数：分享当前存档最近一次通关到全局频道。
- 所有原有命令见 [命令系统](../../source/MiaoNet.Client/docs/command-system.md)。

## 通关消息
- C→S：`PacketShareLevelClear(Record)`。
- S→C：`PacketLevelClear(PlayerID, DateTime, Record)`。
- 服务端从连接中确定发送者，广播使用 ServerState 全局作用域。
- 非法数据拒绝并断开；连续分享按每连接 10 秒节流。
- 同一账号的相同 ClearID 仅广播一次，重复请求仅补发给请求者作确认；正常客户端收到回显后持久化并禁止重复指令。

## 其他接口
共享协议见 [包系统](../../source/MiaoNet.Shared/docs/packet-system.md)。HTTP 服务含 `/status`、`/metrics` 和管理后台，详见 [服务端架构](../../source/MiaoNet.Server/docs/server-arch.md)；通关分享不修改 HTTP 认证和权限。
