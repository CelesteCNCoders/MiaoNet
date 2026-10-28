# 任务清单：通关印章与单次分享

- [√] 将底部状态改为按条件显示的红色草莓数量、金色金草莓和绿色 SL 印章，复用游戏贴图。
- [√] 每次通关生成唯一编号，收到服务端回显后持久化已分享状态。
- [√] 服务端按账号和通关编号防止重复全局广播，重连重试仅向请求者补发确认。
- [√] 增加重连去重与同成绩新通关测试，完成构建和回归验证。
- [√] 新增标准、紧凑和屏蔽三档设置，切换时调整已有消息的布局与高度。
- [√] 更新文档，替换本地测试客户端并重启测试服务器。

## 方案
直接使用既有广播包的自身回显作为发送成功确认，不引入额外确认包。ClearID 为每次通关独立生成的 GUID，重复广播检查发生在服务端并以登录账号为作用域。
协议记录新增字段，版本提升至 0.7.0，避免旧测试客户端误读。服务端去重缓存生命周期为当前进程；正常客户端持久化最近已分享编号，重启后仍禁止重复分享。

## 验证
- 165 项 MSTest 测试通过；客户端及本地证书配置的服务端构建成功。
- 客户端保留原有 ChatTabListView 可空性警告，无新增编译警告。
- 原版草莓、金草莓 Gameplay atlas 资源路径已核对。
- 测试客户端 0.7.0-alpha 已安装，DLL SHA256 与构建产物一致；本机服务器状态接口正常。
- 原 Mods 备份保持在 Mods.backup-20261028-2144，还原脚本已更新进程时间比较方式。
- 游戏内实际视觉、设置持久化和存档读档行为尚需手工验收。

## 主要变更文件
- source/MiaoNet.Client/Data/LevelClearChatItem.cs
- source/MiaoNet.Client/Game/Settings/ClearCardView.cs
- source/MiaoNet.Client/Game/Settings/MiaoNetModuleSettings.cs
- source/MiaoNet.Client/Game/Settings/MenuMiaoNetOptions.cs
- source/MiaoNet.Client/Game/MiaoNetModule.LevelClear.cs
- source/MiaoNet.Client/Game/MiaoNetModule.cs
- source/MiaoNet.Client/Game/LevelClearTracking.cs
- source/MiaoNet.Client/Command/MiaoNetCommand.LevelClear.cs
- source/MiaoNet.Client/Connection/MiaoNetContext.PacketHandling.cs
- source/MiaoNet.Client/Components/ChatComponent.LevelClear.cs
- source/MiaoNet.Client/Components/ChatComponent.cs
- source/ChatInputBox/ChatInputBox/ChatMessageListView.cs
- source/MiaoNet.Client/ModFolder/Dialog/English.txt
- source/MiaoNet.Client/ModFolder/Dialog/Simplified Chinese.txt
- source/MiaoNet.Client/ModFolder/everest.yaml
- source/MiaoNet.Shared/Data/LevelClearRecord.cs
- source/MiaoNet.Shared/Connection/Connection.cs
- source/MiaoNet.Server/Server/MiaoServerService.LevelClear.cs
- source/MiaoNet.UnitTest/LevelClearTests.cs
- source/MiaoNet.UnitTest/LevelClearBroadcastTests.cs
- source/MiaoNet.Client/docs/command-system.md
- source/MiaoNet.Shared/docs/packet-system.md
- helloagents/CHANGELOG.md
- helloagents/wiki/api.md
- helloagents/wiki/data.md
- helloagents/wiki/modules/client.md
- helloagents/wiki/modules/server.md
- helloagents/wiki/modules/shared.md
- helloagents/wiki/modules/tests.md
