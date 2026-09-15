# MiaoNet UI 布局与样式参数规格

> 本文档描述 MiaoNet 当前 UI 的布局与样式参数：每个参数有唯一 ID，给出当前取值、公式与单位。
> 参数按区域分组，`## 12` 给出参数组到承载节点的映射。
> 公式中的输入量：`s` 为缩放值，`lh` 为缩放后行高，`H`/`W` 为逻辑屏幕高/宽，`dt` 为帧时间。

---

## 0. 使用方式

1. **参数 ID**：每个参数有唯一 ID，在本文档与代码中一致；按 ID 定位与修改。
2. **取值**：表格中的"值/公式"即当前实现的取值，是唯一事实来源；节点与主题按 ID 取值，不另立常量。
3. **公式输入**：`s`、`lh`、`H`、`W`、`dt` 等由运行期提供；单测中以注入值驱动，不依赖真实字体。
4. **变更**：新增或调整参数时，同时更新对应表格与 `## 12` 的承载映射。

---

## 1. 全局约定

### 1.1 坐标系与锚点

| ID | 参数 | 值 |
|---|---|---|
| `GLOBAL.ORIGIN` | 屏幕原点 | 左上角，+X 右、+Y 下（`Engine.ScreenMatrix`） |
| `GLOBAL.VIEW` | 逻辑尺寸 | `Engine.Width` × `Engine.Height` |

**文本锚点语义**（`justify`，只用到 4 种组合）：

| ID | `justify` | 含义 | 影响 |
|---|---|---|---|
| `GLOBAL.ANCHOR.TL` | `Vector2.Zero` = (0,0) | 左对齐、顶部贴 `y` | 玩家名、频道头、状态图标 |
| `GLOBAL.ANCHOR.BL` | `(0,1)` / `Vector2.UnitY` | 左对齐、**底部**贴 `y` | 聊天正文、标签文字、输入框文本、补全项 |
| `GLOBAL.ANCHOR.RT` | `Vector2.UnitX` = (1,0) | **右**对齐、顶部贴 `y` | 玩家列表右侧整列 |
| `GLOBAL.ANCHOR.LC` | `(0,0.5)` | 左对齐、**垂直居中** | 折叠计数 |

聊天区大量使用"底部贴 `y`"的锚点；若按顶部对齐渲染，所有 y 值都会平移一个 `lh`。对应实现为 `VerticalAnchor.Bottom` 或显式的行高补偿。

### 1.2 缩放

| ID | 参数 | 公式 |
|---|---|---|
| `GLOBAL.SCALE` | 缩放值 | `s = 0.25 * 3.2^t`，`t = clamp((v-1)/19, 0, 1)`，`v ∈ [1,20]` 整数设置值 |

由 `UIScale.FromSetting` 计算；玩家列表与聊天各用一份缩放值（`PlayerListUIScaleValue` / `ChatUIScaleValue`）。

参考值表：

| v | 1 | 4 | 6 | 10 | 20 |
|---|---|---|---|---|---|
| s | 0.25000 | 0.30040 | **0.33953** | 0.43373 | 0.80000 |

### 1.3 行高

| ID | 参数 | 值 |
|---|---|---|
| `GLOBAL.L` | 字行高 | `ENZhsLineHeight`（运行期由字体决定，无编译期常量） |
| `GLOBAL.LH` | 缩放后行高 | `lh = L * s` |

> `lh` 是聊天区几乎所有布局的基本单位。单测中应以注入的 `lh` 作为输入参数，不得硬编码。

### 1.4 像素对齐

| 区域 | 规则 |
|---|---|
| 聊天消息背景 | **对齐到像素**：`xi=floor(x)`，`yi=floor(y)`，`wi=floor(x+w)-xi`，`hi=floor(y+h)-yi` |
| 标签栏背景 | `floor(x)`、`floor(y-lh)`、`floor(w)` |
| 输入框 / 补全 / 玩家列表 | **不对齐**，使用浮点坐标 |

| ID | 参数 | 值 |
|---|---|---|
| `GLOBAL.SNAP.CHAT_MSG` | 消息背景像素对齐 | 是（按上述四式） |
| `GLOBAL.SNAP.CHAT_TAB` | 标签背景像素对齐 | 是 |
| `GLOBAL.SNAP.INPUT` | 输入框像素对齐 | 否 |
| `GLOBAL.SNAP.COMPLETION` | 补全浮层像素对齐 | 否 |
| `GLOBAL.SNAP.PLAYLIST` | 玩家列表像素对齐 | 否 |

由 `UIStyle.PixelSnap` 控制：默认关，聊天消息与标签栏打开。统一开启或统一关闭会改变外观。

---

## 2. 设置 → 参数映射

| 设置 | 范围 | 默认 | 归一化 | 影响参数 |
|---|---|---|---|---|
| `ChatUIScale` | 1–20 | 6 | `GLOBAL.SCALE` | 聊天全部尺寸 |
| `PlayerListUIScale` | 1–20 | 6 | `GLOBAL.SCALE` | 玩家列表全部尺寸 |
| `ChatMessagePadding` | 0–8 | 4 | 整数 | `CHAT.MSG.PAD_Y` |
| `ChatBackgroundOpacity` | 0–10 | 8 | `/10` | `CHAT.MSG.BG_ALPHA` |
| `ChatTextOpacity` | 0–10 | 10 | `/10` | `CHAT.MSG.TEXT_ALPHA` |
| `ChatDisplayDuration` | 1–12 | 8 | 整数→秒 | `CHAT.ANIM.SHOW_DURATION` |
| `IdleChatHeight` | 1–10 | 4 | `/10` | `CHAT.LIST.IDLE_RATIO` |
| `ActiveChatHeight` | 1–10 | 8 | `/10` | `CHAT.LIST.ACTIVE_RATIO` |
| `FoldWindowSeconds` | 1–60 | 10 | 整数 | 折叠窗口（非布局） |
| `FancyFoldCounter` | bool | true | — | `CHAT.ANIM.COUNTER_FANCY` |
| `MessageFolding` | bool | — | — | 折叠总开关 |
| `NewMessagesShowing` | enum | `ShowAll` | — | `CHAT.LIST.SOURCE_MODE` |
| `PlayerListMapNameClipType` | enum | `None` | — | `PL.ROW.CLIP_TYPE` |
| `PlayerListButtonMode` | enum | — | — | 面板开关行为（非布局） |

设置变更经 `Settings_SettingsChanged` 统一推送到 UI 节点（聊天在 `UIComponent.UpdateChat`，玩家列表在 `RebuildPlayerListIfNeeded`）；节点不各自读取设置。

---

## 3. 聊天消息行

所有行的 `y` 是**该行底部参考线**。

| ID | 参数 | 值/公式 |
|---|---|---|
| `CHAT.MSG.PAD_X` | 左右内边距 | `8` |
| `CHAT.MSG.PAD_Y` | 上下内边距 | `messageYPadding` = `ChatMessagePadding` 设置 |
| `CHAT.MSG.LINE_H` | 行高 | `mh = lh + 2 * messageYPadding` |
| `CHAT.MSG.BG_X` | 背景左边界 | `x` |
| `CHAT.MSG.BG_Y` | 背景上边界 | `y - mh`（**底部锚定**） |
| `CHAT.MSG.BG_W` | 背景宽 | `lineWidth + 2 * 8` |
| `CHAT.MSG.BG_H` | 背景高 | `mh` |
| `CHAT.MSG.TIME_RATIO` | 时间列宽系数 | `3.5625`（"00:00:00" 宽度 / `lh`，固定系数而非运行期测量） |
| `CHAT.MSG.TIME_PAD_X` | 时间文字左内边距 | `2` |
| `CHAT.MSG.TIME_W` | 时间列宽 | `3.5625 * lh + 2 * 2` |
| `CHAT.MSG.COUNTER_GAP` | 计数间隔 | `4 * s` |
| `CHAT.MSG.RIGHT_PAD` | 背景右内边距 | 已含在 `BG_W`（左右各 8） |

**`lineWidth` 推导（顺序不可变）**

```
messageWidth = Σ measure(segment.Text).X            // 全部段落，整行
lineWidth    = messageWidth + (有时间戳 ? TIME_W : 0)
if RepeatCount > 1:
    counterScale  = FoldCounter.GetScale(n) + FoldCounter.GetPopScale(Ease.ElasticOut(t))
    counterShake  = FoldCounter.GetShakeAmplitude(n) * s
    lineWidth    += COUNTER_GAP + measure("X{n}").X * counterScale + counterShake
```

> 计数宽度用「弹出缩放 + 最大抖动」测量，保证背景始终包住计数（含抖动随机偏移）。

**文字位置（同样底部锚定）**

| ID | 参数 | 值 | 锚点 |
|---|---|---|---|
| `CHAT.MSG.TEXT_ORIGIN` | 文字起点 | `(x + 8, y - messageYPadding)` | — |
| `CHAT.MSG.TIME_POS` | 时间文字 | `(x + 8 + 2, y - messageYPadding)` | `BL` |
| `CHAT.MSG.BODY_X` | 正文起点 | `x + 8 + (有时间戳 ? TIME_W : 0)` | `BL` |
| `CHAT.MSG.COUNTER_POS` | 计数 | `(BODY_X + messageWidth + COUNTER_GAP, y - mh * 0.5) + shake` | `LC` |

抖动：两轴各 `(Random.NextSingle()*2 - 1) * counterShake`。

**透明度**

| ID | 参数 | 值 |
|---|---|---|
| `CHAT.MSG.ALPHA` | 行淡出系数 | `fade = active ? 1 : state.FadeOut`，再乘部分可见 alpha |
| `CHAT.MSG.BG_ALPHA` | 背景 | `fade * ChatBackgroundOpacityValue` |
| `CHAT.MSG.TEXT_ALPHA` | 文字 | `fade * ChatTextOpacityValue` |

---

## 4. 聊天列表视口

| ID | 参数 | 值/公式 |
|---|---|---|
| `CHAT.LIST.MARGIN` | 屏幕边距 | `16` |
| `CHAT.LIST.PADDING` | 内边距 | `8` |
| `CHAT.LIST.INPUT_TOP` | 输入框顶 | `H - 16 - lh - 16` |
| `CHAT.LIST.TAB_TOP` | 标签栏顶 | `INPUT_TOP - lh - 16` |
| `CHAT.LIST.BASE_Y` | 消息基线 | `active ? TAB_TOP : INPUT_TOP` |
| `CHAT.LIST.BASE_X` | 消息左边界 | `16` |
| `CHAT.LIST.IDLE_RATIO` | 空闲高度比例 | `IdleChatHeight/10`，默认 `0.4` |
| `CHAT.LIST.ACTIVE_RATIO` | 激活高度比例 | `ActiveChatHeight/10`，默认 `0.8` |
| `CHAT.LIST.MAX_H` | 可见高度 | `v = ratio * BASE_Y`；`maxH = floor(v / mh) * mh` |
| `CHAT.LIST.TOP` | 裁剪上边界 | `BASE_Y - maxH` |
| `CHAT.LIST.SOURCE_MODE` | 数据源选择 | `active⇒ActiveChatLog`；否则 `WithTab⇒ActiveChatLog`、`ShowAll⇒ChatLog`、其余空 |

**绘制顺序（自下而上）**

```
curY = BASE_Y + (active ? scroll : 0)
1) 找 firstVisible：从最后一条向前，若 curY > BASE_Y 则 curY -= mh 并跳过
2) 若 firstVisible+1 < count：以 alpha = 1 - (curY+mh - BASE_Y)/mh 画该条（底部淡出）
3) 从 firstVisible 向前逐条画 alpha=1，直到 curY < BASE_Y - maxH
4) 若越界条 index > 0：以 alpha = 1 - (BASE_Y - maxH - curY)/mh 画该条（顶部淡出）
```

| ID | 参数 | 值 |
|---|---|---|
| `CHAT.LIST.FADE_TOP` | 顶部淡出 | 1 条，按上式线性 |
| `CHAT.LIST.FADE_BOTTOM` | 底部淡出 | 1 条，按上式线性 |
| `CHAT.LIST.CLIP` | 裁剪 | **仅通过 alpha 与循环边界实现，无 scissor/裁剪矩形** |

**滚动**

| ID | 参数 | 值 |
|---|---|---|
| `CHAT.SCROLL.KB_SPEED` | 键盘滚动 | `1024` px/s；PageUp `+`，PageDown `−` |
| `CHAT.SCROLL.RANGE` | 范围 | `[0, max(0, count * mh - maxH)]`，其中 `count` 是**当前显示列表**的条数（打开时或 `WithTab` 模式为当前 tab 的 log；`ShowAll` 且未打开时为全量 log；否则为空），`maxH = floor(maxHeightV / mh) * mh` |
| `CHAT.SCROLL.SMOOTH` | 平滑 | `maxMove = max(|target - scroll|, 8) * 8 * dt`，`scroll = Approach(scroll, target, maxMove)` |
| `CHAT.SCROLL.RESET` | 关闭时 | `target = scroll = 0`（发生在 `Active` 由真变假的那一刻） |

> 滚动夹取与渲染共用同一个 `mh = lh + 2 * MessageYPadding`，`MessageYPadding` 只来自 `ChatMessagePadding` 设置；否则设置非 8 时滚动范围与实际内容高度不一致。

**淡出与计时**（`CHAT.ANIM.*`）

| ID | 参数 | 值 |
|---|---|---|
| `CHAT.ANIM.SHOW_DURATION` | 停留 | `ChatDisplayDuration` 秒，默认 `8` |
| `CHAT.ANIM.DISAPPEAR` | 消失时长 | `0.25` 秒，`FadeOut -= (1/0.25)*dt` |
| `CHAT.ANIM.ACTIVE_OVERRIDE` | 激活时 | `fade` 强制 `1`，不淡出 |
| `CHAT.ANIM.COUNTER_POP` | 计数弹出 | `FoldCounter.PopDuration = 0.5`；新折叠行初始 `CounterPopTimer = 0.5` |
| `CHAT.ANIM.COUNTER_FANCY` | 动画开关 | `FancyFoldCounter` |

**折叠计数数学**（`FoldCounter`，纯函数）

| ID | 公式 |
|---|---|
| `ANIM.COUNTER.SCALE` | `count<=1 ? 1 : min(1 + 0.12*(count-1), 2.2)` |
| `ANIM.COUNTER.POP` | `progress>=1 ? 0 : 0.4*(1-progress)` |
| `ANIM.COUNTER.SHAKE` | `count<3 ? 0 : min(0.5*(count-2), 6)` |
| `ANIM.COUNTER.COLOR` | `count<=20`：`(1, 1-r, 1-r)`，`r=clamp((count-2)/18,0,1)`；否则 HSV 彩虹，周期 `3s` |

---

## 5. 标签栏

| ID | 参数 | 值 |
|---|---|---|
| `CHAT.TAB.MARGIN` | 屏幕边距 | `16` |
| `CHAT.TAB.PADDING` | 内边距 | `8` |
| `CHAT.TAB.INPUT_TOP` | 输入框顶 | `H - 16 - lh - 16` |
| `CHAT.TAB.BASE` | 基线 | `(16, INPUT_TOP - 8)` |
| `CHAT.TAB.WIDTH` | 单标签宽 | `measure(title).X + 16` |
| `CHAT.TAB.HEIGHT` | 单标签高 | `lh` |
| `CHAT.TAB.GAP` | 标签间隔 | `2` |
| `CHAT.TAB.BG_ACTIVE` | 选中背景 | `Black * 0.5` |
| `CHAT.TAB.BG_IDLE` | 未选中背景 | `Black * 0.15` |
| `CHAT.TAB.TEXT_ACTIVE` | 选中文字 | `White * 1.0` |
| `CHAT.TAB.TEXT_IDLE` | 未选中文字 | `White * 0.5` |
| `CHAT.TAB.FIRST` | 首个标签 | `InitialTabTitle`（本地化"全局"） |
| `CHAT.TAB.ORDER` | 顺序 | 先 `InitialTabTitle`（index `-1`），再 `TabNameList` |

标签列**像素对齐**（见 `GLOBAL.SNAP.CHAT_TAB`）。渲染顺序：消息列表 → 标签栏（`active` 时）。

---

## 6. 输入框

| ID | 参数 | 值 |
|---|---|---|
| `CHAT.INPUT.MARGIN` | 屏幕边距 | `16` |
| `CHAT.INPUT.PADDING` | 内边距 | `8` |
| `CHAT.INPUT.BASE` | 基线（底） | `(16, H - 16)` |
| `CHAT.INPUT.TEXT_ORIGIN` | 文本原点 | `(24, H - 24)` |
| `CHAT.INPUT.HEIGHT` | 高度 | `lh + 16` |
| `CHAT.INPUT.BG_RECT` | 背景 | `(16, H - 16 - height, W - 32, height)` |
| `CHAT.INPUT.BG_ALPHA` | 背景色 | `Black * (0x7f/255)` ≈ `0.4980` |
| `CHAT.INPUT.TEXT` | 正文 | `White`，锚点 `BL` |
| `CHAT.INPUT.IME_TEXT` | IME 组合文字 | `Color.Gray`，紧随光标前文本 |
| `CHAT.INPUT.CARET_W` | 光标线宽 | `2` |
| `CHAT.INPUT.CARET_COLOR` | 光标色 | `White` |
| `CHAT.INPUT.CARET_BLINK` | 闪烁间隔 | `0.5` 秒 |
| `CHAT.INPUT.CARET_POS` | 光标 x | `TEXT_ORIGIN.X + measure(TextBeforeCaret).X (+ IME 前缀)` |
| `CHAT.INPUT.CARET_Y` | 光标范围 | `(x, TEXT_ORIGIN.Y)` → `(x, TEXT_ORIGIN.Y - lh)` |
| `CHAT.INPUT.MAX_LEN` | 最大长度 | `64` |

**行为参数（非几何，但属同一契约）**

| ID | 值 |
|---|---|
| `CHAT.INPUT.KEY_REPEAT` | 左右/上下 `SetRepeat(0.4, 0.05)` |
| `CHAT.INPUT.TAB_ACCEPT` | 仅当补全数 `==1` 或已有选中项时接受 |
| `CHAT.INPUT.TAB_CLAMP` | 接受时按 `MaxTextLength` 截断内容 |
| `CHAT.INPUT.PASTE` | Ctrl+V，过滤控制字符，按 `MaxTextLength` 截断 |
| `CHAT.INPUT.CHAR_FILTER` | 仅接受 `CanRender(chr)` 且 `长度 < MaxTextLength` |
| `CHAT.INPUT.CONTROL_CHARS` | `8`=Backspace、`2`=Home、`3`=End、`127`=Delete |
| `CHAT.INPUT.IME_RECT` | 按 `Engine.ViewWidth/Height` 相对 `Engine.Width/Height` 换算，宽 `max(1, imeW*xScale)`。候选窗水平位置在部分输入法下不准确，属已知限制 |

---

## 7. 补全浮层

| ID | 参数 | 值 |
|---|---|---|
| `CHAT.COMPL.PADDING` | 内边距 | `4` |
| `CHAT.COMPL.BASE` | 浮层原点 | `(INPUT.TEXT_ORIGIN.X + measure(before).X, INPUT.TEXT_ORIGIN.Y - lh - 8)` |
| `CHAT.COMPL.TEXT_ORIGIN` | 文本原点 | `BASE + (4, -4)` |
| `CHAT.COMPL.WIDTH` | 宽 | `max(measure(item.Display).X) + 8` |
| `CHAT.COMPL.HEIGHT` | 高 | `lh * count + 8` |
| `CHAT.COMPL.ANCHOR` | 锚定 | 输入框**上方**（`cY = BASE.Y - totalH - 8`），不占布局高度 |
| `CHAT.COMPL.BG` | 背景 | `Black * (0xaa/255)` ≈ `0.6667` |
| `CHAT.COMPL.BORDER_TOP` | 上边框 | `(cX, cY, cW, 1)`，`Color.Cyan` |
| `CHAT.COMPL.BORDER_LEFT` | 左边框 | `(cX - 3, cY, 3, cH)`，`Color.CornflowerBlue` |
| `CHAT.COMPL.BORDER_LEFT_W` | 左边框宽 | `3`（**在背景外**，向左延伸） |
| `CHAT.COMPL.ITEM_ORDER` | 顺序 | **自下而上**：`i = count-1 → 0`，`curY -= lh` |
| `CHAT.COMPL.TEXT_NORMAL` | 普通项 | `Color.LightGray` |
| `CHAT.COMPL.TEXT_SELECTED` | 选中项 | `Color.White` |
| `CHAT.COMPL.SEL_BG` | 选中背景 | `Color.Wheat * (0x22/255)` ≈ `0.1333` |
| `CHAT.COMPL.SEL_BAR` | 选中左条 | `(cX - 3, sY, 3, lh)`，`Color.Wheat` |
| `CHAT.COMPL.SELECT_INIT` | 初始选中 | `-1`（无） |
| `CHAT.COMPL.WRAP_UP` | Up 越界 | 从 `-1` 到 `count-1`，再循环到 `count-1` |
| `CHAT.COMPL.WRAP_DOWN` | Down 越界 | 从 `-1` 到 `0`，再 `% count` |

补全浮层由 `OverlayNode` 承载，`Overlay` 不参与父节点测量：补全不撑高输入框。

---

## 8. 玩家列表面板

缩放 `s = PlayerListUIScaleValue`，`lh = L * s`。

| ID | 参数 | 值 |
|---|---|---|
| `PL.PANEL.MARGIN_X` | 左边距 | `16` |
| `PL.PANEL.MARGIN_Y` | 频道上/下边距 | `16` |
| `PL.PANEL.PAD_X` | 频道左右内边距 | `16` |
| `PL.PANEL.PAD_Y` | 频道上下内边距 | `16` |
| `PL.PANEL.HEADER_H` | 频道头高 | `lh` |
| `PL.PANEL.BG` | 面板背景 | `Black * (0xcc/255)` = `0.8` |
| `PL.PANEL.BORDER_TOP` | 顶部边框 | 高 `3`，`Color.CornflowerBlue` |
| `PL.PANEL.BORDER_LEFT` | 左侧边框 | 宽 `3`，`Color.Cyan` |
| `PL.PANEL.HEADER_COLOR` | 频道头文字 | `Color.Yellow`，锚点 `TL` |
| `PL.PANEL.CLIP` | 裁剪 | **启用**：按 `ScrollNode` 纵向裁剪 |
| `PL.PANEL.CLIP_RECT` | 裁剪矩形 | `(0, 0, max(Engine.Width, PANEL_X + PANEL_W), Engine.Height)`，即**只做纵向裁剪**，不改变横向溢出行为 |
| `PL.PANEL.ORDER` | 频道顺序 | 自身频道置顶；私有虚拟频道置底 |

**几何（顺序不可变）**

```
curY = -scroll
每个频道：
    curY += 16                     // MARGIN_Y
    channelTop[i] = curY
    curY += 16                     // PAD_Y
    curY += lh                     // 频道头
    每个玩家： curY += rowH
    curY += 16                     // PAD_Y
    channelH[i] = curY - channelTop[i]
    curY += 16                     // MARGIN_Y
```

| ID | 参数 | 值 |
|---|---|---|
| `PL.PANEL.ROW_H` | 玩家行高 | `rowH = lh + 2 * PL.ROW.PAD_Y` |
| `PL.PANEL.PANEL_X` | 面板左 | `16` |
| `PL.PANEL.PANEL_W` | 面板宽 | `totalMaxLineWidth + 2 * 16` |
| `PL.PANEL.BODY_X` | 内容左 | `16 + 16 = 32` |

**宽度推导（全局最大值，所有频道共用同一宽度）**

```
maxLineWidth = max(所有频道的 headerWidth 与所有玩家的 itemWidth)
maxPingWidth = max(有 PingText 的玩家的 (measure(PingText).X * s + spaceWidth))
totalMaxLineWidth = maxLineWidth + maxPingWidth + 2 * PL.ROW.PAD_X
```

| ID | 参数 | 值 |
|---|---|---|
| `PL.PANEL.HEADER_W` | 频道头宽 | `measure(Header).X * s` |
| `PL.ROW.SPACE_W` | 空格宽 | `measure(" ").X * s` |
| `PL.ROW.COLON_W` | 冒号宽 | `measure(":").X * s` |
| `PL.ROW.PAD_X` | 行左右内边距 | `4` |

**斑马纹**

| ID | 参数 | 值 |
|---|---|---|
| `PL.ROW.PAD_Y` | 行上下内边距 | `2` |
| `PL.ROW.STRIPE_EVEN` | 偶数行 | `(0,0,0,0x22)` = 黑 13.3% |
| `PL.ROW.STRIPE_ODD` | 奇数行 | `(0x22,0x22,0x22,0x88)` |
| `PL.ROW.STRIPE_RECT` | 斑马纹矩形 | `(16 + 16, curY, panelW - 32, rowH)`；`curY` 起点 `channelTop + 16 + lh` |
| `PL.ROW.STRIPE_IDX` | 奇偶判定 | `j % 2`，`j` 为该频道内玩家序号 |

---

## 9. 玩家行

**统一起点**

| ID | 参数 | 值 |
|---|---|---|
| `PL.ROW.DRAW_Y` | 文字顶 | `curY + 2` |
| `PL.L2R.X0` | 左→右起点 | `32 + 4` |
| `PL.R2L.X0` | 右→左起点 | `32 + totalMaxLineWidth - 4` |

**状态图标（左→右，按固定顺序）**

顺序不可变：`Paused → Interactions → LiveMode → TakingGolden → GroupPhotoMode → Watching`。

| ID | 参数 | 值 |
|---|---|---|
| `PL.ICON.PAUSED_GAP` | 暂停图标左右间隙 | `PausedTexOffsetRange = 4`（前置 `+4`，后置 `+4`，合计 `+8`） |
| `PL.ICON.PAUSED_ANIM` | 暂停图标浮动 | `offset = sin(t) * 4`，`t += dt * 2` 并 `WrapAngle` |
| `PL.ICON.SCALE` | 图标缩放 | `lh / tex.Height`（每个图标各自按自身高度） |
| `PL.ICON.TINT` | 图标着色 | `Color.White` |

各图标纹理键（`GFX.Gui`）：

| 图标 | 纹理 |
|---|---|
| Paused | `miaonet/paused` |
| Interactions | `miaonet/interactions` |
| LiveMode | `miaonet/live_mode` |
| TakingGolden | `miaonet/taking_golden` |
| GroupPhotoMode | `miaonet/group_photo_mode` |
| Watching/Debug | `miaonet/debug_map` |

**玩家名**

| ID | 参数 | 值 |
|---|---|---|
| `PL.ROW.NAME_POS` | 位置 | `(L2R.X0, DRAW_Y)`，锚点 `TL`，缩放 `s` |
| `PL.ROW.NAME_COLOR` | 颜色 | `player.Info.Color` |

**Ping（右→左，第一列）**

| ID | 参数 | 值 |
|---|---|---|
| `PL.ROW.PING_POS` | 位置 | `(R2L.X0, DRAW_Y)`，锚点 `RT` |
| `PL.ROW.PING_COLOR` | 颜色 | `Color.LightGray` |
| `PL.ROW.PING_COL_W` | 列宽 | `maxPingWidth`（全局最大值，右对齐占用） |

**位置段（右→左，依次）**

| ID | 参数 | 值 |
|---|---|---|
| `PL.ROW.AREA_ICON` | 区域图标 | `DrawJustified(..., RT)`，缩放 `lh / icon.Height`，`White`；其后 `x -= spaceWidth` |
| `PL.ROW.SIDE_TEXT` | 区域模式字符 | 锚点 `RT`，缩放 `s`，色 `item.MapSideColor`；其后 `x -= spaceWidth` |
| `PL.ROW.MAP_NAME` | 地图名 | 锚点 `RT`，缩放 `s`，色 `item.MapNameColor`；`liveMode && !known ⇒ "*"`；其后 `x -= spaceWidth` |
| `PL.ROW.COLON` | 冒号 | 锚点 `RT`，缩放 `s`，`Color.LightGray`；其后 `x -= colonWidth` |
| `PL.ROW.ROOM` | 房间/章 | 非 debug：锚点 `RT`，缩放 `s`，`Color.LightGray`，`liveMode ⇒ "*"`；debug：`miaonet/debug_map` 纹理 `RT`，缩放 `lh / tex.Height` |
| `PL.ROW.R2L_GAP` | 段间隔 | `spaceWidth`（图标与文字间亦然） |

**颜色推导**

| ID | 参数 | 值 |
|---|---|---|
| `PL.ROW.DEFAULT_COLOR` | 默认色 | `Color.LightGray` |
| `PL.ROW.MAP_NAME_COLOR` | 本地已知地图 | `Lerp(areaData.TitleBaseColor, LightGray, 0.5)` |
| `PL.ROW.MAP_SIDE_COLOR` | 本地已知区域 | `Lerp(areaData.TitleAccentColor, LightGray, 0.8)` |
| `PL.ROW.UNKNOWN_COLOR` | 未知地图 | 两者均 `Color.LightGray` |
| `PL.ROW.AREA_MODE` | 区域模式字符 | `loc.Map.AreaModeCharacter.ToString()` |
| `PL.ROW.CLIP_LEN` | 地图名截断长度 | `24` 字符 |
| `PL.ROW.CLIP_MODE` | 截断方式 | `None` 不截断 / `KeepPrefix` `前24+"..."` / `KeepSuffix` `"..."+后24` |
| `PL.ROW.PING_TEXT` | Ping 文本 | `LastPing == -1 ? null : $"{LastPing}ms"` |

---

## 10. 玩家列表滚动

| ID | 参数 | 值 |
|---|---|---|
| `PL.SCROLL.KB_SPEED` | 键盘滚动 | `1024` px/s；Up `−`，Down `+` |
| `PL.SCROLL.MIN` | 下界 | `0` |
| `PL.SCROLL.MAX` | 上界 | `max(0, CONTENT_H - VIEWPORT_H)` |
| `PL.SCROLL.CONTENT_H` | 内容高 | 频道循环结束后的累计 `curY` 回加 `scroll`，即首个频道上边距到末频道下边距的总高 |
| `PL.SCROLL.VIEWPORT_H` | 视口高 | `Engine.Height` |
| `PL.SCROLL.SMOOTH` | 平滑 | `maxMove = max(|target - scroll|, 8) * 8 * dt` |
| `PL.SCROLL.RESET` | 关闭时 | `target = scroll = 0` |

---

## 11. 调色板汇总

| ID | 颜色 | 位置 |
|---|---|---|
| `COLOR.CHAT.BG` | `Black * (fade * ChatBackgroundOpacity/10)` | 消息背景 |
| `COLOR.CHAT.TIME` | `CornflowerBlue * drawAlpha` | 时间戳 |
| `COLOR.CHAT.TEXT` | 段落色（`ChatText`，默认色由调用方给） | 消息正文 |
| `COLOR.CHAT.COUNTER` | `FoldCounter` 彩虹/红 | 折叠计数 |
| `COLOR.TAB.BG` | `Black * (0.5 / 0.15)` | 标签背景 |
| `COLOR.TAB.TEXT` | `White * (1.0 / 0.5)` | 标签文字 |
| `COLOR.INPUT.BG` | `Black * 0.4980` | 输入框背景 |
| `COLOR.INPUT.TEXT` | `White` | 输入文本 |
| `COLOR.INPUT.IME` | `Color.Gray` | IME 组合 |
| `COLOR.INPUT.CARET` | `White` | 光标 |
| `COLOR.COMPL.BG` | `Black * 0.6667` | 补全背景 |
| `COLOR.COMPL.BORDER_TOP` | `Color.Cyan` | 补全上边框 |
| `COLOR.COMPL.BORDER_LEFT` | `Color.CornflowerBlue` | 补全左边框 |
| `COLOR.COMPL.TEXT` | `LightGray` / `White` | 补全普通/选中 |
| `COLOR.COMPL.SEL_BG` | `Wheat * 0.1333` | 选中背景 |
| `COLOR.COMPL.SEL_BAR` | `Color.Wheat` | 选中左条 |
| `COLOR.PL.BG` | `Black * 0.8` | 面板背景 |
| `COLOR.PL.BORDER_TOP` | `CornflowerBlue` | 面板上边框 |
| `COLOR.PL.BORDER_LEFT` | `Color.Cyan` | 面板左边框 |
| `COLOR.PL.HEADER` | `Color.Yellow` | 频道头 |
| `COLOR.PL.STRIPE_EVEN` | `(0,0,0,0x22)` | 斑马纹 |
| `COLOR.PL.STRIPE_ODD` | `(0x22,0x22,0x22,0x88)` | 斑马纹 |
| `COLOR.PL.NAME` | `player.Info.Color` | 玩家名 |
| `COLOR.PL.PING` | `Color.LightGray` | Ping |
| `COLOR.PL.ROOM` | `Color.LightGray` | 房间与冒号 |
| `COLOR.PL.MAP_NAME` | `Lerp(TitleBaseColor, LightGray, 0.5)` | 地图名 |
| `COLOR.PL.MAP_SIDE` | `Lerp(TitleAccentColor, LightGray, 0.8)` | 区域模式 |
| `COLOR.PL.ICON` | `Color.White` | 全部状态图标 |

全部颜色收敛为 `MiaoNetUITheme` 的命名样式；节点内不出现字面颜色。

---

## 12. 参数 → 节点映射

| 参数组 | 承载节点/样式 |
|---|---|
| `GLOBAL.SCALE` / `GLOBAL.LH` | `UIScale.FromSetting`、`MiaoNetFont.ENZhsLineHeight`、各节点的 `Scale` / `LineHeight` |
| `GLOBAL.ANCHOR.*` | `TextStyle.HorizontalAnchor` / `VerticalAnchor`（`TextNode`） |
| `GLOBAL.SNAP.*` | `UIStyle.PixelSnap` |
| `CHAT.MSG.*` | `ChatMessageNode` + `ChatLayout` + `MiaoNetUITheme.Chat` |
| `CHAT.LIST.*` | `ChatMessageListNode`（`VirtualListNode`）+ `ChatListController` + `ChatScreenNode` |
| `CHAT.ANIM.*` | `ChatListController` + `ChatMessageNode` |
| `ANIM.COUNTER.*` | `FoldCounter` |
| `CHAT.TAB.*` | `ChatTabBarNode` + `MiaoNetUITheme.Tab` |
| `CHAT.INPUT.*` | `ChatInputNode` + `TextFieldNode` + `TextEditingController` + `MiaoNetUITheme.Input` |
| `CHAT.COMPL.*` | `CompletionPopupNode` + `MiaoNetUITheme.Completion` |
| `PL.PANEL.*` | `PlayerListPanelNode`（`ScrollNode`）+ `PlayerListChannelNode` + `PlayerListLayout` |
| `PL.ROW.*` | `PlayerRowNode` + `PlayerListMetrics` + `PlayerListLayout` |
| `PL.ICON.*` | `PlayerRowNode` + `PlayerListMetrics` + `PlayerListIcons` |
| `PL.SCROLL.*` | `PlayerListController` |
| `COLOR.*` | `MiaoNetUITheme` |
