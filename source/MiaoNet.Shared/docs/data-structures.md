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

`PlayerState` 是可克隆的完整状态，包含位置、动画/帧、缩放、`PlayerStateFlags`、冲刺数、帧时间、精灵模式、Follower、风向、持有物和可选的最终 Camera 世界坐标。`PlayerStateDelta` 是每帧使用的增量，`FrameFlags` 控制是否携带冲刺、持有物、Follower 初始/增量、风向和 Camera；Camera 只在 Player 存在 Watcher 时附加到既有帧包，不增加独立高频包。服务端在 `ServerMap.StateLock` 下将增量应用到玩家状态，并拒绝非有限 Camera 坐标。Watcher 在非转场帧中不保留隐藏 Player、Camera Trigger 或房间演出实体产生的本地 Camera 位移，而是在 `Level.Update` 完成后应用远端样本；原版 `Level.TransitionTo` 运行期间则暂停远端应用并由转场独占 Camera。

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

`WatchSceneDelta` 保存产生变化时的位置、连续序号、flags 增删，并用 `HasTouchSwitchState` 区分“未更新此类状态”和“用空集合替换状态”。实体持续状态使用 `None`、`Patch`、`Replace` 三种模式；短暂动画、音效等按产生顺序放入 `WatchEntityEvent`。Touch Switch 与 `Replace` 实体集合均表示当前房间的完整状态，因此切房和重复进入房间可以丢弃旧缓存。`RoomTransition` 保存 Player 实际触发原版转场时的 source、target、出口位置与方向；`IsDeathRespawn` 标记普通死亡或 Retry 后的轻量完整状态。独立的 `DeathWipe` live-state 在 Player 实际调用原版死亡 WipeOut 前发出，Watcher 同时开始缩圈，将重生状态缓存到完全黑屏帧再原子应用；快照尚未到齐时保持全黑，应用完成后直接显示新状态，不额外播放 WipeIn。普通死亡保留当前 Level；Touch Switch、Temple Cracked Block、Final Boss Moving Block 及其地图 Spikes 等单向状态在黑屏帧由对应适配器定向重建。适配器无法原地逆转的局部状态只记录实体类型诊断，不得把 `IsDeathRespawn` 升级为整房重载。`RequiresRoomReload` 只表示生产端实际发生的 F5、读档或其他显式完整生命周期；只有携带完整状态的该类增量才能授权 Watcher 调用 `Level.Reload()`。死亡后直接进入其他房间的特殊生命周期（例如 PlayerSeeker 结尾）会把目标房间完整 `Replace` 标记为 `IsDeathRespawn`，保留死亡上下文并在目标房间就绪后无额外复活动画地恢复 Ghost。

实体状态采集以适配器类型为独立失败边界：生产端先严格校验每个适配器生成的单条状态；抛出异常或生成无效 payload 的类型会被隔离，同房间连续生产时沿用该类型最后一次有效状态，首次快照或切房无可用基线时只省略该类型。聚合增量仍不通过完整协议校验时，先丢弃瞬时事件并重建一次完整 `Replace`；如果仍然无效，只跳过这一次场景更新并保留当前 Watch 会话。服务端的 payload、位置和序号校验不放宽，实际收到畸形包时仍按无效包处理。

`PersistentSession` 实体状态集中保存当前房间的 `DoNotLoad`、已收集草莓 ID、Cassette、HeartGem、第九章 `fake_heart`、Summit Gems、检查点命中状态和复活点，并保存草莓、Cassette 与 HeartGem 的幽灵外观。它覆盖普通、幽灵、梦境与第九章假结局 Crystal Heart，草莓、Golden Berry、Key、Lock Block、Summit Gem，以及永久 Dash Block、Fake Wall、Temple Cracked Block 和 Crumble Wall 的最终存在状态。草莓籽不再写入虚假的父草莓 `DoNotLoad`，而由独立状态同步 ghost 精灵、待机、跟随、返回、合并和最终清除；草莓、种子和 Key 正在跟随玩家时仍使用原有 Ghost Follower 表现，Sprite ID 改变时会重建对应跟随物，不会在观看端给隐藏的本地 Player 创建重复 follower。普通“消失”变化直接移除本地实体；状态回退要求实体重新出现或条件实体重新求值时，由对应适配器执行定向重建，失败仅留下类型化诊断并等待后续权威状态收敛。`Checkpoint` 与 `SummitCheckpoint` 另用按 Entity ID 标识的布尔状态同步亮起结果。

`WingedStrawberry` 使用地图 `EntityData.ID` 同步 `Present`、`FlyingAway` 和 `Absent` 三态。冲刺时观看端复用原版飞走逻辑；实体飞出房间后仍保留 `Absent` 状态，直到被观看玩家的房间生命周期重新创建该草莓。正在作为 Player Follower 的草莓由 `PersistentSession` 与原有 Ghost Follower 处理。

短周期互动实体分别同步 Spring 的启用与弹跳事件，Refill 的可用生命周期，Fly Feather 的收集、护盾碰撞与重生事件，可重复 `FakeHeart` 的碰撞、击碎与重生事件，以及 Booster 的进入、随玩家冲出、破裂和重生阶段；Bumper 同步冷热模式、冷却与碰撞方向，Cloud 同步运动阶段。机关适配同步 Dash Switch 和 Temple Gate 的按压/开门进度、Crumble Platform 的起始震动、砖块出入事件与最终可见状态、Level Core Mode，以及 Heart Gem Door 的计数、可见性和完整开门渲染进度。

`MovingSolid` 状态族使用原版 `EntityData.ID` 标识 Zip Mover、Swap/Switch Block、Move Block、Falling/Final Boss Falling Block、Crush Block、Sinking Platform、Floaty Space Block、Dream Block、Golden Block、Glass Block 和 Star Jump Block。每项状态携带实体类型、可见性、碰撞状态、绝对位置、原版阶段和至多三个实体专用进度值。Floaty Space Block 在 Watcher 端复用原版组长的 `Moves` 与 `Jumpthrus` 关系，使整组连接的 JumpThrough 按权威位移一同移动。Dream Block 只同步 Dream Dash 模式、one-use 状态和激活期白色填充；粒子帧与边框 wobble 相位继续由 Watcher 的原版 `DreamBlock.Update` 本地推进，不发送或逐帧回写 `animTimer`、`wobbleEase`，避免本地视觉时钟与网络样本互相争用。Bounce Block 使用独立状态保存冷热模式、阶段、绝对位置和原版计时器，并以单个破坏事件携带实际碎片方向，避免观看端自行触发或重复播放破坏动画。Watcher 在每次本地场景更新后重新收敛到最近的远端绝对状态，避免本地隐藏 Player 缺少触发条件时实体自行回退。连续变化的坐标或进度只在对应实体实际运动期间产生补丁；停止观战后仍通过房间重载恢复本地场景。

`PeriodicPlatform` 统一同步普通 Moving Platform、旋转平台生成的运行时子平台、Slider、Track Spinner 和 Rotate Spinner。地图实体沿用 `EntityData.ID`，旋转子平台使用由初始几何信息计算的稳定正数 ID。Slider、Track Spinner 和 Rotate Spinner 在本地运行原版确定性运动，并每 0.1 秒取得一次权威位置、速度/进度和方向锚点；Slider 额外同步当前四向运动方向，避免转角后仅靠表面法线无法恢复路径。Watcher 对小误差按比例软纠偏，仅在方向/模式改变、误差过大、首次快照或死亡黑帧时直接重锚，避免逐包硬跳和长期相位漂移。死亡和 Retry 的 `IsDeathRespawn` 生命周期会在全黑帧强制应用最新锚点，即使 payload 与旧缓存相同也不会跳过。普通完整 `Replace` 不会因为 Clutter 等无关实体变化强制重置已存在的 Spinner。Player 暂停时 Watcher 同步冻结这些本地运动。依赖 Player Rider 下沉的 Moving Platform 暂时继续使用权威绝对状态。`CassetteBlock` 以子编号区分全房间 Manager 节拍与单个 Cassette Block；方块状态包含绝对位置与碰撞高度，Watcher 不推进本地 Manager 节拍，避免预切换尺寸变化累积。`SwitchGate` 保存绝对位置、碰撞、Wiggler 和图标帧。`ClutterSystem` 以子编号区分 Color Switch、Cabinet、Clutter Door、三色 Clutter 组存在状态和当前直接接触根块；接触根块使用按生成几何排序的稳定 ID，并在首次出现后保留 Active/Inactive 墓碑状态，因此结束接触只产生局部 Patch，不会因临时键消失升级为全实体 `Replace`。整组清理事件触发原版吸收碎片、同色基底停用与柜门收束续程，Active 根块在 Watcher 端调用原版 `WeightDown()`，其递归与轻量浮动由本地演算。`DoorMechanism` 覆盖 Door、Trapdoor 与 MrOshiroDoor；持续状态负责最终收敛，有序事件负责原版开启方向及动画。

危险实体不使用隐藏的本地 Player 作为触发源。`StaticSpinner` 仅记录本房间已经销毁的 Crystal Static Spinner ID，并以稀疏集合和破坏事件同步；Watcher 播放原版同等的音效与碎片后隐藏实体但不将其移出场景，因此死亡快照可以原地恢复，而不会为 36,938 个原版静态 Spinner 建立逐实体常驻快照。Dust Static Spinner 只禁用 Watcher 碰撞。`TriggerSpikes` 使用父实体 `EntityData.ID` 与尖刺数组下标同步每段的触发、伸出和回收计时，并以事件重放首次触发音效。`FireBall` 使用地图父实体 ID 与生成序号标识同一路径上的火球；Watcher 运行原版确定性运动，并每 0.1 秒接收绝对位置、路径进度和速度锚点，小误差软纠偏，模式变化、较大误差及死亡快照直接重锚。冰球击碎仍以独立事件重放音效、动画和粒子。`Lava` 以子编号 `0/1` 区分每房间唯一的 Rising Lava 与 Sandwich Lava，直接同步父实体位置、上下 LavaRect 位置、冷热视觉参数、模式和阶段；Watcher 不运行这些依赖本地 Player 的危险实体更新，但会按原版 `4/s` 在本地推进纯视觉冷热插值，并让同一模式内的网络锚点保持单向收敛，避免冷模式被较旧的热色进度覆盖。颜色、波形、淡出范围或位置变化时会使 `LavaRect` 的缓存顶点失效，确保完全冻结的 cold Lava 也能重建为正确的冷色填充。

`Snowball` 和 `Puffer` 使用 Player 权威状态与约 0.1 秒绝对锚点；阶段、动画或存在性变化立即发送。Puffer 沿用地图 `EntityData.ID`，在离散阶段不变时按实际到达间隔在 Watcher 本地插值，不因高速移动超过固定距离阈值而逐锚点硬跳；动画帧只在首次状态、阶段/动画切换或生命周期重置时校正。Snowball 使用生成它的 `WindAttackTrigger` ID，Watcher 只运行原版水平速度和正弦视觉运动，重置坐标完全来自 Player；破碎阶段由 `Destroy` 到成功 `ResetPosition` 的显式生命周期锁存，不从一次性 `break` 动画结束后被清空的动画 ID 推断。Snowball 破碎和 Puffer 爆炸使用纯表现事件；死亡遮罩期间仍会先把已经到达的破碎等事件应用到旧生命周期，再在全黑帧以重生 `Replace` 原子切换实体状态，避免致死 Snowball 的破碎事件被重生快照覆盖。Puffer 的观看端爆炸只播放 Sprite、音效、震动、位移和粒子，Theo、Touch Switch 与可破坏物的结果仍由各自状态适配器同步，不在 Watcher 重复执行。

`BadelineOldsite` 不读取 Watcher 隐藏的本地 Player。存在、开始/停止追逐、悬浮、死亡笑脸及动画覆盖等离散生命周期变化携带位置和原版追随延迟；连续运动主要复用已有的逐帧 PlayerFrame，在 Watcher 保存至多四秒的远端位置、动画与朝向历史，并按原版 `1.55s + 0.4s * index` 延迟取样和 `500/s` 追近速度本地演算。追逐期间另以约 `0.1s` 的稀疏位置锚点做指数软纠偏，避免越过原版路径边界后被下一次生命周期状态硬拉回。收到 `Visible false -> true` 的实时生命周期边沿时，Watcher 还会用同一份 PlayerFrame 历史重建原版出生目标，在本地同时推进 `0.5s` 的缩放/颜色/头发淡入和 `followBehindTime - 0.1s` 的入场位置 Tween；完整快照和生命周期重置不会错误重播出生表现。初次观看及死亡重生的完整快照会额外携带分片的一次性 Player ChaserState 历史种子，避免 Watcher 等待追随延迟窗口填满时让 Badeline 暂停；每片保持在通用 1 KiB 实体 payload 上限内，普通增量不重复发送。Watcher 只更新 Badeline 的 Sprite、Hair、Trail 与遮光表现，不运行原版追逐协程、碰撞或本地 Player 相关 AI。

`AngryOshiro` 作为每房间唯一的运行时实体使用固定实体键。Player 权威发送 Waiting、Chase、ChargeUp、Attack、Hurt、Dummy 等离散阶段，并约每 `0.1s` 发送位置、速度和视觉锚点；阶段与可见性变化立即发送。Watcher 保留原版 `oshiro_boss` Sprite、组件与 `Depth`，在本地使用远端 PlayerFrame 位置、当前远端 Camera 和原版速度公式推进 Chase、ChargeUp、Attack、Hurt，再以网络锚点软纠偏，阶段切换、死亡生命周期或大偏差直接重锚。Watcher 不运行 AngryOshiro 的 StateMachine、PlayerCollider 或转场回调；权威 `TimeRate` 与 Distort Anxiety 仅作为表现状态应用，并在 Oshiro 离场或停止观看时恢复 Watcher 基线。踩踏事件携带权威位置并短暂暂停锚点纠偏，避免 hurt 动画刚开始便被拉回。

`SeekerSystem` 将地图 Seeker 与由 `SeekerStatue` 孵化出的同一只 Seeker 视为共享 `EntityData.ID` 的单一生命周期。Player 权威同步 Statue、Hatching、Seeker 三种形态，以及 Seeker 的八个原版 StateMachine 阶段、动画、朝向、速度、光照和攻击参数；Watcher 在形态切换时替换原版实体主体，禁用本地寻路、Player/Holdable 碰撞与 AI StateMachine，仅推进 Sprite、Shaker、光照和拖影，并在约 `0.1s` 的绝对锚点之间按速度本地演算。撞墙事件携带权威位置与速度并立即重锚，重生阶段在 Watcher 本地补发原版 Regen 粒子；攻击、踩踏和其他重生边沿仍使用有序表现事件。`SeekerBarrier` 保持地图 ID 和原版纯视觉 Update，始终不参与 Watcher 碰撞；反射闪光作为事件传播，最终 Flash、Solidify 状态由低频状态收敛。

`PlayerSeeker` 同样保留原版 Seeker Sprite、Shaker、粒子和 Trail，但观看端不读取本地输入、不运行 Actor 碰撞、不碰 Camera，也不会执行第五章结尾的 `End` 换房回调。Player 发送启用/孵化/动画、位置、速度、Dash 与视觉状态，并约每 `0.1s` 提供绝对锚点；Watcher 使用远端 `TimeRate` 只在该实体自身更新期间推进原版视觉组件和位移，随后立即恢复本地时钟，避免接管整个 Watcher。Dash、破壳和撞墙使用表现事件；Barrier、Temple Cracked Block 等世界结果仍由各自适配器同步。Glitch、Distort Anxiety、ColorGrade、ScreenPadding 与 CanRetry 在观看期间由该实体暂时拥有，并在实体离场、停止观看或模块卸载时恢复进入 Void 前的 Watcher 本地基线。

`FinalBoss` 使用地图 `EntityData.ID` 同步节点、Pattern、动画、朝向、位置、光照、移动阶段和当前表现模式，并在移动期间约每 `0.1s` 发送绝对锚点。Watcher 按权威模式在 Pattern 0 的 `NormalSprite` 与真正的 SpriteBank `badeline_boss` 之间切换，只更新原版 Sprite、Hair、Wiggler 与其他表现组件，不运行攻击协程、Player 碰撞、推人、Camera、对话或音乐控制。协议显式覆盖 `badeline_boss` 的完整原版动画目录，包括 `attack1Loop`、`attack2Begin/Aim/Lock/Recoil`、`star` 和 `recoverHit`；未知动画使用专用值并保留 Watcher 当前动画，不再错误回退为 `idle`。Shot/Beam 出现时会先确保真实 Boss Sprite 已创建，再允许原版 `ShotOrigin`/`BeamOrigin` 取值；不会把 `NormalSprite` 别名写入 `Sprite`。接收端缺少远端动画时保留当前有效动画，必要时回退 `idle`，不向自定义皮肤强制播放不存在的动画。`FinalBossShot` 与 `FinalBossBeam` 使用所属 Boss 的地图 ID 和当前 Boss 实例内单调递增的 `SubID`；完整 `Replace` 会携带当前全部运行时弹幕，因此中途开始观看或死亡重生不依赖生成事件历史。Shot 在锚点之间本地推进直线与正弦轨迹，Beam 同步 Charging、Active、Dissipating 阶段、角度和计时器；两者均不检测 Watcher 隐藏 Player，Beam 发射粒子使用有序表现事件。`FinalBossMovingBlock` 与已经覆盖的 Final Boss Falling Block 分离，使用自身地图 ID 同步 Boss 节点、路径节点、Highlight Alpha、绝对位置和移动阶段，Watcher 禁用原版 Boss 驱动协程，并在锚点间同时插值 Solid 位置和蓝紫 TileGrid 交叉渐变。Break 事件只在 Watcher 端受控调用一次原版 `Finish()`，确保碎片、粒子、主体和附着 `StaticMover` 同时结束；完整状态缺失则无动画销毁附着物并移除主体。`ReflectionTentacles` 的四个原版渲染层共享地图实体 ID，并用 `layer 0..3` 作为 `SubID`；协议只发送 Index、Outwards、Ease、Player 投影点等控制状态，Watcher 在本地生成 Tentacle 网格，Retreat 与 Snap 作为表现事件，并按原版逐触手插值到下一节点、在最终节点淡出，不读取隐藏 Player 的位置。

第五批章节机关继续使用地图 `EntityData.ID`，且所有 Watcher 副本都禁用本地 Player 碰撞、Dash 输入和全局场景控制。`LightningBreakerBox` 同步生命值、`idle/open/opened/break` Sprite、缩放和击中/破坏方向事件；其位置通过原版 Solid 移动接口应用，使附着的普通 Spikes 等 `StaticMover` 跟随下沉与反弹。其最终 `disable_lightning` flag 仍由 Session flags 收敛，Watcher 不启动会改写音乐和全房 Lightning 的原版 `Break()`。`Lightning` 同步可见性、消退、单体 Fade、全局 `LightningRenderer.Fade` 与位置；带节点的移动 Lightning 额外同步原版 `SineInOut` 的归一化相位和往返方向，Watcher 禁用本地 `MoveRoutine` 并由这一份相位连续控制位置，约每 `0.1s` 的绝对状态只负责纠偏。移动 Lightning 在 `Track`、`Untrack` 和边缘拓扑重建期间临时恢复地图初始位置，使 Renderer 的占用网格与边缘几何采用同一坐标系，之后立即恢复远端运动位置。Watcher 在帧末远端 Camera 生效后重新执行电网边缘裁剪，普通镜头运动使用增量刷新，换房或大幅跳变使用完整刷新。Watcher 禁止 Lightning 致死碰撞，在破坏结束时通过 `RemoveSelf()` 触发原版 `Untrack()` 清除电网边缘，单块破碎事件只负责粒子。`BirdPath` 同步目标、速度、动画与位置锚点；Watcher 禁用 `onlyIfLeft` 的隐藏 Player 判定和原版路径协程，但继续运行原版 `Update()` 的纯表现运动、旋转、Sprite 与 Trail，只在远端动画阶段变化时重启一次性动画，并以有序 Roll 事件补齐每段末尾的 `flyupRoll` 音效。`WhiteBlock` 同步启用、激活、背景碰撞层和深度，调用原版激活表现后立即恢复隐藏 Player 的 Depth。`RidgeGate` 同步节点、位置和可见状态，进入事件只重放机关音效，不让 Watcher 的本地 Player 在 `Awake` 中启动移动协程。

`ForsakenCitySatellite` 以 `SubID 0` 保存控制台、输入序列、脉冲/屏幕颜色、Bloom 可见性与动态 HeartGem，五种不重复方向码 `U/L/DR/UR/UL` 映射为 `SubID 1..5` 的 CodeBird。CodeBird 的位置、速度、Sprite 动画/颜色/缩放以及变形后的 Heart 轮廓约每 `0.1s` 发送锚点；Dash 与 Transform 另用有序表现事件启动原版纯视觉协程，Watcher 禁止控制器本地解谜协程读取隐藏 Player。HeartGem 从鸟群位置飞到最终生成点的过程由绝对控制状态收敛，且始终不与隐藏 Player 碰撞。`ReflectionHeartStatue` 同步四个 Torch 的稳定子编号、输入序列、启用状态和动态 HeartGem；点火和整体激活使用有序表现事件。Watcher 在收到中途快照时可从当前 Torch mask 重新启动纯视觉 `ActivateRoutine`，使用原版 `Y - 52` Heart 生成点，同时不注册本地 DashListener，因此不会由隐藏 Player 重复解谜。

第六批把房间环境状态收束为单个 `RoomEnvironment` 状态：Bloom、Lighting、Glitch、Blackhole 强度、WindController 模式、Level Wind、ColorGrade、BackgroundColor、Music event/progress 以及既有音乐和环境参数均以绝对值覆盖。Watcher 禁用 Bloom/Light/Music/Wind/Blackhole 等 Trigger 对隐藏 Player 的本地响应，并在帧末重新应用远端状态；停止观战时恢复进入观看前的本地 AudioState 与视觉基线。`RumbleTrigger` 同步持续状态并用事件重放 Invoke；`RumbleWall` 以地图 ID 保存存在集合，完整快照可定向重建或移除墙体。`Bridge` 使用 `SubID 0` 保存控制器，`SubID 1..N` 按初始瓦片顺序保存每块 BridgeTile 的稳定身份、位置、坠落速度、颜色和震动；瓦片坠落另发有序事件，已从原版可变列表移除的瓦片仍由初始身份表持续捕获，Watcher 在锚点间只使用一个插值器。`IntroCrusher` 通过 Solid 位移同步主体和 StaticMover；`ResortRoofEnding` 同步开始坠落与每张屋顶 Image 的 transform/alpha。Watcher 只推进这些机关的纯组件表现，不运行依赖隐藏 Player 的触发协程。

环境生物继续使用地图 `EntityData.ID`。`BirdNPC`、`FlutterBird`、`MoonCreature` 与 `FlingBirdIntro` 同步可见性、真实 Sprite 动画/帧、transform 和移动状态，并在约 `0.1s` 的位置锚点之间插值。Watcher 停止其本地路径、跟随、碰撞和章节回调，只保留 Sprite、光照、粒子等表现组件；因此它们不会读取隐藏 Player，也不会成为碰撞或剧情触发源。

章节表现实体 `DreamMirror`、`ResortMirror`、`TempleMirrorPortal`、`Gondola`、`WaveDashTutorialMachine` 和 `PowerSourceNumber` 同步各自的破裂、激活、Sprite、位置和演示页状态。`TempleMirrorPortal` 额外同步表现层激活、幕布落下与两侧火炬点亮状态；Watcher 幂等补建原版 BeforeRender/Displacement Hook，让 Portal buffer 本体正常渲染，但不执行入口过场。Watcher 禁用镜面入口、Portal PlayerCollider、Gondola/WaveDash 协程、教程输入与 Camera ease；WaveDash 的 Presentation 只使用远端页码和转场进度，不读取 Watcher 输入。`PlaybackBillboard` 与普通 `playbackTutorial` 保持原版本地确定性播放，不新增网络状态。上述章节机关的最终结果由持续状态收敛，Watcher 不执行对话、音乐改写或章节结束回调。

第七批在 Watcher 隐藏 Player 的整个 `Update` 期间关闭碰撞，并单独拦截 `EventTrigger(onSpawn)`，因此 Event、Interact、MiniTextbox、Credits、Oshiro、ChangeRespawn、NoRefill 等原版 Trigger 以及 Talk/PlayerCollider 不再以 Watcher 本地 Player 为触发源；退出观看后碰撞状态在 `finally` 中恢复。`CoreMessage` 与 `Memorial` 仅在各自原版表现更新期间临时使用远端 Player 坐标，Temple Eye 继续由已同步的 Theo Crystal 驱动，KevinsPC 继续运行确定性视觉时钟。所有 `MiaoNetGhostEntity` 带有原版 `MirrorReflection`，但只在 `MirrorSurfaces` 的反射渲染通道调用 `GhostRender`，因此 Temple Mirror 能显示被观看玩家且不会产生第二套普通场景渲染。

`NarrativeNPC` 优先使用地图 `npc` 的 Entity ID；过场运行时生成的 NPC 则按类型与稳定序号分配确定性 ID。协议同步原版 NPC 的权威存在性、视觉类型、活跃状态、位置、Sprite 动画/帧、transform、光照和深度；动画用双方同版 SpriteBank 中按名称排序的稳定索引编码，不发送普通字符串。Watcher 本地仍存在同 ID NPC 时复用其实例并禁用 Talker/碰撞；NPC 已因跳过剧情而不存在时，按 Granny、Theo、Oshiro 或 Badeline Boss 的视觉类型创建仅含 Sprite/Light 的纯表现代理。代理不继承 NPC，不执行 Added/Awake、剧情协程、对话、碰撞或 Session 写入；完整状态会清除远端已不存在的代理，并隐藏或停用 Watcher 因生命周期差异仍残留的真实 NPC。运行时 `BadelineDummy` 使用生命周期内单调 ID，同步存在性、位置、Sprite、Hair、Light 和 transform；Watcher 创建的副本禁用 AutoAnimator 与原版位移，只推进纯表现组件，因此同时覆盖 Badeline Boost 抱起/抛出和 `CS07_Ascend` 旋转过场。`AscendManager` 同步 index、fade、scroll、introLaunch、outTheTop 和背景色；Watcher 不运行其章节协程，而是幂等补建仅表现用的 `Streaks`/`Clouds` 并在渲染帧推进滚动。生产端实际存在的 `HeightDisplay` 作为 `AscendManager SubID 1` 同步 index、ease、approach 和 pulse；Watcher 禁用它的 Coroutine、Camera ease 与音频进度写入，使 500m 等报幕在 Player 实际生成的同一房间出现，而不会延迟到下一面。`IntroCar` 同步绝对位置、原版 rider 阶段和深度；`ChapterProp` 以 SubID `1/2` 区分 Bonfire 与 Payphone，同步模式、动画帧、光照、Bloom 与运行时参数；`Lookout` 同步交互表现、Sprite、节点和进度，但禁用本地 Interact，Camera 仍只由远端 Player 样本控制。`ConditionalBlock` 用 `SubID 1/2` 覆盖 FakeWall/FakeBlock 与 ExitBlock 的可见、碰撞和渐变进度，完整状态可定向重建缺失主体。SummitCloud、PlayerPlayback、PlaybackBillboard、普通 Water/Waterfall 与装饰实体保持原版本地确定性或纯视觉更新，不加入网络状态；PICO-8 内容、完整对话、Credits 和 Cutscene 不在观战端重放。

Key 与 Lock Block 使用独立过程状态补足拾取、跟随、投入锁孔和解锁动画，最终移除仍与 `PersistentSession` 的 `DoNotLoad` 结果一致。Watcher 使用不访问本地 Player、Follower Leader 或 Session 的专用视觉协程；每次远端使用带有本地代次，Gone、Replace、切房或重载会先取消旧代次再移除实体，不再让已经脱离 Level 的原版 `Key.UseRoutine` 继续访问粒子系统。Theo Crystal 和 Glider 统一使用 `Idle`、`Carried`、`Thrown`、`Moving`、`Flying`、`Destroying`、`Gone` 阶段；携带外观沿用 PlayerFrame 的 `HoldableInfo`，动态阶段按固定间隔发送绝对位置、速度和旋转校正，释放事件额外携带权威位置。Watcher 对 Glider 重放原版拾取生命周期并同步 `bubble`，避免松手后返回地图出生点；动画继续采用生产端状态，不由 Release 事件猜测。携带中的 Theo Crystal 会补上原版光源，其观看副本碰撞 HeartGem 时不会启动本地收集 cutscene。Theo Crystal Pedestal 另同步放置结果。PlayerFrame 的 `RedBoosted` 标志用于在红泡泡跨房后保持原版 PlayerSprite 的 `bubble` 动画，不额外绘制第二套 Booster Sprite。Badeline Boost 同步主体与 Sprite/Stretch 的独立可见性，并以当前节点段的归一化进度在 Watcher 端重建原版 Stretch 旋转和 `YoYo` 缩放；其 BoostRoutine 创建的 `BadelineDummy` 由独立运行时表现状态覆盖。Watcher 的 Update 只推进粒子、光照和组件时钟，不读取隐藏 Player、触发 Skip 或运行 BoostRoutine。Fling Bird 同步 `Wait/Fling/Move/WaitForLightningClear/Leaving`、节点、绝对位置、Fling 速度/目标速度/加速度，以及真实的 `hover/hoverStressed/throw/fly` Sprite、帧和 transform。Watcher 禁用原版控制 Player、Camera、TimeRate 或读取隐藏 Player 的协程与 Update 分支，只推进组件时钟、Trail、Fling 纯运动和位置锚点；激活事件只重放投掷音效，不再猜测 Sprite 或装置阶段。Wall Booster 同步冷热外观，Torch 同步点燃，Temple Cracked Block 使用方向事件重放碎裂，Temple Big Eyeball 分离普通反弹与 Theo 触发的破裂表现；神殿结局 cutscene 仍由 Watcher 跳过。

## 约定

- 高速路径中的动画字符串使用 `PooledStringManager`，不要在每帧协议中引入新的普通字符串字段。
- 可选的运行时状态用 nullable 明确表达，例如 Debug Map 中 `PlayerState` 可能为空。
- 修改共享结构时同步更新包注册、客户端/服务端 handler 和序列化测试。
