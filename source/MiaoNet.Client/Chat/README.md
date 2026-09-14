# 聊天数据模型与编辑内核

这里只有数据与编辑逻辑，不含任何渲染或按键轮询。
屏幕 UI（消息列表、标签栏、输入框、补全浮层）由 `MiaoNet.Client/UI/` 的 retained 节点实现，
见[客户端 UI 层](../docs/ui.md)。

## 类型

| 类型 | 职责 |
|---|---|
| `ChatMessageManager` | 统一消息记录、各标签页记录、活动标签、折叠窗口与变更计数 `Version` |
| `ChatItem` | 一条消息：格式化时间戳、`ChatText`、折叠计数（纯数据） |
| `ChatText` / `ChatTextSegment` / `ChatTextStyle` | 富文本解析：分段文本、颜色、下划线/删除线/描边 |
| `TextBuffer` | 文本与光标：插入、退格、删除、`DoCompletion`、光标前后切片 |
| `ICompletionProvider` / `Completion` | 依据光标前文本提供候选及替换范围 |
| `FoldCounter` | 折叠计数的纯动画数学（缩放、抖动、彩虹色） |
| `NewMessageShowingMode` | 未激活时显示哪些消息的策略枚举 |

## 消息与标签页

`ChatMessageManager.ChatLog` 保存全部消息，每个标签页另有独立列表。`ActiveTabIndex=-1`
表示 `ALL`，否则 `ActiveChatLog` 指向对应标签页。

```csharp
manager.AddTab("Global");
manager.AddChatMessage(new ChatItem(dateTime, text), dateTime, "Global");
manager.CycleTabForward();
manager.CleanHistory();
```

- `tabName` 指定时，消息进入总记录和该标签页；不存在的标签页会自动创建。
- `tabName=null` 时，消息进入总记录和所有现有标签页，适用于本地公告。
- `foldKey` + `foldedText` 在 `FoldWindowSeconds` 内把重复消息折叠成一行并递增 `RepeatCount`。
- `CleanHistory` 清空总记录和各标签消息但保留标签；`CleanUp` 连标签一起清空。
- 任何改动都会递增 `Version`，宿主据此判断是否需要重建 UI 快照。

## 文本编辑内核

`TextBuffer` 保存 `Text`、`CaretPosition`、`TextBeforeCaret`、`TextAfterCaret`，并在变化时触发
`TextOrCaretChanged`。上层的编辑策略（控制字符映射、粘贴过滤、`MaxTextLength` 截断、补全选择
与接受规则、IME composition、光标闪烁）在 `MiaoNet.Client/UI/Controls/TextEditingController.cs`，
输入源由宿主推入，因此可脱离游戏测试。

## 依赖约束

本目录在客户端里是普通源码；`MiaoNet.UnitTest` 通过
`<Compile Include="..\MiaoNet.Client\Chat\**\*.cs" />` 编译**同一批文件**，因此这里不得引入
Celeste / Monocle 类型。

`Color` 是个例外，而且是个已知的坑：这些源码裸用 `Color`，没有 `using`。
客户端因为它自己隐式 `global using Microsoft.Xna.Framework;`，`Color` 解析成 XNA `Color`；
测试项目则在 `source/MiaoNet.UnitTest/Chat/Color.cs` 里提供了一个同命名空间的 `Color` 垫片。
换句话说 **`ChatTextSegment.Color` 在两侧不是同一个类型**——共享的只是源码文本。

要让这条约束名副其实，需要把 `Color` 换成 `UiColor` 并删掉垫片；代价是 `MiaoNetFont`
（世界空间文本渲染，4 处 `seg.Color * alpha`）要多一次 `UiColor` → XNA `Color` 的转换。
尚未做。
