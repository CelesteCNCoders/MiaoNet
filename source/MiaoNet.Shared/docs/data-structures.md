# MiaoNet.Shared 数据结构

## 概述

`MiaoNet.Shared` 中的 `Data/` 目录定义了客户端和服务端共享的游戏数据结构。所有结构都实现 `IRefBinarySerializable` 以支持二进制序列化。

## 玩家身份

### PlayerInfo

玩家的固定身份信息，登录时确定，连接期间不变。

```csharp
public sealed class PlayerInfo
{
    int AuthID        // 认证系统分配的 ID
    string Name       // 显示名
    string Prefix     // 前缀标签（如 "[Admin]"）
    string AvatarUrl  // 头像 URL
    Color Color       // 名字颜色
}
```

### PlayerGlobalFlags

玩家的全局状态标志，实时更新广播：

```csharp
[Flags]
public enum PlayerGlobalFlags : ushort
{
    None,
    Paused         = 1 << 0,  // 游戏暂停
    Typing         = 1 << 1,  // 正在输入聊天
    LiveMode       = 1 << 2,  // 直播模式（隐藏信息）
    Interactions   = 1 << 3,  // 启用玩家互动
    TakingGolden   = 1 << 4,  // 正在携带金草莓
    GroupPhotoMode = 1 << 5,  // 合照模式
    Watching       = 1 << 6   // 观战中
}
```

## 位置系统

### PlayerMap

```csharp
public readonly struct PlayerMap : IEquatable<PlayerMap>
{
    string Sid          // 地图 SID（如 "Celeste/LostLevels"）
    AreaMode AreaMode   // A-Side / B-Side / C-Side
}
```

值类型，正确实现了 `Equals` / `GetHashCode` / `==`。`IsEmpty` 表示不在任何地图中。

### PlayerLocation

```csharp
public readonly struct PlayerLocation
{
    PlayerMap Map    // 所在地图
    string Room      // 所在房间（关卡内的具体屏幕）
}
```

三种状态：
- `IsEmpty` — 不在地图中（Map 为空，Room 为空）
- `IsInDebugMap` — 在 debug 地图编辑器中（Map 非空，Room 为空字符串）
- `IsInMap` — 在正常地图中（Map 非空，Room 非空）

### ChangeResult

`PlayerLocation.CompareTo` 返回位置变化类型：

```csharp
enum ChangeResult { None, RoomOnly, All }
```

- `None` — 位置没变
- `RoomOnly` — 仅房间变化（同一张地图内转场）
- `All` — 地图变化（需要重建 Ghost）

## 玩家运行时状态

### PlayerState

游戏中的实时状态，每帧更新，切地图时作为 InitialState 发送：

```csharp
public sealed class PlayerState : ICloneable
{
    Vector2 Position           // 位置
    bool FacingLeft            // 朝向
    byte Dashes                // 当前冲刺数
    bool Dashing               // 是否正在冲刺（不序列化）
    float DeltaTime            // 帧时间
    PlayerSpriteMode PlayerSpriteMode  // 精灵模式
    bool Dead                  // 是否死亡
    HoldableInfo HoldableInfo  // 持有物信息
    FollowerInfo[] FollowerInfos // 跟随者列表
    Vector2 WindDirection      // 风向
    bool Interactions          // 互动开关
    bool Ducking               // 是否蹲下
    int HeldByPlayerID         // 被谁抓着
}
```

使用 `PooledStringManager` 序列化（动画名等字符串走 PooledString 优化）。

### PlayerGraphicsInfo

玩家的图形配置（头发颜色/长度），用于 Ghost 渲染：

```csharp
public sealed class PlayerGraphicsInfo : ICloneable
{
    HairInfo Dash0HairInfo     // 0 冲刺时头发
    HairInfo Dash1HairInfo     // 1 冲刺时头发
    HairInfo Dash2HairInfo     // 2+ 冲刺时头发
    HairInfo FeatherHairInfo   // 羽毛状态头发
}

public readonly struct HairInfo
{
    byte Length    // 头发段数
    Color Color   // 头发颜色
}
```

## 频道

### ChannelInfo

```csharp
public struct ChannelInfo
{
    string Name   // 频道名称
}
```

### ChatChannel

```csharp
public enum ChatChannel : byte
{
    Global,    // 全服
    Channel,   // 频道内
    Map        // 地图内
}
```

## 帧同步附属数据

### FollowerInfo

跟随者（草莓、钥匙等）的完整状态，仅在跟随者列表变化时发送：

```csharp
public readonly struct FollowerInfo
{
    FollowerType Type          // Strawberry / StrawberrySeed / Key / Custom
    string SpriteID            // Sprite 注册 ID
    string Animation           // 当前动画
    ushort AnimationFrame      // 当前帧
    Vector2S Offset            // 相对 Leader 的偏移
}
```

### FollowerInfoDelta

跟随者的增量更新（仅位置和动画），每帧发送：

```csharp
public readonly struct FollowerInfoDelta
{
    string Animation
    ushort AnimationFrame
    Vector2S Offset
}
```

### HoldableInfo

持有物信息：

```csharp
public struct HoldableInfo
{
    HoldableType Type          // None / Jelly / Theo / Player
    Vector2? Offset            // 相对持有者偏移（null 表示未变化）
    string? Animation          // 动画（仅 Jelly）
    ushort AnimationFrame
    Vector2 Scale
    float Rotation
}
```

## 传送相关

### PlayerSessionData

传送请求响应时携带的完整 Session 数据，用于在目标端重建游戏状态：

```csharp
public sealed class PlayerSessionData
{
    Vector2 Position
    Vector2 RespawnPoint
    PlayerInventory Inventory
    string[] StringFlags
    string[] LevelStringFlags
    StringIntPair[] Strawberries
    StringIntPair[] DoNotLoad
    StringIntPair[] Keys
    StringIntPair[] Counters
    string? StartCheckpoint
    string? ColorGrade
    int SummitGems
    SessionFlags Flags
    float LightingAlphaAdd
    float BloomBaseAdd
    float DarkRoomAlpha
    long Time
    CoreModes CoreMode
}
```

### PlayerMovedInitialData

切地图/切频道时，服务端发给客户端的同地图玩家初始数据：

```csharp
public readonly struct PlayerMovedInitialData
{
    int PlayerID
    PlayerState InitialState
    PlayerGraphicsInfo? GraphicsInfo
}
```

## 表情

### EmoteData

```csharp
public readonly struct EmoteData
{
    EmoteAtlasCategory Atlas   // Game / Gui / Portraits / ...
    string Path                // 精灵路径
}
```

### PlayerPlayedAudio

音频同步数据：

```csharp
public readonly struct PlayerPlayedAudio
{
    string Event       // FMOD 事件名
    string? Param      // 可选参数名
    float ParamValue   // 参数值
}
```

## 基础类型

### Vector2

```csharp
public readonly struct Vector2 { float X, Y }
```

### Vector2S

压缩的 Vector2（使用 short，精度 1 像素），用于 Follower 偏移等不需要高精度的场合：

```csharp
public readonly struct Vector2S { short X, Y }
```

### Color

```csharp
public readonly struct Color { byte R, G, B, A }
```

### AreaMode

```csharp
public enum AreaMode : byte { Normal, BSide, CSide }
```

### PlayerSpriteMode

```csharp
public enum PlayerSpriteMode { Madeline, MadelineNoBackpack, Badeline, ... }
```