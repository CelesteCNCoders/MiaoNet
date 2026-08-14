# 客户端命令系统

聊天输入以 `/` 开头时由 `CommandParser` 解析；其他文本按当前 `ChatChannel` 发送。

## 结构和流程

```text
ChatComponent
  -> CommandParser.Parse
  -> 按 Name/Aliases 匹配 MiaoNetCommand
  -> 按 Segments 校验和拆分参数
  -> MiaoNetCommand.Context 执行 OnExecute
  -> 本地提示或 QueuePacket/Request
```

`MiaoNetCommand` 包含 `Name`、`Aliases`、`Segments`、`CaptureRestSegments` 和 `OnExecute`。当 `CaptureRestSegments=true` 时，最后一个参数保留剩余文本，供聊天、频道名等包含空格的值使用。

## 参数和补全

| `CommandSegmentType` | 候选范围 |
|---|---|
| `Text` | 不提供固定候选 |
| `Emote` | 表情列表 |
| `Player` | 全服玩家 |
| `PlayerSameChannel` | 当前频道玩家 |
| `PlayerSameMap` | 同频道且同地图玩家 |
| `Channel` | 已有频道 |
| `ChatChannelType` | `global`、`channel`、`map` |
| `CommandName` | 所有命令名 |

`ChatCompletionProvider` 同时处理命令/参数和 Emoji 补全，匹配不区分大小写并使用包含匹配。补全只影响输入体验，最终参数数量仍由 `CommandParser` 校验。

## 当前命令

| 命令 | 别名 | 参数 | 作用 |
|---|---|---|---|
| `/help` | `?`, `？`, `h` | 无 | 列出命令 |
| `/help-command` | `??`, `？？`, `hc` | 文本 | 显示单个命令帮助 |
| `/say` | 无 | 文本... | 按当前聊天范围发送 |
| `/emote` | `e` | 文本... | 发送解析后的表情或文字表情 |
| `/teleport` | `tp` | 同频道玩家 | 按设置选择传送模式 |
| `/teleport-no-session` | `tpns` | 同频道玩家 | 不携带 Session 传送 |
| `/teleport-with-session` | `tpws` | 同频道玩家 | 请求目标 Session 后传送 |
| `/random-teleport` | `rtp` | 无 | 随机传送到同频道玩家 |
| `/back` | 无 | 无 | 返回传送前的存档/位置 |
| `/whisper` | `w`, `msg` | 玩家 + 文本... | 私聊 |
| `/channel` | `join` | 频道... | 加入现有频道或创建频道 |
| `/locate` | `lc` | 同频道玩家 | 显示玩家位置 |
| `/watch` | `wt` | 同地图玩家 | 开始观战 |
| `/unwatch` | `uw`, `uwt` | 无 | 停止观战 |
| `/clear` | `cls` | 无 | 清空全部聊天记录和标签页记录 |
| `/group-photo-mode` | `gpm`, `hy` | 无 | 切换合影模式 |
| `/interactions` | `int` | 无 | 切换玩家互动 |
| `/chat` | `c` | 聊天范围 | 修改默认聊天范围 |
| `/map-chat` | `mc` | 文本... | 发送地图聊天 |
| `/channel-chat` | `cc` | 文本... | 发送频道聊天 |
| `/global-chat` | `gc` | 文本... | 发送全服聊天 |

聊天打开时，`LeftShift+Tab` 在 `ALL`、`Global`、`Channel`、`Map` 标签间循环，并在进入频道标签时同步默认发送范围。普通 `Tab` 仍由输入框用于补全。

## 执行上下文

`MiaoNetCommand.Context` 向命令提供当前 `MiaoNetContext` 和已解析参数，并封装 `QueuePacket`、泛型 `Request`、本地普通/错误提示及聊天条目插入。命令在游戏主线程执行，可以访问 `ClientState` 和 Celeste 场景。

## 传送

无 Session 模式用目标地图与房间新建 `Session`；带 Session 模式发送 `PacketTeleportRequest`，服务端转发 `PacketBeTeleportedRequest` 给目标玩家，响应成功后用返回的 `PlayerSessionData` 创建关卡。启用临时存档时，客户端先保存当前存档并进入 Debug Save，`/back` 可恢复传送前状态。

## 添加命令

1. 在 `MiaoNetCommand.Commands.cs` 的 `Commands` 列表添加定义。
2. 实现静态执行方法并选择准确的 `CommandSegmentType`。
3. 在英文和简体中文 Dialog 文件添加描述及参数 key。
4. 为解析、参数数量和关键执行分支添加测试。
