# 命令系统

## 结构概览

```
source/MiaoNet.Client/Command/
├── MiaoNetCommand.cs               — 命令定义（Name, Aliases, Segments, OnExecute）
├── MiaoNetCommand.Commands.cs      — 所有命令的实现（静态方法）
├── MiaoNetCommand.Context.cs       — 命令执行上下文（发包、提示、Request）
├── CommandParser.cs                — 命令文本解析
└── CommandSegmentType.cs           — 参数类型枚举
```

## 解析流程

```
用户在聊天框输入 "/tp wheat"
    │
    ▼
ChatComponent 检测到 "/" 前缀
    │
    ▼
CommandParser.Parse("/tp wheat")
    │
    ├── 提取命令名 "tp"
    ├── 在 Commands 列表中匹配（Name 或 Aliases）
    ├── 按空格分割参数 ["wheat"]
    ├── 校验参数数量（与 Segments.Count 对比）
    └── 返回 ParseResult + matchedCommand + segments
    │
    ▼
matchedCommand.OnExecute(new Context(miaoNetContext, segments))
    │
    └── 返回 null 表示成功，返回 string 表示错误消息
```

## 命令定义

```csharp
public sealed partial class MiaoNetCommand
{
    public string Name { get; }                          // 主命令名
    public IReadOnlyList<string>? Aliases { get; }       // 别名列表
    public IReadOnlyList<CommandSegmentType> Segments { get; } // 参数类型列表
    public bool CaptureRestSegments { get; }             // 最后一个参数捕获剩余文本
    public ExecuteHandler OnExecute { get; }             // 执行委托
}
```

`CaptureRestSegments = true` 时，最后一个参数会包含该位置之后的所有文本（不再按空格分割），用于消息类命令。

## 参数类型 CommandSegmentType

| 类型 | 含义 | 自动补全范围 |
|------|------|-------------|
| `Text` | 任意文本 | 无 |
| `Emote` | 表情名 | 表情列表 |
| `Player` | 任意在线玩家 | 全服玩家 |
| `PlayerSameChannel` | 同频道玩家 | 当前频道玩家 |
| `PlayerSameMap` | 同地图玩家 | 当前地图玩家 |
| `Channel` | 频道名 | 已有频道 |
| `ChatChannelType` | 聊天频道类型 | Global/Channel/Map |
| `CommandName` | 命令名 | 命令列表 |

参数类型同时用于解析校验和 `ChatCompletionProvider` 的自动补全提示。

## 执行上下文 Context

```csharp
public readonly struct Context
{
    MiaoNetContext MiaoNetContext    // 完整上下文
    IReadOnlyList<string> Segments   // 解析后的参数列表

    void QueuePacket(packet)         // 发包
    void Request<T>(packet, callback) // Request-Response
    void TipMessage(message)         // 显示提示（本地聊天）
    void TipErrorMessage(message)    // 显示错误（本地聊天）
    void AddLocalChat(chatText)      // 添加本地聊天条目
}
```

命令在主线程执行，可安全访问 `ClientState` 和游戏引擎。

## 命令列表

| 命令 | 别名 | 参数 | 功能 |
|------|------|------|------|
| `/help` | `?`, `？`, `h` | — | 列出所有命令 |
| `/help-command` | `??`, `？？`, `hc` | `<命令名>` | 查看单个命令帮助 |
| `/say` | — | `<文本...>` | 按当前聊天频道发送消息 |
| `/emote` | `e` | `<表情...>` | 发送表情 |
| `/teleport` | `tp` | `<玩家>` | 传送到玩家（根据设置选择模式） |
| `/teleport-no-session` | `tpns` | `<玩家>` | 无存档传送 |
| `/teleport-with-session` | `tpws` | `<玩家>` | 带存档传送（请求对方 Session 数据） |
| `/random-teleport` | `rtp` | — | 随机传送到同频道的一个玩家 |
| `/back` | — | — | 返回传送前的位置（恢复存档） |
| `/whisper` | `w`, `msg` | `<玩家> <文本...>` | 私聊 |
| `/channel` | `join` | `<频道名...>` | 加入频道（不存在则创建） |
| `/locate` | `lc` | `<玩家>` | 查看玩家当前位置 |
| `/watch` | `wt` | `<玩家>` | 观战同地图玩家 |
| `/unwatch` | `uw`, `uwt` | — | 停止观战 |
| `/clear` | `cls` | — | 清空聊天记录 |
| `/group-photo-mode` | `gpm`, `hy` | — | 切换合照模式 |
| `/interactions` | `int` | — | 切换玩家互动 |
| `/chat` | `c` | `<频道类型>` | 切换默认聊天频道 |
| `/map-chat` | `mc` | `<文本...>` | 发送地图聊天 |
| `/channel-chat` | `cc` | `<文本...>` | 发送频道聊天 |
| `/global-chat` | `gc` | `<文本...>` | 发送全服聊天 |

## 传送系统

传送是最复杂的命令，有两种模式：

### NoSession（无存档传送）

直接创建目标地图的 Session 并加载，不携带当前游戏进度。

### WithSession（带存档传送）

1. 向目标玩家发送 `PacketTeleportRequest`
2. 目标玩家收到后回复 `PacketBeTeleportedResponse`（包含其 Session 数据）
3. 收到响应后用对方 Session 数据重建 Level

### 传送流程（两种模式共用）

```
保存当前存档 (if moveToDebugSave)
    │
    ▼
切换到 Debug Save（允许传送到任意地图）
    │
    ▼
播放 ScreenWipe 过渡动画
    │
    ▼
创建 Session → LevelLoader → 进入目标关卡
    │
    ▼
/back 可恢复原存档
```

## 本地化

命令帮助文本通过 Celeste 的 `Dialog` 系统本地化：

- 命令描述：`miaonet_commands_{name}_description`
- 参数名：`miaonet_commands_{name}_s{i}_name`
- 参数描述：`miaonet_commands_{name}_s{i}_description`

## 添加新命令

1. 在 `MiaoNetCommand.Commands.cs` 的 `Commands` 列表中添加 `new MiaoNetCommand(...)`
2. 实现对应的 `static string? CommandName(Context context)` 方法
3. 在 Dialog 文件中添加本地化 key
4. 参数类型选择合适的 `CommandSegmentType`（影响自动补全）
