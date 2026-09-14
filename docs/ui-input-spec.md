# MiaoNet UI 按键输入模型

> 用途：描述当前客户端中 UI 按键如何被读取、归属与消费。
> 配套：[布局与样式参数规格](ui-parameter-spec.md)、[客户端 UI 层](../source/MiaoNet.Client/docs/ui.md)

---

## 1. 生效门控

| 门控 | 定义位置 | 作用 |
|---|---|---|
| `HasConnection` | `MiaoNetContext.Update` | 为假时 `components.ForEach(Update)` 不执行 → **所有** MiaoNet 按键失效 |
| `active`（`ChatComponent`） | 聊天组件 | 打开聊天输入框；与 `UiFocusOwner.Chat` 同步 |
| `IsSuitableToOpenUI` | `MiaoNetContext` | 打开任一 UI 的前置条件；**不**门控已打开的 UI 的按键 |
| `HasComponentFocus` | `MiaoNetContext` | `UiInput.HasFocus` 的只读投影：为真时 `IsSuitableToOpenUI` 为假 |
| `playerList.IsOpen` | `UIComponent` | 玩家列表与聊天列表滚动的互斥；也为假时 `PageUp`/`PageDown`/滚轮归聊天列表 |
| `settings.Fireworks` + `!level.Paused` + 冷却 | `MainComponent` | 烟花按键 |
| `settings.EnableEmoteWheel` + `MInput.ControllerHasFocus` | `EmoteComponent` | 手柄接管时才启用表情轮盘 |

### `IsSuitableToOpenUI` 的完整条件

以下**全部**成立才为真（`MiaoNetContext.IsSuitableToOpenUI`）：

1. 场景中没有 `KeyboardConfigUI` 或 `ButtonConfigUI`
2. 若场景是 `Overworld`，当前不是 `OuiFileNaming`，也不是 `Celeste.Mod.UI.OuiModOptionString`
3. 场景中没有 `TextMenuExt.Modal { Visible: true }`
4. 场景不是 `LevelLoader`（避免传送中打开）
5. `!HasComponentFocus`
6. 若场景是 `Level`，其 `Overlay` 为 null

> 第 5 条使 `HasComponentFocus` 成为「已打开的 UI」对「再打开一个 UI」的互斥。它由输入层的
> 焦点归属派生，不再由各组件自行维护。

---

## 2. 按键表

「吞噬」列说明该输入是否被标记为已消费，以及用什么手段。

### 2.1 聊天：打开路径（`UiFocusOwner.None`）

| 动作 | 绑定来源 | 默认键 | 行为 | 吞噬 |
|---|---|---|---|---|
| `ChatToggle` | `settings.ChatButton` | `T` | `IsSuitableToOpenUI` 为真时打开聊天输入框 | 适配器**无条件** `ConsumePress()`——即使场景不允许打开也已被吞掉 |
| `ChatCommandToggle` | `settings.ChatCommandButton` | 未绑定 | 同上，并预填 `/` | 同上 |

> 两者是各自独立的设置项绑定，不互斥。

### 2.2 聊天：编辑路径（`UiFocusOwner.Chat`）

进入此焦点后 `Engine.Scene.Paused = true`。

| 动作 | 物理输入 | 行为 | 吞噬 |
|---|---|---|---|
| `Cancel` | `Esc`（硬编码） | 关闭聊天 | `MInputHack.ConsumeAllInputs()`（吞掉**全部**虚拟按键，含玩家列表键） |
| `Submit` | `Enter`（硬编码） | 提交：非空则入历史 → 命令走 `HandleCommand`，否则 `SendChat`（LiveMode 下改为报错）；随后关闭 | `MInputHack.ConsumeAllInputs()` |
| `ChannelPrevious` / `ChannelNext` | `Shift` + `Left` / `Right` | 切换频道标签，并同步 `Settings.ChatChannel` | 否（只读） |
| `HistoryUp` / `HistoryDown` | `Up` / `Down`（原始边沿，不重复） | 浏览输入历史（首尾夹取，不循环）；补全弹窗开启时不动作 | 否 |
| `CompletionUp` / `CompletionDown` | 重复虚拟按键 `Up` / `Down`（0.4s / 0.05s） | 上/下选择补全候选（**循环**）；无候选时不动作 | 适配器 `ConsumePress()` |
| `CaretLeft` / `CaretRight` | 重复虚拟按键 `Left` / `Right`（0.4s / 0.05s） | 光标左/右移 | 适配器 `ConsumePress()` |
| `CompletionAccept` | `Tab`（硬编码） | 接受补全：仅当候选恰好 1 个、或已有选中项 | 否（**未** `ConsumePress`） |
| `Paste` | `Ctrl` + `V`（硬编码） | 粘贴：过滤控制字符 → 按 `MaxTextLength` 截断 | 否 |
| 字符输入 | `TextInput.OnInput` 事件 | 可打印字符与 `CanRender` 过滤；控制字符 `8`/`2`/`3`/`127` = Backspace/Home/End/Delete | — |
| IME 组合 | `TextInputEXT.TextEditing` 事件 | 组合文本、起点、长度 | — |

> 字符输入与 IME 由 `Activate` 订阅、`Deactivate` 退订，随焦点 `Chat` 的取得与释放同步。

### 2.3 玩家列表与聊天列表滚动

| 动作 | 绑定来源 | 默认键 | 生效焦点 | 行为 | 吞噬 |
|---|---|---|---|---|---|
| `PlayerListToggle` | `settings.PlayerListButton` | `Tab` | `None` / `PlayerList` | `Press` 模式按下切换，`Hold` 模式按住显示 | 适配器 `ConsumePress()`（仅 `Press` 边沿） |
| `PlayerListScrollUp` / `PlayerListScrollDown` | 设置项 | `Up` / `Down` | `PlayerList` | 按住滚动列表，1024 px/s | 电平 |
| `ChatListScrollUp` / `ChatListScrollDown` | `PageUp` / `PageDown`（硬编码） | | `None` / `Chat` | 按住滚动聊天消息列表 ±1024 px/s；两者同按 `PageUp` 优先 | 电平 |
| 鼠标滚轮 | `Mouse.GetState()`（绕过 MInput） | — | `None` / `Chat` | 聊天消息列表滚动；焦点为 `PlayerList` 时增量被丢弃 | — |

> `PageUp`/`PageDown`/滚轮不要求聊天激活：走路时也作用于聊天列表。

### 2.4 未纳入统一输入层的消费者

以下消费者仍各自读取输入，不受路由表与焦点归属管辖。

| 输入 | 绑定来源 | 默认键 | 消费者 | 生效场景 | 行为 | 吞噬 |
|---|---|---|---|---|---|---|
| `CreateFireworksButton` | 设置项 | 未绑定 | `MainComponent` | `HasConnection && settings.Fireworks && !level.Paused && 冷却结束` | 创建烟花并发包 | `MInputHack.ConsumeAllInputs()` |
| `EmoteButtons[i]` | 设置项（列表） | `1`..`8`（超出为未绑定） | `EmoteComponent` | `HasConnection` 且关卡内有 `Player` | 发送第 i 个表情 | `ConsumePress()` |
| `EmoteWheelSendEmote` | 设置项 | 右摇杆 | `EmoteWheel`（由 `EmoteComponent` 创建） | 同上 `&& settings.EnableEmoteWheel && MInput.ControllerHasFocus` | 轮盘选中并发送 | — |
| 鼠标左/中/右键 | `MInput.Mouse` | | `GroupPhotoPlatform` | 平台实体存在时 | 合影平台拖拽/旋转等 | — |

### 2.5 按键绑定的注册方式

`MiaoNetModule.OnInputInitialize` 对 `Settings.GetButtonBindings()` 逐个：

```csharp
buttonBinding.Button = new VirtualButton(buttonBinding.Binding, Input.Gamepad, 0.08f, 0.2f);
buttonBinding.Button.AutoConsumeBuffer = true;
```

`OnInputDeregister` 逐个 `Deregister()`。因此所有设置项按键都是**全局虚拟按键**，与 UI 焦点无关；
焦点只决定这些虚拟按键产生的动作是否被投递。

---

## 3. 路由表：动作 → 消费者

`UiInputRouter.Rules` 只声明「这个动作归谁、在哪些焦点下生效」，不写消费者的代码。
「认领它的类」是实现侧的对端。

| 动作 | `None` | `Chat` | `PlayerList` | 认领它的类 |
|---|---|---|---|---|
| `ChatToggle` / `ChatCommandToggle` | chat | — | — | `ChatComponent` |
| `PlayerListToggle`（按下与按住） | 玩家列表 | — | 玩家列表 | `UIComponent` |
| `PlayerListScrollUp/Down`（按住） | — | — | 玩家列表 | `UIComponent` |
| `Submit` / `Cancel` / `Channel*` / `History*` / `Completion*` / `Caret*` / `Paste` | — | chat | — | `ChatComponent` |
| `ChatListScrollUp/Down`（按住） | chat | chat | — | `UIComponent`（挂在 chat 消费者上） |

「—」既表示未投递，也表示该输入已被消费掉，不会落给别的消费者。

`ChatListScrollUp/Down` 的消费者是 chat，但认领它的是 `UIComponent`——因为真正驱动消息列表
滚动的是 `UIComponent`。注册句柄按**消费者**而非按类共享，所以两者拿到的是同一个
`UiInputRegistrations`；这也是"注册是模块化"与"注册是按类划分"的区别所在。

---

## 4. 焦点归属

| 开着的是 | `UiFocusOwner` |
|---|---|
| 都没开 | `None` |
| 聊天输入框 | `Chat` |
| 玩家列表 | `PlayerList` |

焦点由输入层**显式持有**，而不是散落的可写标志：它由「实际开着什么」决定，因此不会像可写
标志那样被遗忘而卡住。

- `UiInput.SetFocus(...)` 在打开与关闭时由组件调用：`ChatComponent.Activate`/`Deactivate`
  设置 `Chat`/`None`，`UIComponent` 在玩家列表开关时设置 `PlayerList`/`None`。
- 断线时由 `MiaoNetContext` 复位为 `None`；`UIComponent.OnDisconnected` 在列表开着时同样显式释放。
- `MiaoNetContext.HasComponentFocus` 是 `UiInput.HasFocus` 的**只读投影**，不再可写。

---

## 5. 注册机制与不变量

消费者在构造时通过 `Register` 认领路由表给它的动作；注册是幂等的，同一个消费者拿到同一个句柄。

```csharp
input = context.UiInput.Register(UiInputConsumer.Chat);   // 幂等：同一个消费者拿到同一个句柄
input.On(UiInputAction.Cancel, CancelEditing);            // 边沿反应
input.On(UiInputAction.Submit, SubmitEditing);
// "按住"是电平而非事件，因此认领后轮询，而不是配一个每帧重置的假反应
chatInput.OwnHeld(UiInputAction.ChatListScrollUp);
... if (chatInput.IsHeld(UiInputAction.ChatListScrollUp)) delta += ...;
```

**边沿 vs 按住**：

- 边沿用 `On(action, handler)` 注册反应；反应在组件更新之前、按注册顺序执行。
- 按住是电平而非事件：`OwnHeld(action)` 只认领，消费者在自己的 `Update` 里用 `IsHeld(action)`
  轮询。若把按住做成反应，就需要每帧重置的记账才能回答「是否还按着」。
- `IsHeld` 对未 `OwnHeld` 的动作抛异常；投递时已经套用了路由表的焦点条件，调用点无需再守卫。

**`Seal()` 不变量**：`MiaoNetContext` 在全部组件构造完成后调用一次 `Seal()`，违反即抛出而不是静默失效：

| 违反 | 后果 | 检测 |
|---|---|---|
| 某个动作被路由但无人认领 | 按键静默失效 | `Seal` 报 `nothing claimed it` |
| 认领了不属于自己的动作 | 两个模块抢同一个键 | `Claim` 立即抛出 |
| 同一个动作反应注册两次 | 一次按键执行两次 | `Claim` 立即抛出 |
| 路由表里同一动作出现两行 | 动作落到两个消费者 | 静态构造 `BuildIndex` 抛出 |

**焦点交接**：一个反应如果改变了焦点（`Activate` / `Deactivate` / 开关玩家列表），`Route` 当场
结束本帧的后续反应。关闭聊天输入框不会顺带提交或翻页。

**优先级**：`Route` 在组件更新之前一次性裁定，与 `components` 数组顺序无关。

---

## 6. 如何新增一个按键

1. 在 `UiInputAction` 加一个具名动作。
2. 在 `UiInputRouter.Rules` 加一行：动作 → 消费者 + 生效焦点列表（一个动作只能有一行）。
3. 在 `MiaoNetUiInputAdapter.Poll` 里把物理输入翻译成 `frame.Press(...)` 或 `frame.Hold(...)`，
   并决定是否 `ConsumePress()`。
4. 在消费者的构造里认领：`Register(consumer).On(action, ...)` 或 `.OwnHeld(action)`；
   新的消费者需要自己的 `Register`。
5. 启动时 `Seal()` 会校验路由表与认领一致；模型遗漏会在启动时抛出，而不是按键静默失效。

---

## 7. 冲突与优先级矩阵

路由表保证一个动作只到一个消费者，焦点列表则决定同一物理键映射出的多个动作互斥。

| 按键 | 动作 A | 动作 B | 如何避免冲突 | 脆弱度 |
|---|---|---|---|---|
| `Tab` | `CompletionAccept`（`Chat`） | `PlayerListToggle`（`None` / `PlayerList`） | 生效焦点互斥，同一帧只有一个动作被投递 | 低 |
| `Up` / `Down` | `History*` / `Completion*`（`Chat`） | `PlayerListScroll*`（`PlayerList`） | 生效焦点互斥 | 低 |
| `Esc` / `Enter` | `Cancel` / `Submit`（`Chat`） | — | 仅聊天焦点生效 | 低 |
| `PageUp` / `PageDown` / 滚轮 | `ChatListScroll*`（`None` / `Chat`） | — | 只看焦点是否为 `PlayerList`，与聊天是否激活无关 | 中 |
| `T` | `ChatToggle`（`None`） | — | 玩家列表打开时焦点为 `PlayerList`，不会投递；场景不允许时 `IsSuitableToOpenUI` 挡住打开 | 低 |

---

## 8. 已知的审计盲区

- `EmoteWheel` 内部的手柄输入细节未展开（只记录了它的创建条件与用途）。
- `GroupPhotoPlatform` 的鼠标交互细节未展开。
- 手柄映射（`Input.Gamepad` = 0）对所有设置项按键生效，但默认绑定里的手柄键只有 `EmoteWheelSendEmote`；其余为键盘或未绑定。
