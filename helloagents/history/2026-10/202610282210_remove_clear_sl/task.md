# 移除通关卡片 SL 状态

- [√] 删除 SL 采集、Mod Session 标记、卡片印章和对应翻译。
- [√] 保留 SpeedrunTool 原有联机读档兼容回调。
- [√] 协议末尾保留固定 false 的兼容字节，接收时丢弃旧值，版本调整至 0.7.1。
- [√] 验证旧协议兼容、运行测试并构建客户端：166 项通过，构建成功。
- [√] 更新知识库及当前测试客户端：0.7.1-alpha 安装完成，SHA256 一致。

## 变更文件
- source/MiaoNet.Client/Data/LevelClearChatItem.cs
- source/MiaoNet.Client/Game/LevelClearTracking.cs
- source/MiaoNet.Client/Game/MiaoNetModule.cs
- source/MiaoNet.Client/ModInterop/SpeedrunToolInterop.cs
- source/MiaoNet.Client/ModFolder/Dialog/English.txt
- source/MiaoNet.Client/ModFolder/Dialog/Simplified Chinese.txt
- source/MiaoNet.Client/ModFolder/everest.yaml
- source/MiaoNet.Shared/Data/LevelClearRecord.cs
- source/MiaoNet.Shared/Connection/Connection.cs
- source/MiaoNet.UnitTest/LevelClearTests.cs
- source/MiaoNet.Client/docs/command-system.md
- source/MiaoNet.Shared/docs/packet-system.md
- helloagents/wiki/modules/client.md
- helloagents/wiki/modules/shared.md
- helloagents/wiki/modules/tests.md
- helloagents/wiki/data.md
- helloagents/CHANGELOG.md
- helloagents/history/index.md
