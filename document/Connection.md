# Connection

## Protocol

1. Handshake & Initialization

> 暂时还没有做 TLS 加密层, 以后加了就是简单套一层

- Client 发送 `ConnectionHead` 以及 `Handshake`, 包含客户端版本, 自己的登录信息, 安装的 mod 等信息
- Server 根据 `Handshake` 信息决定是否同意, 同意后分配频道, 然后发送 `ClientInitial` 包含玩家列表等信息, 否则拒绝并断开连接
	- Client 的连接被同意, 根据 `ClientInitial` 初始化联机相关内容, 开始正常互相发包
	- Client 的连接被拒绝, 如果 `head` 都不对直接断开, 否则发送断开理由再断开.
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

- Client 与 Server 开始互相发送帧同步包，进行实时交互。
