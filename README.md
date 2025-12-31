# MiaoNet

CelesteNet 的一个重写, 以应对数以百计的蔚蓝联机玩家.  
称为 MiaoNet, 在可能与之前基于 CelesteNet 的 Miao.CelesteNet(也可能被称为 MiaoNet) 混淆时可以使用 MiaoNet+ 进行区分.  
目前该项目仍在早期的开发中(如你所见目前分支名也叫 wip).

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
  - `MiaoNet.UnitTest`: 一些单元测试(虽然现在没什么东西能测的), 引用了 `MiaoNet.Server` 项目,
在一些客户端独有的但可(或者需要)单元测试的时候会单独引用一些源文件过来, 例如目前的客户端侧的指令.

有关连接具体如何进行可以参考 [`document/Connection.md` 这个文档](./document/Connection.md),
一些碎碎的设计相关的杂念可以在 [`document/Design.md`](./document/Design.md) 中找到.  
有关的更详细的但是是 AI 生成并稍微人工调整的勉强还能看的文档可以在 [`document/AIGC`](./document/AIGC) 中找到.  
**注意这部分文档可以预见的是会经常过时, 建议还是以阅读代码为主.**

## 项目进度

[见该 Issue](https://github.com/CelesteCNCoders/MiaoNet/issues/2)