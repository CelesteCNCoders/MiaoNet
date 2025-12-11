# MiaoNet

CelesteNet 的一个重写, 以应对数以百计的蔚蓝联机玩家.  
称为 MiaoNet, 在可能与之前基于 CelesteNet 的 Miao.CelesteNet(也可能被称为 MiaoNet) 混淆时可以使用 MiaoNet+ 进行区分.  
目前该项目仍在早期的开发中(如你所见目前分支名也叫 wip), 预计将在 2025 结束前完成大部分 CelesteNet 所具有的功能.  

喵服论坛([bbs.celemiao.com](https://bbs.celemiao.com)) 中有关该项目的帖子: [新群服进度追踪](https://bbs.celemiao.com/d/347-xin-qun-fu-jin-du-zhui-zong)

## 项目结构

- `document`: 项目文档
- `source`: 项目源码
  - `artifacts`: 由于项目启用了 `artifact` 风格的编译产物输出, 这里就会存放相应的编译产物
  - `ChatInputBox`: 聊天栏以及聊天历史记录库, 分离开来避免和 MiaoNet 耦合太强(难道还有别的地方会用到它吗 :L)
  - `MiaoNet.Client`: MiaoNet 客户端, 作为一个蔚蓝 Mod
    - `Components`: 借鉴于 CelesteNet, 管理客户端不同部分的显示以及发包等
    - `Data`: 客户端独有的数据类
    - `Entity`: 游戏中会用到的实体, 例如其他玩家的实体 `MiaoNetGhost`
    - `Game`: 游戏本体相关的逻辑
    - `Misc`: 目前只有一个单线程同步上下文类的实现
    - `ModFolder`: Mod 的资源文件
  - `MiaoNet.Server`: MiaoNet 服务端
    - `Data`: 服务器特有的一些数据类
    - `Http`: 提供后台管理 HTTP api 相关的类
    - `Primitives`: 一些服务端需要的客户端所有的结构/枚举
    - `Server`: 实际管理客户端连接等相关的类
  - `MiaoNet.Shared`: 共享项目, 包含 Client 以及 Server 共有的部分(例如包的结构定义)
    - `Command`: MiaoNet 中可用的指令
    - `Data`: 一些储存数据的类(枚举, 玩家所在地图的结构体等)
    - `Helpers`: 目前包含包的序列化/反序列化的逻辑
    - `Packet`: 包相关的东西
      - `Packets`: MiaoNet 中所有的包
    - `PlayerList`: 目前包含玩家列表排序相关逻辑
  - `MiaoNet.UnitTest`: 一些单元测试(虽然现在没什么东西能测的)

有关连接具体如何进行可以参考 [`document/Connection.md` 这个文档](./document/Connection.md),
一些碎碎的设计相关的杂念可以在 [`document/Design.md`](./document/Design.md) 中找到.

## 项目进度

以下内容同步自上面提到的喵服论坛:

> - [x] 基础设施
> - [x] 基本玩家状态同步
> - [x] 部分玩家视觉信息同步(非 CelesteNet 那种每帧每一节头发都同步的)
> - [x] 基础频道相关内容
> - [x] 玩家列表
> - [x] 调试地图玩家显示
> - [x] 杂项玩家状态同步
> - [ ] 更多玩家头发同步(不是这个 b 头发怎么东西这么多)
> - [ ] 跟随物同步
> - [ ] 抓取物同步
> - [x] 聊天栏
> - [ ] 基础指令
> - [ ] 玩家交互
> - [x] Emote
> - [ ] 管理后端
>
> 并没有计划在跨年之前完成的部分:
>
> - [ ] 全量皮肤同步模式(类似 CelesteNet 的那种每节头发都同步的模式)
> - [ ] 项目文档
> - [ ] 地图传递(CelesteNet 中 tp 时不存在地图时的传递地图下载信息)
> - [ ] Mod 支持
> - [ ] UDP 传输支持 (因为国内的 UDP 环境很糟糕, 所以推后很合理)