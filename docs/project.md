## 项目结构

- `artifacts`: 项目启用了 `artifact` 风格的编译产物输出, 这里会存放相应的编译产物
- `ChatInputBox`: 聊天栏以及聊天历史记录库
  - `ChatInputBoxExample`: 分离的使用了 `ChatInputBox` 的示例项目
- `MiaoNet.Client`: MiaoNet 客户端项目, 作为一个蔚蓝 Mod
- `MiaoNet.Server`: MiaoNet 服务端项目
- `MiaoNet.Shared`: 共享项目, 包含 Client 以及 Server 共有的部分(例如包的结构定义)
- `MiaoNet.UnitTest`: 一些单元测试(虽然现在没什么东西能测的), 引用了 `MiaoNet.Server` 项目,
在一些客户端独有的但可(或者需要)单元测试的时候会单独引用一些源文件过来, 例如目前的客户端侧的指令.

## 构建

### 环境配置

项目大多数基于 `.NET 8.0` 或 `.NET 10.0`, 因此单独安装 `.NET 10.0 SDK` 即可满足所有项目基本的构建环境需求.

### `MiaoNet.Client`

客户端项目基于 `Everest 6088` 以及其最高可用的 `.NET 8.0`.  
构建暂时需要临时修改 `MiaoNet.Client.csproj` 中 `CelesteRootPath` 项到游戏程序所在目录. 这个做法来自[另一个模板项目](https://github.com/Saplonily/Saladim.CelesteModTemplate). 可以预见它可能会存在一小些跨平台兼容性问题, 期望你不会因此遇到什么问题.  

项目构建后会在 `$(CelesteRootPath)/Mods/MiaoNet_link` 下创建 Mod 文件夹链接, 以方便在游戏中调试.  

#### 多开游戏

对客户端进行更改调试时经常需要多开游戏, 在 `Steam` 平台的游戏版本中, 单独启动游戏程序会快速退出并从 `Steam` 重启, 同时因此避免多开. 这里可以简单提供一个在 `Steam` `Windows` 平台上的多开方法. 只需要在游戏启动时设置环境变量 `SteamAppId` 为 `504230` 即可避免重启, 或者在游戏目录下创建一个 `steam_appid.txt` 文件, 内容为 `504230`. 例如前者可以制作这样的 `.bat` 文件:

```bat
set SteamAppId=504230
start Celeste.exe
```

此外这样多开仍然会有 Mod 热重载时出现文件占用的问题, 有点麻烦的是 `Everest` 没有自带的指定 `Mods` 文件夹的功能, 这里有一个[简单的 patch](./PATH_EVEREST.patch), 对 `Everest` 添加了环境变量 `EVEREST_PATH_EVEREST` 以指定 `PathEverest` 进而指定 `Mods` 文件夹位置的功能. 再加上一些指定 `log` 和存档位置的环境变量, 通常最终可能得到这样的 `.bat` 文件:

```bat
set SteamAppId=504230
set EVEREST_PATH_EVEREST=C:\Program Files (x86)\Steam\steamapps\common\Celeste\miaonet\1
set EVEREST_SAVEPATH=C:\Program Files (x86)\Steam\steamapps\common\Celeste\miaonet\1
set EVEREST_LOG_FILENAME=log_miaonet_1
start Celeste.exe

set EVEREST_PATH_EVEREST=C:\Program Files (x86)\Steam\steamapps\common\Celeste\miaonet\2
set EVEREST_SAVEPATH=C:\Program Files (x86)\Steam\steamapps\common\Celeste\miaonet\2
set EVEREST_LOG_FILENAME=log_miaonet_2
start Celeste.exe
```

### `MiaoNet.Server`

服务端基于 `.NET 10.0`, 和通常的 `.NET` 项目构建一样.

### 配置

在项目 `Directory.Build.props` 中有一些全局配置, 目前有 `UseLocalhostPfx` 以及 `UseCeleMiaoAuth`, 分别对应源文件中的 `USE_LOCALHOST_PFX` 和 `USE_CELEMIAO_AUTH` 宏.  
`UseLocalhostPfx` 开启后客户端与服务端之间的连接将会只接受项目中的 `localhost.pfx` 作为证书, 否则客户端会回归正常的证书验证, 服务端则也要求配置证书.  
`UseCeleMiaoAuth` 开启后客户端与服务端将会进行论坛账号验证, 即这样的构建会接入 `https://bbs.celemiao.com` 的账号系统.  
通常在 `wip` 分支以及 `Debug` 构建下, `USE_LOCALHOST_PFX` 会被开启, `UseCeleMiaoAuth` 会被关闭以方便本地调试.  
不过注意 `UseCeleMiaoAuth` 开启后当前分支构建仍然不能连接目前部署的已在使用的 `alpha` 版本服务器, 你需要切换到 `wip-alpha` 分支.  
目前分发的 `alpha` 客户端就来自 `wip-alpha` 分支的构建.