# ChatInputBox

`ChatInputBox` 是以源码形式复用的聊天 UI 组件，包含文本编辑、补全、富文本、统一历史、标签页历史和消息渲染。宿主通过 `IScalelessTextRenderer` 提供字体测量与绘制能力。

## 主要类型

| 类型 | 职责 |
|---|---|
| `InputBox` / `TextBuffer` | 输入、光标、编辑、历史和补全应用 |
| `ICompletionProvider` / `Completion` | 根据光标前文本提供候选 |
| `ChatText` / `ChatTextSegment` | 富文本解析和分段样式 |
| `ChatItem` | 可选时间戳和一条消息的渲染数据 |
| `ChatMessageManager` | 统一消息记录、标签页记录和活动标签 |
| `ChatMessageListView` | 当前记录的布局、淡入淡出和滚动 |
| `ChatTabListView` | `ALL` 与各标签标题的渲染 |
| `ChatMessageBox` | 面向宿主的消息/标签页组合入口 |

## 消息与标签页

`ChatMessageManager.ChatLog` 保存全部消息，每个标签页另有独立列表。`ActiveTabIndex=-1` 表示 `ALL`，否则 `ActiveChatLog` 指向对应标签页。

```csharp
messageBox.AddTab("Global");
messageBox.AddChatMessage(dateTime, text, "Global");
messageBox.CycleTab();
messageBox.CleanHistory();
```

- `tabName` 为指定名称时，消息进入总记录和该标签页；不存在的标签页会自动创建。
- `tabName=null` 时，消息进入总记录和所有现有标签页，适用于本地公告。
- `CleanHistory` 清空总记录和所有标签消息，但保留标签。
- `CleanUp` 同时清空消息、标签和列表视图状态，适合宿主断线或卸载。

`ChatTabListView` 仅在消息框激活时渲染，标签切换由宿主绑定按键并调用 `CycleTab`。

## 输入和补全

`TextBuffer` 保存 `Text`、`CaretPosition`、光标前后文本，并提供插入、退格、删除、清空和 `DoCompletion`。文本或光标变化会触发补全重新计算。

`ICompletionProvider.GetCompletions(string input)` 接收光标前文本，返回 `Completion`：

```csharp
public readonly struct Completion
{
    string Content; // 插入内容
    string Display; // 列表文本
    int Remove;     // 插入前从光标前删除的字符数
}
```

默认键盘行为包括字符输入、Backspace/Delete、Home/End、左右移动、上下选择候选、Tab 应用候选和 Ctrl+V 粘贴。宿主可用 `SetSuppressCompletions` 避免程序化修改后立即弹出新候选。

## 消息列表

空闲模式只显示最近消息，超过 `ShowDuration` 后淡出；激活模式显示当前标签的较长历史并允许滚动。常用配置包括 `IdleMaxCount`、`ActiveMaxCount`、`ShowDuration`、`BackgroundOpacity` 和 `TextOpacity`。

每条 `ChatItem` 可保存 UTC/本地时间来源；渲染时格式化本地时间并和消息绘制在同一背景条中。标签切换只改变视图数据源，不复制 `ChatItem` 内容。

## 富文本

`ChatText.Parse` 将格式码拆成 `ChatTextSegment`，支持预设颜色、自定义 `RRGGBB` 颜色、重置、下划线、删除线、描边和反斜杠转义。宿主的 renderer 负责测量、普通绘制、描边和不受游戏缩放影响的坐标处理。

## 集成

`ChatInputBox.msbuildproj` 是 NoTargets 源码项目。消费者通过 `Compile Include="../ChatInputBox/**/*.cs"` 链接源码，组件本身不生成运行时 DLL。可运行 `source/ChatInputBox/ChatInputBoxExample` 查看 Everest 集成；构建示例需要 `CelesteRootPath`。
