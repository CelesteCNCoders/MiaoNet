# MiaoNetShootGame

用 MiaoNet 的 net-mod API 写的一个对射小游戏, 既是示例也是这套 API 的实测用例。

- 同一张房间里的玩家之间对射: **鼠标左键**朝准星方向开火(0.15s 一发);
- 子弹打中谁, **谁自己死**(`Player.Die`, 无视无敌帧), 由被打中的那一侧判定;
- 每死一个人, 聊天栏里打一行: `[MiaoNetShootGame] A 被 B 击杀 | A: 0  B: 3`。

## 两个包

| 包 | 声明时的 scope | 谁发给谁 | 内容 |
|---|---|---|---|
| `PacketShoot` | `Players` | 发给**同一房间**的其他玩家(`SendTo`, 名单从 `ClientState` 里筛) | 枪口位置 + 单位方向 |
| `PacketKill` | `Map` | 被击杀者**广播**给整张地图(`Send`) | 凶手 ID |

发送者身份一律不在 payload 里 —— 服务端转发时会写进 `PacketEnvelope`, 收包方看 `envelope.SenderPlayerID`。
所以 `PacketKill` 的语义是"**我**(envelope 里的 sender) 被 `KillerPlayerID` 打死了"。

三个设计取舍:

- **命中由被打的人判定**, 不是开枪的人。只有被打的那个客户端知道自己的碰撞盒此刻在哪(别人的位置在本地只是
  一份同步过来的幽灵), 所以让受害者自己 `Die` 并发出死亡通知, 谁也代替不了他。
- **子弹只发给同一房间的人**: 其他房间的人既看不见也打不着, 没必要让他们各自模拟一颗子弹。名单用
  `ClientState.Players`(不含自己)筛 `Location`, 顺带避开"定向 scope 不排除发送者"那条规则 —— 否则子弹会被
  服务端弹回来, 本地就多一颗。
- **击杀通知发给整张地图**: 大家都在同一张击杀榜上, 所以每个人收到的死亡通知是一样的。
  广播不含发送者, 所以被打死的人自己那一份在本地直接记。

击杀榜是每个客户端按收到的死亡通知各自维护的 `Dictionary<int, int>`: 没有服务端权威, 也就没有别的来源。
它按玩家 ID 记, 不处理"换个会话后 ID 被复用"这种情况 —— 对小游戏来说不值得。

## 构建与安装

```
dotnet build          # 输出到 ModFolder/Code/ 并复制到 <Celeste>/Mods/MiaoNetShootGame_copy
```

游戏里装上并连接 MiaoNet 之后, 进关卡按住左键就能打(准星是自画的, 因为游戏把鼠标藏起来了)。
单人也能开枪, 只是没有人会死。

工程直接 `ProjectReference` 仓库里的 `source/MiaoNet.Client/MiaoNet.Client.csproj`(`Private=false`,
不把 `MiaoNet.dll` 复制进本 mod 的 `Code/`): 装在 `Mods/Cache/` 里的那份 `MiaoNet.MiaoNet.dll` 是上次启动
游戏时缓存的构建, API 一改就对不上了。运行时的依赖由 `ModFolder/everest.yaml` 里的 `MiaoNet` 声明。

## 文件

| 文件 | 内容 |
|---|---|
| `MiaoNetShootGameModule.cs` | 声明 net-mod、输入与准星、收包、击杀榜与聊天输出 |
| `Packets.cs` | `PacketShoot` / `PacketKill` |
| `Bullet.cs` | 子弹实体: 各自模拟, 撞墙消失, 只对本地玩家做命中判定 |

机制本身的说明见 [`docs/netmod.md`](../../../docs/netmod.md)。
