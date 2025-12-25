# Design

原本打算服务端和客户端共用一个 `OnlineContext` 的, 但是发现服务端需要考虑各种并发问题, 客户端单线程一把嗦,
加上客户端和服务端两边存的东西也不大一样, 所以就分开了

## PooledStringManager & PooledString

~~一套记录客户端和服务端曾经发过的 enum-like 的 string 的记录,
但是目前的实现非常遭, 每个用到了 PooledString 的地方都会一路传染到最顶层的类型要求实现 IHasPooledString, 而每个
解析的地方也要经常制作另一份不含 PooledString 的类型或者将 PooledStringManager 传入, 发包时也被迫引入了 BroadcastProcessed
用来逐客户端发送, 或许是否应该引入 Contextual Serialization 将 PooledString 当成单纯的 string 的 alias 仅在
Serialization 时有所区别?~~

目前已使用 Contextual 和 Contextless 来解决 (虽然好像感觉这个设计还是挺丑的)

## Server 端

Server 端的并发控制相关问题尽可能会使用乐观并发控制. 在线玩家, 频道列表等都会使用不可变类型, 并会原子地进行更新.

## Sync Level

> 玩家间有 3 个 SyncLevel: (目前还没做)
>
> // TODO 改成一定范围内而不是大小不固定的房间?
> 
> - 0: 只同步所在地图和房间名
> - 1: 只会同步位置
> - 2: 所有状态全部同步
> 
> - 0 -> 不在同一个图
> - 1 -> 在同一个图但是不在同一个房间
> - 2 -> 在同一个图且在同一个房间
> 
> 目前的话应该算是大概实现了, 不过只是受于 DebugMap 的影响,
> 玩家图不同或者频道不同就会只进行 SyncLevel 0 的同步,
> 图一样但是在 DebugMap 里会进行 SyncLevel 1 的同步,
> 在图里那就是 SyncLevel 2 的同步了(目前面不一样还是 SyncLevel 2)
> 
> // 由于实现上的困难以及实现后的收效甚微  
> // 上述 synclevel 设计将不会被实现  
> // (发现每个发 state 的地方都要非常繁琐的判断应该发哪种 state)  
> // 而目前发 lite state 的只有 debug map  
> // 但是能在 debug map 里的人能有多少啊(x

目前只要处于图内都会进行完全的同步, 虽然平常大量同图的可能性不太大, 但感觉应该是个需要做的内容

## Dialog Report

> Icon 是否也需要 Report?

地图的 SID -> Dialog 名字客户端可以向服务器汇报, 实际发给玩家/展示在服务器后台的会是其中最高被汇报次数的名字(可能有人自己改图名)