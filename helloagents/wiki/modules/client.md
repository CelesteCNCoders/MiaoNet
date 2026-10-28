# 客户端

## 模块概述
- 职责：Everest 生命周期、网络连接、聊天、命令、同步与 Ghost。
- 状态：开发中；更新：2026-10-28。
- 入口：`source/MiaoNet.Client/Game/MiaoNetModule.cs`。

## 通关分享规范
`LevelClearTracking` 订阅 Level.OnComplete，草莓结算后取快照。观战不采集；从检查点开始不算全收集。
最近成绩随 Mod SaveData 正常保存。0.7.1 移除通关记录的 SL 检测、存储与显示，保留 SpeedrunTool 原有联机读档兼容。
`LevelClearChatItem` 绘制原生卡片，收件人即使未安装地图也能使用传来的名称显示。卡片固定归入 Global 标签，不参与文本折叠，直播模式隐藏。

底部按实际状态显示红色草莓数量、金色金草莓印章。原版贴图来自 Gameplay atlas，跟随透明度和字号缩放。
界面设置 ClearCardView 支持 Standard、Compact、Hidden，默认 Standard；历史卡片按当前设置测量高度与绘制，Hidden 高度为零，列表跳过隐藏条目。
每次通关生成独立 ClearID，收到自身回显后写入 LastSharedClearID 并保存 Mod SaveData。重复指令提示已分享，新的通关可再次分享；屏蔽设置不阻止确认处理。

## 限制
自定义收集物需要适配；当前是客户端自报数据。最终游戏视觉及特殊地图需手工验收。

## 参考
- [客户端架构](../../../source/MiaoNet.Client/docs/client-arch.md)
- [使用说明](../../../source/MiaoNet.Client/docs/command-system.md)
- [本次变更](../../history/2026-10/202610282121_clear_share/why.md)
