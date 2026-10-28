# 任务清单：全局通关分享卡片

- [√] 1. 定义通关记录、发送及广播包，补充协议验证测试。
- [√] 2. 追加协议注册与版本，服务端验证、节流及全局广播。
- [√] 3. 实现通关采集和最近记录存储。
- [√] 4. 接入 SpeedrunTool 存读档跟踪，沿用既有返回同一对象机制防止标记回滚。
- [√] 5. 扩展 ChatItem 和聊天列表可变高度渲染。
- [√] 6. 实现通关卡片和接收处理。
- [√] 7. 添加分享指令、中英文文案和使用说明。
- [√] 8. 检查输入边界、发送身份和频率控制。
- [√] 9. 运行测试、服务端及客户端构建，记录验证限制。
- [√] 10. 初始化并同步知识库，归档方案包。

## 验证结果
- `dotnet test source/MiaoNet.UnitTest/MiaoNet.UnitTest.csproj --no-restore`：162 项通过，0 失败、0 跳过。沙箱阻止测试框架的回环套接字，经审批执行后通过。
- 服务端通过上述测试构建；MockClient 构建成功。
- 客户端使用本机 Celeste 引用构建成功；输出至仓库内 `source/artifacts/clear-share-mod`，未修改游戏安装。
- 客户端保留原有 ChatTabListView.InitialTabTitle 的 CS8618 警告，无新增编译警告。
- `git diff --check` 通过。
- 未在游戏内实测。需手工验收卡片的字形、裁剪与缩放、SL 保存/恢复、重新进入关卡的标记重置、存档落盘与特殊地图收集规则。

## 实现文件清单
- `source/MiaoNet.Shared/Data/LevelClearRecord.cs`
- `source/MiaoNet.Shared/Packet/Packets/PacketLevelClear.cs`
- `source/MiaoNet.Shared/AssemblyInfo.cs`
- `source/MiaoNet.Shared/Connection/Connection.cs`
- `source/MiaoNet.Shared/docs/packet-system.md`
- `source/MiaoNet.Server/Server/LevelClearShareLimiter.cs`
- `source/MiaoNet.Server/Server/MiaoServerService.LevelClear.cs`
- `source/MiaoNet.Server/Server/MiaoServerService.PacketHandling.cs`
- `source/MiaoNet.Server/Server/MiaoClientConnection.cs`
- `source/MiaoNet.Client/Game/LevelClearTracking.cs`
- `source/MiaoNet.Client/Game/MiaoNetModule.cs`
- `source/MiaoNet.Client/ModInterop/SpeedrunToolInterop.cs`
- `source/MiaoNet.Client/Command/MiaoNetCommand.LevelClear.cs`
- `source/MiaoNet.Client/Command/MiaoNetCommand.Commands.cs`
- `source/MiaoNet.Client/Connection/MiaoNetContext.PacketHandling.cs`
- `source/MiaoNet.Client/Components/ChatComponent.cs`
- `source/MiaoNet.Client/Components/ChatComponent.LevelClear.cs`
- `source/MiaoNet.Client/Data/LevelClearChatItem.cs`
- `source/MiaoNet.Client/ModFolder/Dialog/English.txt`
- `source/MiaoNet.Client/ModFolder/Dialog/Simplified Chinese.txt`
- `source/MiaoNet.Client/ModFolder/everest.yaml`
- `source/MiaoNet.Client/docs/command-system.md`
- `source/ChatInputBox/ChatInputBox/ChatItem.cs`
- `source/ChatInputBox/ChatInputBox/ChatMessageBox.cs`
- `source/ChatInputBox/ChatInputBox/ChatMessageListView.cs`
- `source/MiaoNet.UnitTest/LevelClearTests.cs`
- `source/MiaoNet.UnitTest/LevelClearBroadcastTests.cs`

知识库包括 `helloagents/project.md`、`CHANGELOG.md`、`wiki/` 的总览、架构、API、数据与模块说明，以及本方案包和历史索引。
