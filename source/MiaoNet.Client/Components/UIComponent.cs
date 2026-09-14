using Celeste.Mod.MiaoNet.Chat;
using Celeste.Mod.MiaoNet.UI.Chat;
using Celeste.Mod.MiaoNet.UI.Controls;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Input;
using Celeste.Mod.MiaoNet.UI.Layout;
using Celeste.Mod.MiaoNet.UI.PlayerList;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet;

// owns the retained UI tree and its screen-space rendering: the runtime, canvas, controllers
// and widget nodes. business state stays in the data components; this only turns input and data
// into nodes.
// one runtime serves both panels, chat (messages, tabs, input box, completion popup) and player
// list. the chat log model lives in MiaoNet.Client/Chat/, we just make nodes out of it.
public sealed class UIComponent : MiaoNetComponent
{
    private readonly UIRoot ui = new();
    private readonly MiaoNetUICanvas canvas = new();
    private readonly MiaoNetTextRenderer textRenderer = MiaoNetTextRenderer.Instance;

    private readonly PlayerListController playerList = new();
    private readonly PlayerListComponent playerListData;
    private readonly PlayerListPanelNode playerListPanel;
    private readonly AlignNode playerListHost;

    private readonly ChatListController chat = new();
    private readonly ChatComponent chatData;
    private readonly ChatMessageListNode chatMessages;
    private readonly ChatTabBarNode chatTabs;
    private readonly ChatScreenNode chatScreen;
    private readonly TextFieldNode chatField;
    private readonly CompletionPopupNode chatPopup;

    private static readonly IReadOnlyList<Completion> NoCompletions = [];

    private readonly StackNode root;

    private int builtPlayerListVersion = -1;
    private float builtPlayerListScale = -1f;
    private bool builtLiveMode;

    private int builtChatVersion = -1;
    private ChatUISnapshot? chatSnapshot;

    private float hostWidth = -1f;
    private float hostHeight = -1f;

    private readonly UIInputRegistrations playerListInput;
    private readonly UIInputRegistrations chatInput;

    // set by the player list toggle reaction, consumed in UpdatePlayerList
    private bool playerListToggleRequested;

    public UIComponent(MiaoNetContext context)
        : base(context)
    {
        playerListData = context.PlayerListComponent;
        chatData = context.ChatComponent;

        playerListPanel = new PlayerListPanelNode(textRenderer);
        playerListHost = new AlignNode
        {
            Alignment = UIAlignment.TopLeft,
            Child = playerListPanel,
        };

        chatMessages = new ChatMessageListNode(textRenderer, chat);
        chatTabs = new ChatTabBarNode(textRenderer);
        chatField = new TextFieldNode(textRenderer, chatData.Editor);
        chatPopup = new CompletionPopupNode(textRenderer);
        chatScreen = new ChatScreenNode(
            chatMessages,
            chatTabs,
            new ChatInputNode(chatField, chatPopup));

        // paint order: player list on top of the chat
        root = new StackNode { Alignment = UIAlignment.TopLeft };
        root.Add(chatScreen);
        root.Add(playerListHost);
        ui.SetRoot(root);

        // we drive the chat message list, so we claim that consumer's scroll actions on the same
        // handle ChatComponent used for its editing reactions: the router arbitrates per
        // consumer, not per class.
        playerListInput = context.UIInput.Register(UIInputConsumer.PlayerList);
        chatInput = context.UIInput.Register(UIInputConsumer.Chat);

        playerListInput.On(UIInputAction.PlayerListToggle, TogglePlayerListInPressMode);
        playerListInput.OwnHeld(UIInputAction.PlayerListToggle);
        playerListInput.OwnHeld(UIInputAction.PlayerListScrollUp);
        playerListInput.OwnHeld(UIInputAction.PlayerListScrollDown);

        chatInput.OwnHeld(UIInputAction.ChatListScrollUp);
        chatInput.OwnHeld(UIInputAction.ChatListScrollDown);
    }

    // press mode reacts to the edge. the reaction runs before Update and only records intent;
    // opening the list still happens in one place, UpdatePlayerList, because that also has to
    // check the scene.
    private void TogglePlayerListInPressMode()
    {
        if (MiaoNetModule.Settings.PlayerListButtonMode != ButtonMode.Press)
        {
            return;
        }

        playerListToggleRequested = true;
    }

    public PlayerListController PlayerListController => playerList;

    public ChatListController ChatController => chat;

    public override void Update()
    {
        UpdatePlayerList();
        UpdateChat();
        EnsureHostSize();
        ui.Layout(hostWidth, hostHeight);
        SyncPlayerListAfterLayout();
    }

    public override void Render()
    {
        canvas.BeginFrame();
        try
        {
            ui.Paint(canvas);
        }
        finally
        {
            canvas.EndFrame();
        }
    }

    public override void OnDisconnected()
    {
        bool playerListWasOpen = playerList.IsOpen;
        playerList.SetOpen(false);
        playerList.Reset();
        playerListData.Active = false;
        playerListHost.IsVisible = false;
        if (playerListWasOpen)
        {
            // release the keyboard explicitly, a stale owner would block opening the chat
            context.UIInput.SetFocus(UIFocusOwner.None);
        }

        chat.Reset();
        builtPlayerListVersion = -1;
        builtPlayerListScale = -1f;
        builtChatVersion = -1;
        chatSnapshot = null;
    }

    // ---------------------------------------------------------------- player list

    private void UpdatePlayerList()
    {
        var settings = MiaoNetModule.Settings;

        bool wantsOpen;
        if (settings.PlayerListButtonMode == ButtonMode.Press)
        {
            wantsOpen = playerList.IsOpen;
            if (playerListToggleRequested)
            {
                wantsOpen = !playerList.IsOpen;
            }
        }
        else
        {
            // hold mode is a level, not an edge: the routing table delivers it and we read it
            wantsOpen = playerListInput.IsHeld(UIInputAction.PlayerListToggle);
        }

        playerListToggleRequested = false;

        // opening is refused when the scene is not suitable, but an already open list stays open
        if (wantsOpen && !playerList.IsOpen && !context.IsSuitableToOpenUI)
        {
            wantsOpen = false;
        }

        bool wasOpen = playerList.IsOpen;
        playerList.SetOpen(wantsOpen);
        if (playerList.IsOpen != wasOpen)
        {
            playerListData.Active = playerList.IsOpen;
            context.UIInput.SetFocus(playerList.IsOpen ? UIFocusOwner.PlayerList : UIFocusOwner.None);
        }

        // the routing table already restricts these to player-list focus, i.e. while it's open
        playerList.ScrollUpHeld = playerListInput.IsHeld(UIInputAction.PlayerListScrollUp);
        playerList.ScrollDownHeld = playerListInput.IsHeld(UIInputAction.PlayerListScrollDown);
        playerList.ViewportHeight = Engine.Height;
        playerList.Update(Engine.RawDeltaTime);

        playerListHost.IsVisible = playerList.IsOpen;
        if (!playerList.IsOpen)
        {
            return;
        }

        RebuildPlayerListIfNeeded(settings.PlayerListUIScaleValue, settings.LiveMode);
        playerListPanel.SetPausedIconOffset(playerList.PausedIconOffset);
        playerListPanel.Offset = playerList.Scroll;
    }

    private void RebuildPlayerListIfNeeded(float scale, bool liveMode)
    {
        // live mode masks map and room names, so it changes the model too
        if (builtPlayerListVersion == playerListData.UIVersion
            && builtPlayerListScale == scale
            && builtLiveMode == liveMode)
        {
            return;
        }

        playerListPanel.Rebuild(
            playerListData.BuildUIChannels(),
            playerListData.UIIcons,
            scale,
            MiaoNetFont.ENZhsLineHeight * scale);

        builtPlayerListVersion = playerListData.UIVersion;
        builtPlayerListScale = scale;
        builtLiveMode = liveMode;
    }

    private void SyncPlayerListAfterLayout()
    {
        if (!playerList.IsOpen)
        {
            return;
        }

        // content height is only known after layout; if the clamp moved the offset, re-arrange so
        // a freshly scrolled list doesn't lag a frame behind.
        float before = playerList.Scroll;
        playerList.ContentHeight = playerListPanel.ContentSize.Height;
        playerList.ClampToContent();
        playerListPanel.Offset = playerList.Scroll;
        if (playerList.Scroll != before)
        {
            ui.Layout(hostWidth, hostHeight);
        }
    }

    // ---------------------------------------------------------------- chat

    private void UpdateChat()
    {
        var settings = MiaoNetModule.Settings;
        float deltaTime = Engine.RawDeltaTime;
        float scale = settings.ChatUIScaleValue;
        float lineHeight = MiaoNetFont.ENZhsLineHeight * scale;

        chatMessages.MessagePaddingY = settings.ChatMessagePadding;
        chatMessages.LineHeight = lineHeight;
        chatMessages.Scale = scale;
        chatMessages.BackgroundOpacity = settings.ChatBackgroundOpacityValue;
        chatMessages.TextOpacity = settings.ChatTextOpacityValue;
        chatMessages.RefreshMetrics();

        chatTabs.LineHeight = lineHeight;
        chatTabs.Scale = scale;
        chatField.LineHeight = lineHeight;
        chatField.Scale = scale;
        chatPopup.LineHeight = lineHeight;
        chatPopup.Scale = scale;
        chatScreen.Input.LineHeight = lineHeight;
        chatScreen.Input.Scale = scale;

        IReadOnlyList<Completion>? completions = chatData.Editor.Completions;
        chatPopup.Items = completions ?? NoCompletions;
        chatPopup.SelectedIndex = chatData.Editor.SelectedCompletionIndex;

        chatScreen.Active = chatData.Active;
        chatScreen.LineHeight = lineHeight;
        chatScreen.IdleRatio = settings.IdleChatHeightValue;
        chatScreen.ActiveRatio = settings.ActiveChatHeightValue;

        // the controller resets scroll itself on the active -> inactive edge, so no edge tracking
        // needed here.
        chat.Active = chatData.Active;
        chat.ShowDuration = settings.ChatDisplayDuration;

        if (builtChatVersion != chatData.UIVersion)
        {
            chatSnapshot = chatData.BuildUISnapshot();
            chatMessages.SetMessages(chatSnapshot.Display);
            chatTabs.Tabs = chatSnapshot.TabNames;
            chatTabs.InitialTitle = chatSnapshot.InitialTitle;
            builtChatVersion = chatData.UIVersion;
        }

        if (chatSnapshot is null)
        {
            return;
        }

        chatTabs.ActiveIndex = chatSnapshot.ActiveTabIndex;
        chat.UpdateTimers(chatSnapshot.FullLogKeys, chatSnapshot.RepeatCounts, deltaTime);

        // wheel and PageUp/PageDown add into one delta. both come from the same routing rule, so
        // the wheel does nothing while the player list owns the keyboard.
        float delta = context.UIInput.ChatScrollDelta;
        if (chatInput.IsHeld(UIInputAction.ChatListScrollUp))
        {
            delta += ChatLayout.KeyboardScrollSpeed * deltaTime;
        }
        else if (chatInput.IsHeld(UIInputAction.ChatListScrollDown))
        {
            delta -= ChatLayout.KeyboardScrollSpeed * deltaTime;
        }

        if (delta != 0f)
        {
            chat.ScrollBy(delta);
        }

        // list geometry is known without a layout pass, so the offset is exact up front.
        // we don't set the message count here: ChatMessageListNode pushes it when the displayed
        // list changes, so the clamp can't use a different list than the one being rendered.
        chat.ItemExtent = chatMessages.MessageLineHeight;
        chat.ViewportHeight = chatScreen.ListHeight(Engine.Height);
        chat.UpdateScroll(deltaTime);
        chatMessages.Offset = chat.ContentOffset;
    }

    private void EnsureHostSize()
    {
        if (hostWidth == Engine.Width && hostHeight == Engine.Height)
        {
            return;
        }

        hostWidth = Engine.Width;
        hostHeight = Engine.Height;
        playerListHost.Style = new UIStyle { Width = hostWidth, Height = hostHeight };
        root.Style = new UIStyle { Width = hostWidth, Height = hostHeight };
    }
}
