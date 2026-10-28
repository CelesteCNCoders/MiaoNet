# 共享协议

## 模块概述
源码链接的二进制序列化与包定义，供客户端、服务端、MockClient 和 Inspector 使用；状态：开发中。

## 规范
包 ID 由 AssemblyInfo.cs 注册顺序定义；只追加新包。0.6.0 追加 PacketShareLevelClear 和 PacketLevelClear；0.x 主版本下不同次版本不能互连。
LevelClearRecord 是不可变 record，支持参数为空的构造及 init 属性，供二进制传输与 Everest YAML 存储使用。

0.7.0 为 LevelClearRecord 新增首字段 ClearID，不修改既有包 ID；与 0.6.x 不兼容。

0.7.1 移除 SL 字段，末尾字节保留为固定 false，接收时丢弃旧值，与 0.7.0 保持线格式兼容。

## 参考
[包系统](../../../source/MiaoNet.Shared/docs/packet-system.md)
