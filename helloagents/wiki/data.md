# 数据模型

## 现有模型
ServerState 维护连接、频道与虚拟玩家；客户端 ClientState 维护在线状态。协议数据包含位置、帧增量、聊天和互动。详见 [共享数据结构](../../source/MiaoNet.Shared/docs/data-structures.md)。

## LevelClearRecord
| 字段 | 类型 | 约束与含义 |
|---|---|---|
| ClearID | string | 每次通关独立生成的非空 GUID，N 格式，共 32 位 |
| MapSID / MapName | string | 非空单行文本，分别最多 256 / 160 个 UTF-16 字符 |
| Mode | byte | 0/1/2 对应 A/B/C 面 |
| TimeTicks | long | 游戏 Session 用时，0 至 365 天 |
| Deaths | int | 本次 Session 死亡次数，0 至 1000 万 |
| Strawberries / TotalStrawberries | int | 普通草莓进度，0 ≤ 已收集 ≤ 总量 ≤ 10 万 |
| FullClear | bool | 从头开始且满足当前地图收集条件 |
| Golden | bool | 本次实际收集金草莓 |

MiaoNetModuleSaveData.LastClear 按存档保存最近一次通关，LastSharedClearID 保存最近已分享记录的编号。卡片持有冻结快照，后续换图不改变历史消息。0.7.1 删除 SL 模型字段；为兼容 0.7.0，线格式保留末尾布尔字节，发送固定 false、接收丢弃。
