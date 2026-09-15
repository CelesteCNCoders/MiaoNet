# MiaoNet 屏幕 UI 层

`source/MiaoNet.Client/UI/` 是聊天与玩家列表的屏幕空间 UI 实现。它是一套**内建**的 retained
布局与样式层，不依赖任何外部 UI mod，也没有全局协调层。

相关文档：

- [布局与样式参数规格](../../../docs/ui-parameter-spec.md)：211 个可检验的布局与样式参数。

## 心智模型

三层，从上到下：

```
控制器（ChatListController / PlayerListController / TextEditingController）
    业务状态与输入语义；不画任何东西
        │
节点树（UINode 及其子类）            ← retained：创建一次，原地更新
    Measure / Arrange / PaintTree / HitTest
        │
画布与文本后端（IUICanvas / ITextRenderer / IUITexture）
    XNA 实现在这一层，仅此一层
```

四条必须遵守的规则：

1. **retained，不是声明式。** 节点创建一次，数据变化时改属性并调用
   `InvalidateMeasure()`；`UIRoot.SetRoot` 只在开关整块 UI 时调用，不是每帧路径。
2. **唯一的全局协调点是虚拟列表。** `VirtualListNode` 按稳定键复用条目节点；除此之外
   没有任何按类型 diff 的逻辑。
3. **布局层不得引用业务类型。** `Geometry/`、`Layout/`、`Scene/`、`Styling/`、
   `Controls/`、`Chat/`、`PlayerList/` **全部不得出现** `MiaoNetContext`、网络包类型、
   `MiaoNetModuleSettings`、`Engine`、`Color`、`MTexture`。控制器是唯一的桥。
   这条规则让整个 UI 层能被 `MiaoNet.UnitTest` 直接编译与测试。
4. **颜色与尺寸只在主题里。** 节点内不得出现字面颜色；一律引用 `MiaoNetUITheme`。

## 目录

| 目录 | 内容 |
|---|---|
| `Geometry/` | `UISize`、`UIOffset`、`UIRect`（含 `Snap()`）、`UIColor`、`EdgeInsets`、`UIAlignment`、`BoxConstraints` |
| `Scene/` | `UINode`、`SingleChildNode`、`MultiChildNode`、`UIRoot` |
| `Layout/` | `FlexNode`、`StackNode`、`AlignNode`、`BoxNode`、`SpacerNode`；`FlexAxis`、`MainAxisAlignment`、`CrossAxisAlignment` |
| `Styling/` | `UIStyle`、`TextStyle`、`TextDecoration`、`MiaoNetUITheme` |
| `Rendering/` | 接口 `IUICanvas`、`ITextRenderer`、`IUITexture`；XNA 实现 `MiaoNetUICanvas`、`MiaoNetTextRenderer`、`MiaoNetTexture`、`XnaInterop` |
| `Controls/` | `TextNode`、`RectNode`、`IconNode`、`TextFieldNode`、`ScrollNode`、`VirtualListNode`、`TextEditingController` |
| `Status/` | `StatusPanelNode`（连接状态浮层：齿轮 + 一行消息） |
| `DebugMap/` | `DebugMapOverlayNode`（关卡编辑器上的玩家标记） |
| `Input/` | 按键仲裁：`UIInputAction`、`UIFocusOwner`、`UIInputConsumer`、`UIInputFrame`、`UIInputRule`、`UIInputRouter`、`UIInputRegistrations` |
| `Chat/` | `ChatScreenNode`、`ChatMessageListNode`、`ChatMessageNode`、`ChatTabBarNode`、`ChatInputNode`、`CompletionPopupNode`、`ChatListController`、`ChatLayout` |
| `PlayerList/` | `PlayerListPanelNode`、`PlayerListChannelNode`、`PlayerRowNode`、`PlayerListController`、`PlayerListMetrics`、`PlayerListModel`、`PlayerListStyle` |

## 布局模型

约束向下、尺寸向上、父节点决定位置：

```csharp
UISize Measure(BoxConstraints constraints);   // 我在这些约束下想多大
void Arrange(UIRect bounds);                   // 父亲最终给我的矩形
```

- `BoxConstraints` 的宽度/高度各是一个区间；`Tight` 表示固定尺寸。
- `Style.Width` / `Style.Height` 会在测量前把对应轴收紧成固定值，因此"设了宽高的节点"
  等价于固定尺寸盒子。
- `UINode.Measure` 按约束缓存结果；`InvalidateMeasure()` 会逐级向上清除缓存。
- **不要手算坐标。** 位置一律通过 `FlexNode` / `StackNode` / `AlignNode` / `BoxNode`
  表达；`PlayerListMetrics` 与 `ChatScreenNode` 这类"布局节点"里出现的算术本身就是
  布局算法，属于本层职责。

## 样式

`UIStyle` 是每个节点自己的样式（不继承），包含尺寸区间、`Padding`、背景/前景/边框、
`Opacity`、字号与行高、`TextRenderer`、`PixelSnap`。

```csharp
new BoxNode
{
    Style = new UIStyle
    {
        Padding = new EdgeInsets(8f),
        Background = MiaoNetUITheme.PlayerList.Background,
    },
    Child = ...,
}
```

`MiaoNetUITheme` 收纳参数规格 §11 的全部颜色常量。**新增颜色时加在这里，不要在节点里内联。**

`TextStyle` 是可 `with` 的不可变记录，描述一次文本绘制的颜色、缩放、行高、装饰与锚点
（`HorizontalAnchor` / `VerticalAnchor`）。

## 绘制

- `IUICanvas`：`FillRect` / `DrawLine` / `PushClip` / `PopClip`。
- `ITextRenderer`：`Measure` / `Draw` / `CanRender`。XNA 实现是 `MiaoNetTextRenderer`，
  基于 `MiaoNetFont`。
- `IUITexture`：图标纹理抽象；XNA 实现是 `MiaoNetTexture`。
- `UIColor` 与游戏颜色类型互转只在 `XnaInterop` 里。

绘制遍历从根开始，自上而下：

```
PaintTree(canvas, opacity)
  → PaintSelf（本节点）
  → OnBeforeChildren（例如推入裁剪）
  → 子节点（按 Children 顺序；被 CullRectFor 判定为不可见的整棵跳过）
  → OnAfterChildren（弹出裁剪）
```

`UINode.Opacity` 是**只影响绘制**的透明度（不触发重新测量），聊天列表用它实现边缘淡出。
`Style.Opacity` 则会触发失效，适合「整体半透明」这类不常变的情况。

## 宿主：UIComponent

`source/MiaoNet.Client/Components/UIComponent.cs` 是唯一宿主，每帧：

```
Update()
  UpdatePlayerList()    按钮、滚动键、焦点、必要时重建节点树
  UpdateChat()          输入框/浮层指标、滚动输入、计时器、必要时重建快照
  EnsureHostSize()      屏幕尺寸变化时更新根节点样式
  ui.Layout(w, h)       Measure + Arrange
  SyncPlayerListAfterLayout()   玩家列表内容高度只有布局后才知道，需回夹取
Render()
  canvas.BeginFrame(); ui.Paint(canvas); canvas.EndFrame()
```

三个容易踩的点：

1. **聊天滚动偏移可以布局前算准**（内容高度 = 条数 × 行高），玩家列表不行
   （内容高度取决于实际测量结果），所以后者需要 `SyncPlayerListAfterLayout`。
2. `MiaoNetContext.renderableComponents` 的顺序是 `[dm, ui, chat]`，让 UI 先画。
3. `MiaoNetUICanvas` 的裁剪会重启 SpriteBatch（scissor 是 Begin 时捕获的状态），
   因此 `BeginFrame` / `EndFrame` 必须包住整个绘制过程。

## 新增一个节点

1. 继承 `UINode`（或 `SingleChildNode` / `MultiChildNode`）。
2. 实现 `OnMeasure`；需要摆孩子就实现 `OnArrange`；需要画东西就实现 `PaintSelf`。
3. 数据属性用带失效的 setter：

```csharp
private string label = string.Empty;

public string Label
{
    get => label;
    set
    {
        if (label == value) return;
        label = value;
        InvalidateMeasure();     // 只影响绘制时不要调用
    }
}
```

4. 颜色从 `MiaoNetUITheme` 取，坐标从布局取。
5. 把新文件加进 `MiaoNet.UnitTest.csproj` 的对应 `Compile Include` 通配（若目录已覆盖则无需改动），
   并为几何/行为写测试。

角色分工参考：

| 想做的事 | 选哪个 |
|---|---|
| 横/竖排布 | `FlexNode` |
| 层叠 | `StackNode` |
| 九宫格定位 | `AlignNode` |
| 内边距 + 背景 + 边框 | `BoxNode`（边框需要非对称时重写 `PaintBorder`） |
| 占满剩余空间 | `SpacerNode` |
| 纯色块 / 间隔 | `RectNode` |
| 一行文字 | `TextNode` |
| 图标 | `IconNode` |
| 长列表 | `VirtualListNode` |
| 可滚动视口 | `ScrollNode` |

## 键盘归属

输入已集中管理。完整规格（含逐条约束）见
[按键输入消费规格](../../../docs/ui-input-spec.md)。

```text
MInput / Mouse / 设置项按键
   ↓  UIInput/MiaoNetUIInputAdapter.Poll()    唯一读取点
  UIInputFrame
   ↓  UI/Input/UIInputRouter.Route(frame)     唯一仲裁点，按焦点裁定
   ├─→ 按住电平：消费者用 IsHeld 读
   └─→ 边沿反应：消费者注册的 handler（组件更新之前）
   ↓
  ChatComponent / UIComponent
```

- **焦点归属**由 `UIFocusOwner`（`None` / `Chat` / `PlayerList`）显式持有，
  开关面板时调用 `MiaoNetContext.UIInput.SetFocus(...)`。
  `MiaoNetContext.HasComponentFocus` 只是它的只读投影。
- **`UI/Input/` 是 XNA-free 的**，已加入 `MiaoNet.UnitTest` 的编译集合，
  因此仲裁规则有单测覆盖；`UIInput/` 里的适配器才碰 `MInput`。
- **消费者不读输入绑定，也不在 `Update` 里判键。** 需要新按键时，三处：
  1. 在 `UIInputAction` 加动作；
  2. 在 `MiaoNetUIInputAdapter.Poll` 把物理键翻译成该动作；
  3. 在 `UIInputRouter.Rules` 加一行（归属 + 生效焦点），并在对应组件的构造函数里
     `Register(consumer).On(action, handler)` 认领。

  漏掉第 3 步不会安静地过去：`Seal()` 在启动时抛异常，
  `UiInputOwnershipTests.Components_ClaimEveryRoutedAction` 在构建期就会失败。

  **不要在组件里读设置项绑定**（`settings.ChatButton.Pressed` 之类），这条是会静默失效的
  陷阱：适配器在 `Poll` 里就 `ConsumePress()` 了这些绑定，而 `Poll` 在任何组件之前运行，
  所以组件读到**永远是 false**，功能直接消失且不报错——按 `T` 打不开聊天就是这么来的。
  由 `UiInputOwnershipTests` 在源码层面守卫（它会扫出对适配器拥有的绑定的
  `.Pressed` / `.Check` / `.ConsumePress` 读取）。
- **「按住」是电平，不是事件。** 需要按住状态的用 `OwnHeld(action)` 认领后轮询 `IsHeld(action)`；
  给电平硬套一个每帧重置的假 handler 只是把簿记换个地方写。玩家列表的 Hold 模式、
  玩家列表滚动、PageUp/PageDown 翻页都属于这一类。
- **注册句柄按消费者共享，不按类。** 聊天的输入框（`ChatComponent`）和消息列表
  （`UIComponent`）是同一个 `UIInputConsumer.Chat`，所以两者 `Register` 拿到同一个句柄——
  路由器是按消费者仲裁的。
- **一个反应如果改变了焦点，本帧后续反应不再执行。** 由 `Route` 统一保证，
  不需要各消费者自己记得写。
- `Tab` 是唯一同帧双消费者的键（玩家列表开关 + 补全接受），
  由路由表按焦点裁定，不依赖守卫条件。

## 客户端侧节点

`UI/` 目录下的核心必须 XNA-free（见下方「测试」），但**节点类型本身是扩展点**：
需要 Monocle 细节的绘制可以定义一个客户端侧节点，放在 `Components/` 下，
照样继承 `UINode`、照样参与 retained 树、位置照样由父节点布局决定。

现有例子：`StatusCogwheelNode`。它的描边旋转需要 `MTexture` 的 `ScaleFix` / `ClipRect` /
`Center` / `DrawOffset`，这些不该泄漏进核心抽象，所以在 `PaintSelf` 里直接用
`Draw.SpriteBatch`。判断标准是**这个节点需要游戏类型吗**，而不是**它在哪个目录**。

反过来，只要一个节点可以只用 `IUICanvas` / `ITextRenderer` / `IUITexture` 表达，
它就应该放进 `UI/` 以便单测覆盖——`DebugMapOverlayNode` 就是这样：
相机变换留在组件里（那是适配器职责），节点只接收算好的屏幕坐标。

## 测试

`MiaoNet.UnitTest` 通过 `Compile Include` 直接编译 UI 源文件，因此：

- **新增任何 UI 源文件时，确认它不引入 XNA / Celeste 类型**，否则测试项目编译失败。
  这条约束是刻意的：它把"UI 核心可脱离游戏测试"变成编译器强制的事情。
- 唯一例外是 `Rendering/` 下的 XNA 实现，测试项目只包含三个接口文件。
- 布局与几何断言用注入的假 `ITextRenderer`（固定字宽）驱动，不要依赖真实字体。
