# 共用客户端连接

## 模块概述
`source/MiaoNet.ClientShared` 提供 TLS/TCP 连接、版本握手、包收发及连接生命周期协调；由游戏客户端和 MockClient 以源码链接共享。状态：开发中。

## 规范
通关分享复用现有队列与握手，不引入额外连接。协议版本来自 Shared.Connection.Version。
