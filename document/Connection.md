# Connection

> AI 整理的, 将就看看吧), 原文就写的挺乱的

## 🧩 协议整理

1. 连接与初始化阶段（Handshake & Initialization）

- Client 连接后发送 `握手数据（HandshakeData）`，包含登录信息。 
- Server 根据握手信息将玩家分配到对应频道。 
- Server 随后发送 `PacketClientInitial` 
	- 玩家自身信息：`玩家信息（PlayerInfo）`
	- 频道列表：`频道列表（ChannelSummaryList）`，包含频道 ID 和名称。 
	- 玩家列表：`玩家列表（OnlinePlayerList）`，包含： 
		- `玩家信息（PlayerInfo）`：如昵称、头衔、颜色等元信息。 
		- `状态信息（StateInfo）`：如所在地图、房间名（mapName, roomName）。 
- Server 广播新玩家登录信息给其他 Client。

2. 状态同步阶段（State Management）

- Client 发送自身的 `状态信息（StateInfo）`。 
- Server 根据 `mapName` 和 `roomName` 判断玩家位置变更： 
	- 若 `mapName` 为空且 `roomName` 为空 → 玩家未进入任何地图。 
	- 若 `mapName` 不变但 `roomName` 变更 → 玩家在当前地图内移动。 
	- 若 `mapName` 变更 → 玩家进入新地图。 
- Server 在地图变更时，向同频道同地图的其他玩家广播该玩家的： 
	- `图形信息（GraphicsInfo）`：如皮肤、外观等。 
	- `初始状态信息（PlayerInitialStats）`：如冲刺次数（dashes）。 

3. 实体创建与更新（Entity Creation & Updates）

- Client 接收其他玩家的图形信息后，创建对应的“远程玩家实体（RemotePlayerEntity）”。 
- Client 保存 `GraphicsInfo`，Server 不再重复发送，除非 Client 主动发起更新请求。

4. 帧同步阶段（Frame Synchronization）

- Client 与 Server 开始互相发送帧同步包，进行实时交互。
