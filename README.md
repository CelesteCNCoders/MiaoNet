# MiaoNet

CelesteNet 的一个重写, 以应对数以百计的蔚蓝联机玩家.  
称为 MiaoNet, 在可能与之前基于 CelesteNet 的 Miao.CelesteNet(也可能被称为 MiaoNet) 混淆时可以使用 MiaoNet+ 进行区分.  
目前该项目仍在早期的开发中(如你所见目前分支名也叫 wip).  

尽管目前服务端侧的逻辑依然比较混乱.

## 项目结构

- `document`: 项目文档
  - `artifacts`: 由于项目启用了 `artifact` 风格的编译产物输出, 这里就会存放相应的编译产物
  - `ChatInputBox`: 聊天栏以及聊天历史记录库, 分离开来避免和 MiaoNet 耦合太强(难道还有别的地方会用到它吗 :L)
  - `MiaoNet.Client`: MiaoNet 客户端, 作为一个蔚蓝 Mod
    - `Command`: MiaoNet 指令相关
    - `Components`: 借鉴于 CelesteNet, 客户端不同部分的显示以及发包等
    - `Data`: 客户端的一些数据类
    - `Entity`: 游戏中会用到的实体, 例如其他玩家的实体 `MiaoNetGhost`
    - `Game`: 游戏本体相关的逻辑, 如 Everest 要求的 `Module` 类以及设置类等
    - `Misc`: 杂项
    - `ModFolder`: Mod 的资源文件
  - `MiaoNet.Server`: MiaoNet 服务端
    - `Data`: 服务器的一些数据类
    - `Http`: 部分后台的 HTTP api
    - `Server`: 大部分服务器逻辑
        - `Authentication`: 验证相关逻辑(例如获取论坛侧相关信息)
        - `Certificate`: SSL 证书管理
        - `Connection`: 不完整的一些连接抽象
        - `Options`: 服务器选项
        - `Utils`: 杂项
  - `MiaoNet.Shared`: 共享项目, 包含 Client 以及 Server 共有的部分(例如包的结构定义)
    - `Connection`: 连接相关共享类
    - `Data`: 一些储存数据的类(枚举, 玩家所在地图的结构体等)
    - `Helpers`: 网络包以及相关序列化
    - `Packet`: 包相关的东西
      - `Packets`: MiaoNet 中所有的包
    - `PlayerList`: 目前包含玩家列表排序相关逻辑
    - `Primitives`: 一些简单数据类
  - `MiaoNet.UnitTest`: 一些单元测试(虽然现在没什么东西能测的), 引用了 `MiaoNet.Server` 项目,
在一些客户端独有的但可(或者需要)单元测试的时候会单独引用一些源文件过来, 例如目前的客户端侧的指令.

## 项目进度

[见该 Issue](https://github.com/CelesteCNCoders/MiaoNet/issues/2)

## 构建MiaoNet

### 环境配置
- Celeste的Mod管理器安装（Everest / Olympus / CeleMod)
- .NET 8.0+ SDK

- MSBuild构建工具

### 1. 下载MiaoNet
从Github克隆：
```bash
git clone git@github.com:CelesteCNCoders/MiaoNet.git
cd MiaoNet
```
或下载.zip压缩包并解压。

### 2. 构建MiaoNet
以Beta版游玩测试服仅需要构建MiaoNet.Client。在构造前，确认您的Celeste安装路径是否和项目预设的`C:\Program Files (x86)\Steam\steamapps\common\Celeste`相符。如若这不是您的Celeste安装路径，则需要在构建时手动指定。

> ⚠️ 如何获取Celeste安装路径：如果您的Celeste通过Steam平台安装，可在“库”中右键“Celeste”条目，选择“管理” > “浏览本地文件”以获取安装路径。

```bash
dotnet build source/MiaoNet.Client/MiaoNet.Client.csproj\
  -p:CelesteRootPath=/path/to/Celeste
```
构建完成后`MiaoNet_Link`文件链接将在游戏Mod目录下生成，此时使用Mod加载器选中即可游玩测试服。

## LICENSE

本项目部分借鉴了 [CelesteNet](https://github.com/0x0ade/CelesteNet)([MIT](https://github.com/0x0ade/CelesteNet/blob/e962823cf9666024fd255db9cb5d72a3a5c4d7c6/LICENSE))
的一些实现, 约定, 以及一些其所使用的[图片资源](./source/MiaoNet.Client/ModFolder/Graphics/Atlases/Gui/miaonet).

## Credits

- sky scale: 绘制了直播模式以及合影模式的图标(`live_mode.png`, `group_photo_mode.png`)
