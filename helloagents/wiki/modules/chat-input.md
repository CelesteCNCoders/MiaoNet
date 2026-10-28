# 聊天组件

## 模块概述
ChatInputBox 提供输入、补全、标签、历史及消息列表；状态：开发中。

## 可变高度消息规范
ChatItem.MeasureHeight 返回消息实际占用高度；render 为虚方法。ChatMessageBox.AddChatItem 插入自定义条目，默认不折叠。列表累计真实高度计算滚动。
宿主可设置 RenderClipped 回调，自行保存和恢复 SpriteBatch 状态。MiaoNet 提供屏幕矩阵转换后的矩形裁剪，防止卡片遮住输入框。

## 参考
[组件说明](../../../source/ChatInputBox/ChatInputBox/docs/chatinputbox.md)
