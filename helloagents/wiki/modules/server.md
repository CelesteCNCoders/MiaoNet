# 服务端

## 模块概述
net10.0 Generic Host，维护认证、TLS 连接、玩家与频道、广播和 HTTP 管理；状态：开发中。

## 通关分享规范
`MiaoServerService.LevelClear` 验证 LevelClearRecord，使用连接 ID 和 UTC 服务器时间生成通知，广播到 ServerState 的全部玩家（包括自己和其他频道）。每连接的 LevelClearShareLimiter 使用单调时钟，拒绝 10 秒内再次发送。
只验证数据形状，不证明玩家实际完成关卡。不调整普通聊天长度限制，不额外发给 Minecraft 桥接。

0.7.0 按认证账号与 ClearID 缓存已分享记录，重复请求不再广播。重连后使用新连接 ID 向请求者补发原记录，作为成功确认；缓存仅存于当前服务进程。普通客户端同时持久化本地分享状态。

## 参考
[服务端架构](../../../source/MiaoNet.Server/docs/server-arch.md)
