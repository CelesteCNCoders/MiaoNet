# Connection

## Protocol

1. Handshake & Initialization

- Client 发起连接, 首先进行 TLS Handshake
- Client 依次发送 `ConnectionHead`, `EarlyVersionCheck`(客户端版本), `Handshake`(自己的登录信息, 安装的 mod 等信息)
- Server 检查 `ConnectionHead`, 不符合, 也就是不是 MiaoNet 客户端, 强制断开
- Server 检查 `EarlyVersionCheck`, 根据版本是否一致发送 `EarlyVersionCheckAck` 并断开
- Server 根据 `Handshake` 信息进行登录验证等, 随后分配频道, 然后发送 `HandshakeAck`
	- Client 的连接被同意, 紧跟发送 `ClientInitial`, 包含玩家列表等信息.
	- Client 的连接被拒绝(如验证失败), 断开连接.
	- `ClientInitial`, 包含:
		- 玩家自身信息：self `PlayerInfo`
		- 频道列表: `ChannelInfo` , 包含频道 ID 和名称。 
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