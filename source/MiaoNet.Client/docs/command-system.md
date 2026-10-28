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
| `/shareclear` | `sc` | 无 | 将当前存档最近一次通关以特殊卡片分享到全局频道 |
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

## 通关分享

完成关卡后输入 `/shareclear`（或 `/sc`）。分享目标固定为全局，与当前标签和默认聊天范围无关。每条通关记录只能成功分享一次，新通关后才可再次分享；不同记录间仍需间隔 10 秒。收到服务端回显才持久化已分享编号，发送失败不消耗机会。没有记录、已分享、未连接或开启直播模式时会提示原因。

卡片直接由聊天系统绘制，不生成图片。包含玩家名、地图名与 A/B/C 面、通关用时和死亡次数。底部红色草莓印章显示 `2/20` 这样的收集数（零收集不显示），获得金草莓显示金色贴图和“金草莓”。没有对应状态就不显示印章；全部没有时收起底部区域。全收集判定仍保存在记录中。

在 **MiaoNet → 界面 → 通关分享显示** 中选择标准卡片、紧凑卡片或屏蔽。默认标准，紧凑模式压缩为两行信息加可选印章行；屏蔽仅隐藏通关卡片，不影响普通聊天、分享确认或记录采集。切换立即影响现有聊天历史，设置随 Everest Mod Settings 保存。

记录在通关结算后冻结，切图不会覆盖，随当前存档的 Everest Mod SaveData 正常保存；收到自身分享回显后会立即保存已分享状态。

全收集要求从头开始并收齐地图普通草莓，以及地图实际存在的磁带和爱心；原版山顶 A 面还要求全部宝石。月莓不计入普通草莓数量，金草莓按本次 Session 已收集的地图实体判断。观战时不采集通关记录。

0.7.1 起不再检测、保存或显示 SL 状态。SpeedrunTool 原有的联机读档同步兼容仍然保留。

这是客户端自报成绩，服务端只验证数据格式，不是排行榜认证。0.7.0 新增 ClearID 协议字段，客户端和服务端需一起升级。服务端去重缓存保留至本次服务进程退出；正常客户端的已分享状态随本地存档持久化。

## 添加命令步骤

1. 在 `MiaoNetCommand.Commands.cs` 的 `Commands` 列表添加定义。
2. 实现静态执行方法并选择准确的 `CommandSegmentType`。
3. 在英文和简体中文 Dialog 文件添加描述及参数 key。
4. 为解析、参数数量和关键执行分支添加测试。
