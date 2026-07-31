# MiaoNet 编码规范

> 🚧施工中...

## 基本风格
- 使用C#最新语言特性（LangVersion preview）
- 启用 Nullable Reference Types，合理使用 `?` 和 `!`
- 缩进使用4个空格
- 花括号换行（Allman风格）

## 命名

| 类型 | 风格 | 示例 |
|------|------|------|
| 类/结构体/接口 | PascalCase | `MiaoServerService` |
| 方法/属性 | PascalCase | `GetCertificate()` |
| 私有字段 | camelCase | `packetQueue` |
| 局部变量/参数 | camelCase | `areaKey` |
| 常量 | PascalCase | `ClientVersion` |
| 接口 | I前缀 | `IMiaoCertificateService` |
| 数据包类 | Packet前缀 | `PacketTeleportRequest` |

## 项目约定
- 数据包定义放在 `MiaoNet.Shared/Packet/Packets/` 下
- 请求/响应包成对出现，使用 `PacketRequest<TResponse>` / `PacketResponse` 基类
- 序列化实现 `IRefBinarySerializable<T>` 或 `IContextlessPacket<T>`
- 条件编译宏用于区分平台：`MIAO_CLIENT`、`MIAO_SERVER`、`MIAO_MOCKCLIENT`
- 受限于游戏引擎特性需要patch新方法时，先检查项目内有无相似实现

## 不推荐的做法
- 不要吞掉异常，请保留日志