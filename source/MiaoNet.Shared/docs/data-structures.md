# MiaoNet.Shared 数据结构

共享数据位于 `source/MiaoNet.Shared/Data/`，用于描述玩家身份、位置、频道、实时状态和传送 Session。可序列化类型实现 `IRefBinarySerializable<T>` 或带 `PooledStringManager` 的 contextual 接口。

## 玩家身份与在场状态

`PlayerInfo` 在握手时确定，包含 `AuthID`、显示名、前缀、头像 URL 和颜色。`PlayerGlobalFlags` 是实时广播的位标志：`Paused`、`Typing`、`LiveMode`、`Interactions`、`TakingGolden`、`GroupPhotoMode` 和 `Watching`。

`PlayerPresenceData` 将 `PlayerLocation` 与 `PlayerGlobalFlags` 打包；`PlayerPresenceDataWithID` 额外带玩家 ID，供频道切换和快照使用。

## 位置

```csharp
public readonly struct PlayerMapLocation
{
    string Sid;          // 地图 SID
    AreaMode AreaMode;   // Normal / BSide / CSide
}

public readonly struct PlayerLocation
{
    PlayerMapLocation Map;
    string Room;
}
```

`PlayerLocation.Empty` 表示不在地图中；地图非空而 `Room` 为空表示 Debug Map；地图和房间都存在表示正常关卡。`GetChangeResult` 返回 `None`、`Incremental` 或 `FullSync`，客户端据此决定仅更新位置还是重建 Ghost。

## 实时状态

`PlayerState` 是可克隆的完整状态，包含位置、动画/帧、缩放、`PlayerStateFlags`、冲刺数、帧时间、精灵模式、Follower、风向、持有物和可选的最终 Camera 世界坐标。`PlayerStateDelta` 是每帧使用的增量，`FrameFlags` 控制是否携带冲刺、持有物、Follower 初始/增量、风向和 Camera；Camera 只在 Player 存在 Watcher 时附加到既有帧包，不增加独立高频包。服务端在 `ServerMap.StateLock` 下将增量应用到玩家状态，并拒绝非有限 Camera 坐标。

附属结构包括：

- `FollowerInfo` / `FollowerInfoDelta`：Follower 类型、Sprite、动画、帧和偏移；
- `HoldableInfo`：持有物类型、偏移、动画、缩放和旋转；
- `PlayerGraphicsInfo` / `HairInfo`：不同冲刺状态下的头发配置；
- `PlayerPlayedAudio`：要同步播放的音效和参数。

## 频道和聊天

`ChannelInfo` 仅保存频道名称；`ChatChannel` 有 `Global`、`Channel`、`Map` 三种发送范围。服务端将 `PacketSendChatMessage` 转换成带时间、类型、发送者 ID 和内容的 `PacketChatMessage`。私聊使用单独的请求/响应包。

## 传送

`PlayerSessionData` 保存创建 Celeste `Session` 所需的地图进度、背包、草莓、旗标、核心模式、时间、复活点和颜色设置。`PacketTeleportResponse` 可携带此数据；客户端的无 Session 模式只使用目标位置，带 Session 模式会重建目标玩家的 Session。

## 初始状态包装

`PlayerMovedInitialData` 包装完整 `PlayerState`；`PlayerMovedInitialDataWithID` 再附加玩家 ID。`PacketClientInitial`、频道切换响应和位置切换响应使用这些结构发送同地图玩家的初始状态，避免新建 Ghost 时等待下一帧。

## 观战场景状态

`WatchSceneSnapshot` 保存快照位置、序号、Session string flags、当前房间已激活 Touch Switch 的 Entity ID，以及已注册原版实体适配器提供的完整状态。`WatchEntityKey` 以实体类型、地图 `EntityData.ID` 和可选子实体编号作为稳定标识；状态和瞬时事件的紧凑 payload 由对应客户端适配器解释，服务端只负责通用边界校验与转发。

`WatchSceneDelta` 保存产生变化时的位置、连续序号、flags 增删，并用 `HasTouchSwitchState` 区分“未更新此类状态”和“用空集合替换状态”。实体持续状态使用 `None`、`Patch`、`Replace` 三种模式；短暂动画、音效等按产生顺序放入 `WatchEntityEvent`。Touch Switch 与 `Replace` 实体集合均表示当前房间的完整状态，因此切房和重复进入房间可以丢弃旧缓存。`RoomTransition` 保存 Player 实际触发原版转场时的 source、target、出口位置与方向；`IsDeathRespawn` 标记普通死亡或 Retry 后的轻量完整状态。独立的 `DeathWipe` live-state 在 Player 实际调用原版死亡 WipeOut 前发出，Watcher 同时开始缩圈，将重生状态缓存到完全黑屏帧再原子应用；快照尚未到齐时保持全黑，应用完成后直接显示新状态，不额外播放 WipeIn。普通死亡保留当前 Level；Touch Switch 和死亡前已经移除的 Temple Cracked Block 会在黑屏帧按地图 ID 定向重建，只有其他原版单向实体明确报告无法原地逆转时，才执行一次房间重建并重新应用完整快照。`RequiresRoomReload` 仍只表示生产端 F5、读档或其他显式完整生命周期；该增量同样必须携带完整状态。死亡后直接进入其他房间的特殊生命周期（例如 PlayerSeeker 结尾）会把目标房间完整 `Replace` 标记为 `IsDeathRespawn`，保留死亡上下文并在目标房间就绪后无额外复活动画地恢复 Ghost。

`PersistentSession` 实体状态集中保存当前房间的 `DoNotLoad`、已收集草莓 ID、Cassette、HeartGem、第九章 `fake_heart`、Summit Gems、检查点命中状态和复活点，并保存草莓、Cassette 与 HeartGem 的幽灵外观。它覆盖普通、幽灵、梦境与第九章假结局 Crystal Heart，草莓、Golden Berry、Key、Lock Block、Summit Gem，以及永久 Dash Block、Fake Wall、Temple Cracked Block 和 Crumble Wall 的最终存在状态。草莓籽不再写入虚假的父草莓 `DoNotLoad`，而由独立状态同步 ghost 精灵、待机、跟随、返回、合并和最终清除；草莓、种子和 Key 正在跟随玩家时仍使用原有 Ghost Follower 表现，Sprite ID 改变时会重建对应跟随物，不会在观看端给隐藏的本地 Player 创建重复 follower。普通“消失”变化直接移除本地实体；只有状态回退要求实体重新出现，或房间内的条件实体需要重新求值时才重载房间。`Checkpoint` 与 `SummitCheckpoint` 另用按 Entity ID 标识的布尔状态同步亮起结果。

`WingedStrawberry` 使用地图 `EntityData.ID` 同步 `Present`、`FlyingAway` 和 `Absent` 三态。冲刺时观看端复用原版飞走逻辑；实体飞出房间后仍保留 `Absent` 状态，直到被观看玩家的房间生命周期重新创建该草莓。正在作为 Player Follower 的草莓由 `PersistentSession` 与原有 Ghost Follower 处理。

短周期互动实体分别同步 Spring 的启用与弹跳事件，Refill 的可用生命周期，Fly Feather 的收集、护盾碰撞与重生事件，可重复 `FakeHeart` 的碰撞、击碎与重生事件，以及 Booster 的进入、随玩家冲出、破裂和重生阶段；Bumper 同步冷热模式、冷却与碰撞方向，Cloud 同步运动阶段。机关适配同步 Dash Switch 和 Temple Gate 的按压/开门进度、Crumble Platform 的起始震动、砖块出入事件与最终可见状态、Level Core Mode，以及 Heart Gem Door 的计数、可见性和完整开门渲染进度。

`MovingSolid` 状态族使用原版 `EntityData.ID` 标识 Zip Mover、Swap/Switch Block、Move Block、Falling/Final Boss Falling Block、Crush Block、Sinking Platform、Floaty Space Block、Dream Block、Golden Block、Glass Block 和 Star Jump Block。每项状态携带实体类型、可见性、碰撞状态、绝对位置、原版阶段和至多三个实体专用进度值。Floaty Space Block 在 Watcher 端复用原版组长的 `Moves` 与 `Jumpthrus` 关系，使整组连接的 JumpThrough 按权威位移一同移动。Dream Block 只同步 Dream Dash 模式、one-use 状态和激活期白色填充；粒子帧与边框 wobble 相位继续由 Watcher 的原版 `DreamBlock.Update` 本地推进，不发送或逐帧回写 `animTimer`、`wobbleEase`，避免本地视觉时钟与网络样本互相争用。Bounce Block 使用独立状态保存冷热模式、阶段、绝对位置和原版计时器，并以单个破坏事件携带实际碎片方向，避免观看端自行触发或重复播放破坏动画。Watcher 在每次本地场景更新后重新收敛到最近的远端绝对状态，避免本地隐藏 Player 缺少触发条件时实体自行回退。连续变化的坐标或进度只在对应实体实际运动期间产生补丁；停止观战后仍通过房间重载恢复本地场景。

`PeriodicPlatform` 统一同步普通 Moving Platform、旋转平台生成的运行时子平台、Slider、Track Spinner 和 Rotate Spinner。地图实体沿用 `EntityData.ID`，旋转子平台使用由初始几何信息计算的稳定正数 ID。Slider、Track Spinner 和 Rotate Spinner 在本地运行原版确定性运动，并每 0.1 秒取得一次权威位置、速度/进度和方向锚点；Slider 额外同步当前四向运动方向，避免转角后仅靠表面法线无法恢复路径。Watcher 对小误差按比例软纠偏，仅在方向/模式改变、误差过大、首次快照或死亡黑帧时直接重锚，避免逐包硬跳和长期相位漂移。死亡和 Retry 的 `IsDeathRespawn` 生命周期会在全黑帧强制应用最新锚点，即使 payload 与旧缓存相同也不会跳过。普通完整 `Replace` 不会因为 Clutter 等无关实体变化强制重置已存在的 Spinner。Player 暂停时 Watcher 同步冻结这些本地运动。依赖 Player Rider 下沉的 Moving Platform 暂时继续使用权威绝对状态。`CassetteBlock` 以子编号区分全房间 Manager 节拍与单个 Cassette Block；方块状态包含绝对位置与碰撞高度，Watcher 不推进本地 Manager 节拍，避免预切换尺寸变化累积。`SwitchGate` 保存绝对位置、碰撞、Wiggler 和图标帧。`ClutterSystem` 以子编号区分 Color Switch、Cabinet、Clutter Door、三色 Clutter 组存在状态和当前直接接触根块；接触根块使用按生成几何排序的稳定 ID，并在首次出现后保留 Active/Inactive 墓碑状态，因此结束接触只产生局部 Patch，不会因临时键消失升级为全实体 `Replace`。整组清理事件触发原版吸收碎片、同色基底停用与柜门收束续程，Active 根块在 Watcher 端调用原版 `WeightDown()`，其递归与轻量浮动由本地演算。`DoorMechanism` 覆盖 Door、Trapdoor 与 MrOshiroDoor；持续状态负责最终收敛，有序事件负责原版开启方向及动画。

危险实体不使用隐藏的本地 Player 作为触发源。`StaticSpinner` 仅记录本房间已经销毁的 Crystal Static Spinner ID，并以稀疏集合和破坏事件同步；Watcher 播放原版同等的音效与碎片后隐藏实体但不将其移出场景，因此死亡快照可以原地恢复，而不会为 36,938 个原版静态 Spinner 建立逐实体常驻快照。Dust Static Spinner 只禁用 Watcher 碰撞。`TriggerSpikes` 使用父实体 `EntityData.ID` 与尖刺数组下标同步每段的触发、伸出和回收计时，并以事件重放首次触发音效。`FireBall` 使用地图父实体 ID 与生成序号标识同一路径上的火球；Watcher 运行原版确定性运动，并每 0.1 秒接收绝对位置、路径进度和速度锚点，小误差软纠偏，模式变化、较大误差及死亡快照直接重锚。冰球击碎仍以独立事件重放音效、动画和粒子。`Lava` 以子编号 `0/1` 区分每房间唯一的 Rising Lava 与 Sandwich Lava，直接同步父实体位置、上下 LavaRect 位置、冷热视觉参数、模式和阶段；Watcher 不运行这些依赖本地 Player 的危险实体更新，但会按原版 `4/s` 在本地推进纯视觉冷热插值，并让同一模式内的网络锚点保持单向收敛，避免冷模式被较旧的热色进度覆盖。颜色、波形、淡出范围或位置变化时会使 `LavaRect` 的缓存顶点失效，确保完全冻结的 cold Lava 也能重建为正确的冷色填充。

`Snowball` 和 `Puffer` 使用 Player 权威状态与约 0.1 秒绝对锚点；阶段、动画或存在性变化立即发送。Puffer 沿用地图 `EntityData.ID`，在离散阶段不变时按实际到达间隔在 Watcher 本地插值，不因高速移动超过固定距离阈值而逐锚点硬跳；动画帧只在首次状态、阶段/动画切换或生命周期重置时校正。Snowball 使用生成它的 `WindAttackTrigger` ID，Watcher 只运行原版水平速度和正弦视觉运动，重置坐标完全来自 Player；破碎阶段由 `Destroy` 到成功 `ResetPosition` 的显式生命周期锁存，不从一次性 `break` 动画结束后被清空的动画 ID 推断。Snowball 破碎和 Puffer 爆炸使用纯表现事件；死亡遮罩期间仍会先把已经到达的破碎等事件应用到旧生命周期，再在全黑帧以重生 `Replace` 原子切换实体状态，避免致死 Snowball 的破碎事件被重生快照覆盖。Puffer 的观看端爆炸只播放 Sprite、音效、震动、位移和粒子，Theo、Touch Switch 与可破坏物的结果仍由各自状态适配器同步，不在 Watcher 重复执行。

`BadelineOldsite` 不再发送周期坐标锚点，也不读取 Watcher 隐藏的本地 Player。存在、开始/停止追逐、悬浮、死亡笑脸及动画覆盖等离散生命周期变化携带一次位置和原版追随延迟；连续运动直接复用已有的逐帧 PlayerFrame，在 Watcher 保存至多四秒的远端位置、动画与朝向历史，并按原版 `1.55s + 0.4s * index` 延迟取样和 `500/s` 追近速度本地演算。收到 `Visible false -> true` 的实时生命周期边沿时，Watcher 还会用同一份 PlayerFrame 历史重建原版出生目标，在本地同时推进 `0.5s` 的缩放/颜色/头发淡入和 `followBehindTime - 0.1s` 的入场位置 Tween；完整快照和生命周期重置不会错误重播出生表现。初次观看及死亡重生的完整快照会额外携带分片的一次性 Player ChaserState 历史种子，避免 Watcher 等待追随延迟窗口填满时让 Badeline 暂停；每片保持在通用 1 KiB 实体 payload 上限内，普通增量不重复发送。Watcher 只更新 Badeline 的 Sprite、Hair、Trail 与遮光表现，不运行原版追逐协程、碰撞或本地 Player 相关 AI。该方案没有新增高频实体包或 PlayerFrame 字段。

`AngryOshiro` 作为每房间唯一的运行时实体使用固定实体键。Player 权威发送 Waiting、Chase、ChargeUp、Attack、Hurt、Dummy 等离散阶段，并约每 `0.1s` 发送位置、速度和视觉锚点；阶段与可见性变化立即发送。Watcher 保留原版 `oshiro_boss` Sprite、组件与 `Depth`，在本地使用远端 PlayerFrame 位置、当前远端 Camera 和原版速度公式推进 Chase、ChargeUp、Attack、Hurt，再以网络锚点软纠偏，阶段切换、死亡生命周期或大偏差直接重锚。Watcher 不运行 AngryOshiro 的 StateMachine、PlayerCollider、转场回调、`Engine.TimeRate` 或 Distort 控制，因此隐藏的本地 Player 不会触发重复击杀、踩踏或全局慢速，且只存在一套原版渲染主体。

`SeekerSystem` 将地图 Seeker 与由 `SeekerStatue` 孵化出的同一只 Seeker 视为共享 `EntityData.ID` 的单一生命周期。Player 权威同步 Statue、Hatching、Seeker 三种形态，以及 Seeker 的八个原版 StateMachine 阶段、动画、朝向、速度、光照和攻击参数；Watcher 在形态切换时替换原版实体主体，禁用本地寻路、Player/Holdable 碰撞与 AI StateMachine，仅推进 Sprite、Shaker、光照和拖影，并在约 `0.1s` 的绝对锚点之间按速度本地演算。攻击、撞墙、踩踏和重生使用有序表现事件。`SeekerBarrier` 保持地图 ID 和原版纯视觉 Update，始终不参与 Watcher 碰撞；反射闪光作为事件传播，最终 Flash、Solidify 状态由低频状态收敛。

`PlayerSeeker` 同样保留原版 Seeker Sprite、Shaker、粒子和 Trail，但观看端不读取本地输入、不运行 Actor 碰撞、不碰 Camera，也不会执行第五章结尾的 `End` 换房回调。Player 发送启用/孵化/动画、位置、速度、Dash 与视觉状态，并约每 `0.1s` 提供绝对锚点；Watcher 使用远端 `TimeRate` 只在该实体自身更新期间推进原版视觉组件和位移，随后立即恢复本地时钟，避免接管整个 Watcher。Dash、破壳和撞墙使用表现事件；Barrier、Temple Cracked Block 等世界结果仍由各自适配器同步。Glitch、Distort Anxiety、ColorGrade、ScreenPadding 与 CanRetry 在观看期间由该实体暂时拥有，并在实体离场、停止观看或模块卸载时恢复进入 Void 前的 Watcher 本地基线。

Key 与 Lock Block 使用独立过程状态补足拾取、跟随、投入锁孔和解锁动画，最终移除仍与 `PersistentSession` 的 `DoNotLoad` 结果一致。Watcher 使用不访问本地 Player、Follower Leader 或 Session 的专用视觉协程；每次远端使用带有本地代次，Gone、Replace、切房或重载会先取消旧代次再移除实体，不再让已经脱离 Level 的原版 `Key.UseRoutine` 继续访问粒子系统。Theo Crystal 和 Glider 统一使用 `Idle`、`Carried`、`Thrown`、`Moving`、`Flying`、`Destroying`、`Gone` 阶段；携带外观沿用 PlayerFrame 的 `HoldableInfo`，动态阶段按固定间隔发送绝对位置、速度和旋转校正，释放事件额外携带权威位置。Watcher 对 Glider 重放原版拾取生命周期并同步 `bubble`，避免松手后返回地图出生点；动画继续采用生产端状态，不由 Release 事件猜测。携带中的 Theo Crystal 会补上原版光源，其观看副本碰撞 HeartGem 时不会启动本地收集 cutscene。Theo Crystal Pedestal 另同步放置结果。PlayerFrame 的 `RedBoosted` 标志用于在红泡泡跨房后保持原版 PlayerSprite 的 `bubble` 动画，不额外绘制第二套 Booster Sprite。Badeline Boost 与 Fling Bird 只同步装置阶段、节点和绝对位置，不在 Watcher 端运行控制本地 Player 的协程。Wall Booster 同步冷热外观，Torch 同步点燃，Temple Cracked Block 使用方向事件重放碎裂，Temple Big Eyeball 分离普通反弹与 Theo 触发的破裂表现；神殿结局 cutscene 仍由 Watcher 跳过。

## 约定

- 高速路径中的动画字符串使用 `PooledStringManager`，不要在每帧协议中引入新的普通字符串字段。
- 可选的运行时状态用 nullable 明确表达，例如 Debug Map 中 `PlayerState` 可能为空。
- 修改共享结构时同步更新包注册、客户端/服务端 handler 和序列化测试。
