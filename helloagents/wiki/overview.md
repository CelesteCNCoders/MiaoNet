# MiaoNet

## 项目概述
面向 Celeste 大量玩家联机的 Everest 客户端与独立服务端；目前仍为开发中项目。

## 模块索引
| 模块 | 职责 | 文档 |
|---|---|---|
| Client | 游戏事件、聊天、命令、Ghost 和 UI | [客户端](modules/client.md) |
| Server | 认证、连接、状态、广播、HTTP 管理 | [服务端](modules/server.md) |
| Shared | 二进制协议与数据结构 | [共享协议](modules/shared.md) |
| ClientShared | 共用 TLS/TCP 连接 | [连接层](modules/client-shared.md) |
| ChatInputBox | 输入、补全、标签与消息列表 | [聊天组件](modules/chat-input.md) |
| MockClient / Inspector | 模拟连接与数据包检查 | [工具](modules/tools.md) |
| UnitTest | MSTest 回归验证 | [测试](modules/tests.md) |

## 快速链接
- [技术约定](../project.md)
- [架构](arch.md)
- [API](api.md)
- [数据](data.md)
- [历史](../history/index.md)
