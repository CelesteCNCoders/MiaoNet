# Connection

## Protocol

1. Handshake & Initialization

- Client 连接后发送 `HandshakeData`, 包含登录信息
- Server 根据握手信息将玩家分配到对应频道(待完成)
- Server 随后发送 `PacketClientInitial`, 用来同意客户端的连接, 包含:
	- 玩家自身信息：self `PlayerInfo`
	- 频道列表: `ChannelStateInfo` (重命名为 ChannelInfo?), 包含频道 ID 和名称。 
	- 玩家列表: 包含
		- `PlayerInfo`: 昵称、头衔、颜色等元信息。 
		- `LocationInfo`: 所在地图、房间名 (MapSid, MapRoom) 
- Server 广播新玩家登录信息给其他 Client。

2. 地图变更导致的同步

- Client 在地图变更时发送位置更新包, 可能的位置有:
	- InMap (`MapSid` != `string.Empty`, `MapRoom` != `string.Empty`)
	- InDebugMap (`MapSid` != `string.Empty`, `MapRoom` == `string.Empty`)
	- None (`MapSid` == `MapRoom` == `string.Empty`)
	- > 可能有 mod 会暂时离开 `Level` 但仍然算作在这个图里?
- Server 在地图变更时，向同频道同地图的其他玩家广播该玩家的： 
	- `GraphicsInfo` (如果有的话): 图形同步信息(一些数据本地模拟, cnet 类似的逐帧同步等)
	- `PlayerInitialState`: 位置信息, 冲刺状态等.

- Client 接收其他玩家的进入所在图的信息后, 创建对应的 `MiaoNetGhost` 用来显示其他玩家.
- Client 一旦接收过相应玩家的 `GraphicsInfo`, Server 将不在下次有需要时继续发送, 除非对方发起了更新 `GraphicsInfo` 的请求.

- Client 与 Server 开始互相发送帧同步包，进行实时交互。
