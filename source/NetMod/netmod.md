# NetMod 扩展通道

NetMod 是 MiaoNet 为第三方 Mod 提供的通用数据传输通道扩展层. 它最初是为了在 MiaoNet 上兼容 Co-op Helper 等外部 Mod 而设计, 允许第三方 Mod 在不修改核心包表的前提下声明自己的数据包类型, 并通过现有的 MiaoNet 连接完成透明收发.

## 设计动机

蔚蓝社区中有许多联机玩法 Mod (如 Co-op Helper) 原生基于 CelesteNet 的数据接口开发. MiaoNet 若要支持这些 Mod, 必须能够承载它们自定义的数据同步消息. 但 MiaoNet 原有的内建包表在 `MiaoNet.Shared/AssemblyInfo.cs` 中静态编译, 受 C# `[assembly: PacketRegistry]` 特性限制, 一个程序集只能声明一张静态包表. 如果每次为了支持一个外部 Mod 都在核心包表里增加专用协议包, 不仅每次集成都需要修改核心代码并强迫全员发版, 还会使核心网络与特定第三方业务深度耦合.

NetMod 的解决方式是将核心网络抽象为通用的哑管道 (Dumb Pipe). 核心只负责按目标路由搬运不透明的数据字节 (Payload), 完全不介入具体的业务数据结构解析. 无论是仓库中自带的示例 (如同目录下的 NetModSamples/ShootGame/), 还是未来玩家自制的联机小游戏, 都可以直接挂载在这条通道上. (尽管我**不建议**你这么做. 正如项目 README.md 所说, MiaoNet 仍然不稳定, 随时可能会进行破坏性更改. 如您有自制小游戏的需求, 基于 CelesteNet 开发, 通过 NetMod 让其在 MiaoNet 上自动适配也许会是更好的选择)

## 双层 ID 机制

第三方 Mod 各自独立开发, 不可能在编码期协商出全局唯一的包 ID. NetMod 采用两级 ID 机制解决这一冲突.

第一级是 NetModID, 用于区分是哪个 Mod. 客户端在 `Load` 时动态向 `MiaoNetNetMods.Register` 注册自己的 Mod 名称, 本地注册的先后顺序就是它在本地的 NetModID. 即使两个玩家安装的 Mod 顺序不同也不会产生冲突: 客户端连接时会将自己的 Mod 列表上报给服务端, 服务端在转发消息时会根据 Mod 名字自动把发送者本地的 NetModID 动态翻译成接收者本地的 NetModID.

第二级是 LocalID, 用于区分是该 Mod 内的第几种包. 在单个 Mod 内部, 包类型按声明顺序分配局部的 LocalID. 同名同版本的 Mod 在收发两端的包声明顺序保持一致, 从而无需全局协调即可准确定位消息类型. 全局最多支持注册 255 个 NetMod, 单个 NetMod 最多容纳 255 个包类型.

## 数据流与防伪机制

核心包表中仅增加了上行 `PacketNetMod` 与下行 `PacketNetModDelivery` 两个固定载体包, 所有第三方消息均包装在这两个载体包中完成流转. 客户端发送时携带路由信息, 服务端完成路由分发后下发给对端, 剥离接收方无需关心的路由字段以节省带宽.

第三方包在声明时需指定目标作用域 (全服、频道、地图广播, 或针对玩家个人的单播与多播), 发送时分别调用 `netMod.Send` 或 `netMod.SendTo`, 广播会自动排除发送者自身.

在安全性方面, 第三方包的 Payload 内部不需要也不应包含发送者身份. 服务端在转发给接收方时, 会以自身权威将真实的连接 ID 填入外层信封 (`PacketEnvelope.SenderPlayerID`), 接收方直接从信封读取来源, 从根本上防止了客户端伪造他人身份.

## 版本协商与容错

联机双方安装的第三方 Mod 可能版本不一致. 如果数据结构变动却强行反序列化, 会导致解析异常甚至断线.

客户端在连接握手时, 会上报自身声明的 `NetModInfo(Name, ProtocolVersion)` 列表. 服务端在转发消息时, 会对接收方执行版本精确匹配: 只有接收方声明了名称相同且 ProtocolVersion 完全一致的 Mod 时才会投递数据; 若对端未声明或版本不同, 则静默跳过. 这种设计确保了第三方 Mod 之间的版本不兼容只会局限在该 Mod 本身, 不会影响正常的联机会话或其他 Mod 的运行.

## 第三方接入与限制

第三方 Mod 的接入非常直接. Mod 开发者只需实现包的序列化与反序列化方法, 在 Module 的 `Load` 阶段调用 `MiaoNetNetMods.Register` 绑定包类型与接收回调, 随后即可在需要时收发消息, 并在 `Unload` 中调用 `netMod.Dispose()` 注销资源. 完整的局域对战示例可参考同目录下的 `NetModSamples/ShootGame/`.

需要注意的是, NetMod 单包的有效载荷受底层帧长度约束, 上限约为 64 KB (`ushort.MaxValue` 减去协议头与路由开销). NetMod 层不提供自动分片, 超长数据需由 Mod 自身分片处理.
