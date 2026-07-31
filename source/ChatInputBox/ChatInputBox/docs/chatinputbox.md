# ChatInputBox 库

独立的聊天输入框 UI 库，与 MiaoNet 业务逻辑解耦，通过接口交互。

## 组件

```
ChatInputBox/
├── InputBox.cs                — 输入框核心（输入处理、补全交互、渲染）
├── TextBuffer.cs              — 文本缓冲区（光标管理、编辑操作）
├── ChatMessageListView.cs     — 聊天消息列表（滚动、淡出、渲染）
├── ChatText.cs                — 富文本解析（颜色码、样式码 → Segment 列表）
├── ChatTextSegment.cs         — 富文本段（文本 + 样式 + 颜色）
├── ChatTextStyle.cs           — 样式枚举（None, Underscore, Strikethrough, Outline）
├── ICompletionProvider.cs     — 补全提供者接口
├── Completion.cs              — 补全项结构
└── ITextRenderer.cs           — 文本渲染器接口（Measure, Draw, DrawOutline）
```

## InputBox 输入框

### 职责

- 接收键盘输入（含 IME 组合输入）
- 管理光标位置和文本编辑
- 驱动补全列表（通过 `ICompletionProvider`）
- 渲染输入框 + 补全弹窗

### 输入处理

| 按键 | 行为 |
|------|------|
| 字符输入 | 在光标处插入（需通过 `ITextRenderer.CanRender` 校验） |
| Backspace | 删除光标前一个字符 |
| Delete | 删除光标后一个字符 |
| Home | 光标移到开头 |
| End | 光标移到末尾 |
| Left / Right | 光标左右移动 |
| Up / Down | 补全列表中选择上/下一项 |
| Tab | 应用当前选中的补全项（或唯一项） |
| Ctrl+V | 粘贴（过滤控制字符，截断到 MaxTextLength） |

### 补全交互

```
TextBuffer 文本变化
    │
    ▼
TextOrCaretChanged 事件
    │
    ▼
ICompletionProvider.GetCompletions(textBeforeCaret)
    │
    ├── 返回 null → 无补全列表
    └── 返回 IEnumerable<Completion> → 显示补全弹窗
            │
            ├── Up/Down 选择
            └── Tab 应用：TextBuffer.DoCompletion(remove, content)
```

`SetSuppressCompletions()` 可在应用补全后暂时抑制再次触发补全（防止 "补全后立即弹出新列表" 的循环）。

## TextBuffer 文本缓冲区

维护文本内容和光标位置，提供编辑原语：

```csharp
string Text              // 完整文本
string TextBeforeCaret   // 光标前文本（用于补全匹配）
string TextAfterCaret    // 光标后文本
int CaretPosition        // 光标位置

void InputChar(char)     // 插入字符
void InputString(string) // 插入字符串
bool Backspace()         // 退格
bool Delete()            // 删除
void DoCompletion(int remove, string text) // 补全应用
void Clear()             // 清空
```

所有修改都会触发 `TextOrCaretChanged` 事件。

## 补全系统

### ICompletionProvider 接口

```csharp
public interface ICompletionProvider
{
    IEnumerable<Completion>? GetCompletions(string input);
}
```

输入为光标前的文本。返回 `null` 表示无补全可用。

### Completion 结构

```csharp
public readonly struct Completion
{
    string Content   // 应用时替换的文本
    string Display   // 列表中显示的文本
    int Remove       // 应用时从光标前删除的字符数
}
```

### 应用逻辑

```
输入: "/tp whe|at"  (| 为光标)
补全: Completion("wheat", "wheat", 3)
操作: 从光标前删除 3 字符 "whe"，插入 "wheat"
结果: "/tp wheat|at"
```

### MiaoNet 的 ChatCompletionProvider

实现了 `ICompletionProvider`，按优先级尝试：

1. **Emoji 补全** — 检测最后一个 `:`，在注册的 Emoji 中模糊匹配
2. **命令补全** — 检测 `/` 前缀：
   - 未完成命令名 → 命令名列表匹配
   - 已匹配命令 → 按当前参数的 `CommandSegmentType` 提供候选：
     - `Player` → 全服玩家名
     - `PlayerSameChannel` → 同频道玩家名
     - `PlayerSameMap` → 同地图玩家名
     - `Channel` → 已有频道名
     - `ChatChannelType` → "global", "channel", "map"
     - `CommandName` → 命令名

匹配使用 `Contains`（大小写不敏感），所以 "eat" 能匹配 "wheat"。

## ChatMessageListView 消息列表

### 功能

- 显示聊天消息列表（从下往上排列）
- 空闲模式：新消息淡入后自动淡出（`ShowDuration` 后开始 0.25s 淡出）
- 激活模式：所有消息可见，支持滚动浏览
- 每条消息独立计时、独立淡出

### 配置

| 属性 | 默认值 | 含义 |
|------|--------|------|
| `IdleMaxCount` | 12 | 空闲时最多显示条数 |
| `ActiveMaxCount` | 18 | 激活时最多显示条数 |
| `ShowDuration` | 8s | 消息显示时长 |
| `BackgroundOpacity` | 0.5 | 消息背景透明度 |
| `TextOpacity` | 1.0 | 文字透明度 |

## ChatText 富文本

解析带格式码的文本为 `ChatTextSegment` 数组。

### 格式码

| 语法 | 含义 |
|------|------|
| `\0` - `\f` | 设置预设颜色（Minecraft 风格 16 色） |
| `\#RRGGBB` | 设置自定义 RGB 颜色 |
| `\r` | 重置颜色和样式 |
| `\u` | 切换下划线 |
| `\s` | 切换删除线 |
| `\o` | 切换描边 |
| `\\` | 转义反斜杠 |

### ChatTextSegment

```csharp
public readonly struct ChatTextSegment
{
    ChatTextStyle Style  // None, Underscore, Strikethrough, Outline（可组合）
    Color Color          // 段落颜色
    string Text          // 文本内容
}
```

## ITextRenderer 接口

```csharp
public interface ITextRenderer
{
    float LineHeight { get; }
    bool CanRender(char c);
    Vector2 Measure(string text);
    void Draw(string text, Vector2 position, Vector2 justify, Color color);
    void DrawOutline(string text, Vector2 position, Vector2 justify, Color color);
}
```

由宿主（MiaoNet Client）提供具体实现，使用 Celeste 的字体系统。