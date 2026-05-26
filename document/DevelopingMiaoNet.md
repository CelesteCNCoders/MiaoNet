# MiaoNet开发上手指南

## MiaoNet开发环境配置
MiaoNet是为Celeste开发的C/S架构联机工具，客户端基于Everest API开发，项目使用.NET框架下的C#语言开发。在这里，我们假设你拥有面向对象编程以及C/S架构、多线程并发的相关基础知识。

### IDE 选择
C#开发的主流商业IDE有Microsoft Visual Studio以及Jetbrains Rider，前者仅适用Windows操作系统但对.NET适配最佳；后者有Linux和MacOs的分发，但适配相对较弱，读者可以自行权衡。
> 使用Rider进行本项目开发时，可在`资源管理器`中右键`ChatInputBox/ChatInputBox`和`MiaoNet.Shared`两个共享项目，以修复Rider在分析无目标构建项目时出现的LSP解析错误。

### 前置要求
- .NET 8.0+ SDK（服务端需要 .NET 10.0）
- Celeste 游戏本体（已安装 Everest mod loader）
- Git

### 设置Celeste安装目录
为了构建和测试MiaoNet，你需要在本地有一个Celeste游戏安装。客户端构建时需要引用Celeste的程序集。

设置方式（按优先级）：
1. 通过MSBuild属性传入：`dotnet build -p:CelesteRootPath=/path/to/Celeste`
2. 通过环境变量：//TODO 未来重构构建系统。 
3. 默认值：`C:\Program Files (x86)\Steam\steamapps\common\Celeste`

Linux下典型路径为 `~/.local/share/Steam/steamapps/common/Celeste`。

### 项目结构
| 项目 | 说明 |
|------|------|
| MiaoNet.Client | 客户端，作为Celeste mod加载 |
| MiaoNet.Server | 服务端，独立运行的控制台程序 |
| MiaoNet.Shared | 客户端和服务端共享的代码（协议、数据结构等） |
| MiaoNet.MockClient | 模拟客户端，用于本地调试和压测 |
| ChatInputBox | 聊天输入框UI组件（共享项目） |
| MiaoNet.UnitTest | 单元测试 |
| PacketDumpInspector | 数据包抓包分析工具 |

## 构建

### 构建客户端
```bash
dotnet build source/MiaoNet.Client/MiaoNet.Client.csproj
```
构建成功后会自动在Celeste的Mods目录下创建符号链接（`MiaoNet_link`），启动游戏即可加载。

### 构建服务端
```bash
dotnet build source/MiaoNet.Server/MiaoNet.Server.csproj
```

## 本地调试

### 编译宏说明
项目通过条件编译宏控制不同的构建行为，定义在 `source/Directory.Build.props` 中：

| 宏 | 默认值 | 说明 |
|----|--------|------|
| `USE_LOCALHOST_PFX` | Debug时自动开启 | 使用内嵌的自签名证书，客户端连接`127.0.0.1`并跳过证书验证 |
| `USE_CELEMIAO_AUTH` | false | 启用CeleMiao平台OAuth认证；关闭时使用简单的名字认证 |
| `PACKET_TRACING` | Debug时自动开启 | 启用数据包追踪日志 |

Debug构建默认启用 `USE_LOCALHOST_PFX`，客户端连接 `127.0.0.1:21473`，服务端使用内嵌的 `localhost.pfx` 证书。因此本地调试时无需额外配置SSL证书。

### 启动本地服务端
```bash
dotnet run --project source/MiaoNet.Server
```
服务端默认监听 `0.0.0.0:21473`，配置文件为 `source/MiaoNet.Server/appsettings.json`。

### 使用MockClient测试
MockClient可以在不启动游戏的情况下模拟多个客户端连接服务器：
```bash
dotnet run --project source/MiaoNet.MockClient
```
启动后输入要创建的模拟客户端数量，每个实例会：
- 以随机名字连接本地服务器
- 模拟进入 `Celeste/LostLevels` 地图
- 每帧发送随机位置更新
- 响应服务器的Ping和传送请求

### 完整本地调试流程
1. 启动服务端：`dotnet run --project source/MiaoNet.Server`
2. 启动MockClient模拟其他玩家：`dotnet run --project source/MiaoNet.MockClient`
3. 以Debug模式构建客户端：`dotnet build source/MiaoNet.Client/MiaoNet.Client.csproj`
4. 启动Celeste游戏，客户端会自动连接本地服务器

## 生产构建
Release构建会关闭 `USE_LOCALHOST_PFX`，客户端将连接云端测试服务器并验证SSL证书：
```bash
dotnet build source/MiaoNet.Client/MiaoNet.Client.csproj -c Release
```

服务端生产部署需要在 `appsettings.json` 中配置真实的SSL证书路径：
```json
{
  "MiaoServer": {
    "Certificate": {
      "CertificatePath": "/path/to/cert.pem",
      "CertificateKeyPath": "/path/to/key.pem"
    }
  }
}
```
服务端会每4小时检查证书文件是否更新，自动热重载。

## 开发沟通规范

### 沟通渠道
- 功能讨论、Bug报告：通过 GitHub Issues 提交
- 设计讨论、开放性问题：通过 GitHub Discussions 进行

### PR流程
1. 从 `wip` 主分支创建功能分支，命名建议：`feat/功能名`、`fix/问题描述`、`refactor/重构内容`
2. 开发完成后向 `wip` 分支提交 Pull Request
3. PR标题简洁明了，描述中说明改动内容和测试情况
4. 确保构建通过后再请求审查

### 代码审查
- 所有合入 `wip` 的代码需要至少一位维护者审查
- 收到反馈后及时回应，修改后重新请求审查

## 我可以从哪里开始

如果你刚加入项目，以下是一些适合上手的方向：

- **熟悉协议层**：阅读 `MiaoNet.Shared` 中的 Packet 定义，理解客户端和服务端之间的通信协议
- **阅读设计文档**：`document`目录下的AIGC文档（bushi） ，描述了整体架构
- **跑通本地环境**：按照上面的调试流程启动服务端 + MockClient + 游戏客户端，确认能正常连接
- **查看 GitHub Issues**：参与一些功能增量开发一类的简单工作
- **完善MockClient**：MockClient目前只实现了基础的Ping响应和传送请求处理，可以补充更多协议的模拟行为，并让它更有可读性
- **补充测试**：为 `MiaoNet.Shared` 中的序列化/反序列化逻辑编写单元测试