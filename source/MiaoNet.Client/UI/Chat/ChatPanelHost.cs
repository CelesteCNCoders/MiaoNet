using Celeste.Mod.MiaoNet.Chat;
using Celeste.Mod.MiaoNet.UI.Controls;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Input;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;
using Celeste.Mod.MiaoNet.UI.Styling;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.MiaoNet.UI.Chat;

// everything the chat panel needs from outside the tree: the settings, the engine, the router, the
// ime candidate rect and the data component. the tree itself is ChatScreenNode and friends.
//
// unlike the rest of ui/ this file is client-only (the unit test project doesn't compile it): it
// reads MiaoNetModule and Engine, the same way the concrete renderers do.
public sealed class ChatPanelHost
{
    private static readonly IReadOnlyList<Completion> NoCompletions = [];

    private readonly MiaoNetContext context;
    private readonly ChatComponent data;
    private readonly ChatListController controller = new();
    private readonly ChatMessageListNode messages;
    private readonly ChatTabBarNode tabs;
    private readonly ChatScreenNode screen;
    private readonly TextFieldNode field;
    private readonly CompletionPopupNode popup;
    private readonly UIInputRegistrations input;

    private int builtVersion = -1;
    private ChatUISnapshot? snapshot;

    // the node properties that come straight from the settings, pushed only when one of them
    // changes. pushing them every frame meant twenty-odd writes a second and made a forgotten line
    // a setting that silently stops applying.
    private UISettings appliedSettings;
    private bool hasAppliedSettings;

    public ChatPanelHost(MiaoNetContext context, ITextRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(renderer);

        this.context = context;
        data = context.ChatComponent;

        messages = new ChatMessageListNode(renderer, controller);
        tabs = new ChatTabBarNode(renderer);
        field = new TextFieldNode(renderer, data.Editor);
        popup = new CompletionPopupNode(renderer);
        screen = new ChatScreenNode(messages, tabs, new ChatInputNode(field, popup));

        // ChatComponent already holds this consumer for its editing reactions; we claim the same
        // handle for the list's scroll actions, because the router arbitrates per consumer and not
        // per class.
        input = context.UIInput.Register(UIInputConsumer.Chat);
        input.OwnHeld(UIInputAction.ChatListScrollUp);
        input.OwnHeld(UIInputAction.ChatListScrollDown);
    }

    public UINode Root => screen;

    public ChatListController Controller => controller;

    public void Update()
    {
        ApplySettings();

        float deltaTime = Engine.RawDeltaTime;

        IReadOnlyList<Completion>? completions = data.Editor.Completions;
        popup.Items = completions ?? NoCompletions;
        popup.SelectedIndex = data.Editor.SelectedCompletionIndex;

        screen.Active = data.Active;

        // the controller resets scroll itself on the active -> inactive edge, so no edge tracking
        // needed here.
        controller.Active = data.Active;

        if (builtVersion != data.UIVersion)
        {
            snapshot = data.BuildUISnapshot();
            messages.SetMessages(snapshot.Display);
            tabs.Tabs = snapshot.TabNames;
            tabs.InitialTitle = snapshot.InitialTitle;
            builtVersion = data.UIVersion;
        }

        if (snapshot is null)
        {
            return;
        }

        tabs.ActiveIndex = snapshot.ActiveTabIndex;
        controller.UpdateTimers(snapshot.FullLogKeys, snapshot.RepeatCounts, deltaTime);

        // wheel and PageUp/PageDown add into one delta. both come from the same routing rule, so
        // the wheel does nothing while the player list owns the keyboard.
        float delta = context.UIInput.ChatScrollDelta;
        if (input.IsHeld(UIInputAction.ChatListScrollUp))
        {
            delta += ChatLayout.KeyboardScrollSpeed * deltaTime;
        }
        else if (input.IsHeld(UIInputAction.ChatListScrollDown))
        {
            delta -= ChatLayout.KeyboardScrollSpeed * deltaTime;
        }

        if (delta != 0f)
        {
            controller.ScrollBy(delta);
        }

        // list geometry is known without a layout pass, so the offset is exact up front.
        // we don't set the message count here: ChatMessageListNode pushes it when the displayed
        // list changes, so the clamp can't use a different list than the one being rendered.
        controller.ItemExtent = messages.MessageLineHeight;
        controller.ViewportHeight = screen.ListHeight(Engine.Height);
        controller.UpdateScroll(deltaTime);
        messages.Offset = controller.ContentOffset;
    }

    // point the platform's ime candidate window at the composition. the node hands over logical
    // coordinates, the platform call stays here.
    //
    // the rect is the whole input band, not the text line: that is what the platform was given
    // before, and it has to run after layout or it is a frame behind.
    public void UpdateInputMethodRect()
    {
        if (!data.Active)
        {
            return;
        }

        UIRect box = screen.Input.Bounds;
        float xScale = Engine.ViewWidth / (float)Engine.Width;
        float yScale = Engine.ViewHeight / (float)Engine.Height;
        Rectangle rect = UIImeRect.Map(
            field.ImeAnchorX,
            box.Y,
            field.ImeTextWidth,
            box.Height,
            xScale,
            yScale,
            Engine.Viewport.X,
            Engine.Viewport.Y);

        // Known issue: with only a few pinyin letters typed, the candidate window covers
        // the input row, and it only avoids it once more input arrives. A plain SDL2-only
        // project shows the same thing, so this is probably an SDL2-side issue.
        TextInputEXT.SetInputRectangle(rect);
    }

    // called on disconnect
    public void Reset()
    {
        controller.Reset();
        builtVersion = -1;
        snapshot = null;
    }

    // everything the nodes take straight from the settings, in one place. the derived line height
    // is part of the snapshot on purpose: the font size can change without any setting changing.
    private void ApplySettings()
    {
        UISettings current = UISettings.From(MiaoNetModule.Settings);
        if (hasAppliedSettings && current == appliedSettings)
        {
            return;
        }

        appliedSettings = current;
        hasAppliedSettings = true;

        messages.MessagePaddingY = current.MessagePadding;
        messages.LineHeight = current.ChatLineHeight;
        messages.Scale = current.ChatScale;
        messages.BackgroundOpacity = current.BackgroundOpacity;
        messages.TextOpacity = current.TextOpacity;
        messages.FancyCounter = current.FancyCounter;
        messages.RefreshMetrics();

        tabs.LineHeight = current.ChatLineHeight;
        tabs.Scale = current.ChatScale;

        // chatinputnode hands line height and scale down to the text field and the popup when it
        // measures, so setting those two here as well would just give the same value two owners.
        screen.Input.LineHeight = current.ChatLineHeight;
        screen.Input.Scale = current.ChatScale;

        screen.LineHeight = current.ChatLineHeight;
        screen.IdleRatio = current.IdleRatio;
        screen.ActiveRatio = current.ActiveRatio;

        controller.ShowDuration = current.DisplayDuration;
    }

    private readonly record struct UISettings(
        float ChatLineHeight,
        float ChatScale,
        float MessagePadding,
        float BackgroundOpacity,
        float TextOpacity,
        bool FancyCounter,
        float IdleRatio,
        float ActiveRatio,
        float DisplayDuration)
    {
        public static UISettings From(MiaoNetModuleSettings settings)
            => new(
                MiaoNetFont.ENZhsLineHeight * settings.ChatUIScaleValue,
                settings.ChatUIScaleValue,
                settings.ChatMessagePadding,
                settings.ChatBackgroundOpacityValue,
                settings.ChatTextOpacityValue,
                settings.FancyFoldCounter,
                settings.IdleChatHeightValue,
                settings.ActiveChatHeightValue,
                settings.ChatDisplayDuration);
    }
}
