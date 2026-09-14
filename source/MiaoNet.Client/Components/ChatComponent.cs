using System.Diagnostics;
using Celeste.Mod.MiaoNet.Chat;
using Celeste.Mod.MiaoNet.UI.Controls;
using Celeste.Mod.MiaoNet.UI.Input;
using MiaoNet.Shared;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.MiaoNet;

#pragma warning disable CA1305

public sealed partial class ChatComponent : MiaoNetComponent
{
    // from CelesteNet
    private sealed class PauseUpdateOverlay : Overlay
    {
        public override void Update()
        {
            base.Update();

            Level level = SceneAs<Level>();

            foreach (Entity e in Engine.Scene[Tags.PauseUpdate])
                if (e.Active && e is not TextMenu)
                    e.Update();

            level.HudRenderer.BackgroundFade = Calc.Approach(
                level.HudRenderer.BackgroundFade,
                level.Paused ? 1f : 0f,
                8f * Engine.RawDeltaTime
            );
        }
    }

    // I hate these "previous" things
    private bool previousCommandsEnabled = false;
    private bool previousScenePaused = false;
    private bool previousAllowHudHide = true;
    private readonly PauseUpdateOverlay dummyOverlay;

    private bool active;
    private readonly TextEditingController editor;
    private readonly ChatMessageManager chatManager;

    private string initialTabTitle = string.Empty;

    private readonly CommandParser cmdParser;

    private readonly ChatMessageFactory chatMessageFactory;


    private string lastInput = string.Empty;
    private readonly List<string> inputHistory;
    private int historyIndex;

    // our claims on arbitrated input, reactions get registered once in the ctor
    private readonly UiInputRegistrations input;

    public bool Active => active;

    public ChatComponent(MiaoNetContext context)
        : base(context)
    {
        inputHistory = new();
        dummyOverlay = new();
        cmdParser = new(MiaoNetCommand.Commands);
        chatMessageFactory = new(context);
        editor = new TextEditingController(
            new ChatCompletionProvider(context, cmdParser),
            MiaoNetFont.CanRender);
        chatManager = new();
        ChatMessageBoxSetup();

        input = context.UiInput.Register(UiInputConsumer.Chat);
        RegisterInputReactions();

        context.ChatMessageReceived += Context_ChatMessageReceived;
        context.PlayerJoined += Context_PlayerJoined;
        context.PlayerLeft += Context_PlayerLeft;

        var settings = MiaoNetModule.Settings;
        MiaoNetModule.Settings.SettingsChanged += Settings_SettingsChanged;
        Settings_SettingsChanged(settings, SettingsCategory.VisualsUI);
    }

    // what the chat does with each action it owns. the routing table already decides when each is
    // live, so no focus guard here. order only matters because changing focus ends the frame
    // (Activate/Deactivate), so closing the box can't also submit/page like the old returns did.
    private void RegisterInputReactions()
    {
        input.On(UiInputAction.ChatToggle, OpenChat);
        input.On(UiInputAction.ChatCommandToggle, OpenCommandChat);

        input.On(UiInputAction.Cancel, CancelEditing);
        input.On(UiInputAction.Submit, SubmitEditing);

        input.On(UiInputAction.ChannelPrevious, () =>
        {
            chatManager.CycleTabForward();
            SyncChatChannelWithTab();
        });
        input.On(UiInputAction.ChannelNext, () =>
        {
            chatManager.CycleTabBackward();
            SyncChatChannelWithTab();
        });

        // completions and history share the arrow keys; which one gets them depends on whether
        // the popup is up, so this guard lives here instead of in the routing table.
        input.On(UiInputAction.CompletionUp, () => SelectCompletion(-1));
        input.On(UiInputAction.CompletionDown, () => SelectCompletion(+1));
        input.On(UiInputAction.HistoryUp, () => StepHistory(-1));
        input.On(UiInputAction.HistoryDown, () => StepHistory(+1));

        input.On(UiInputAction.CaretLeft, () => editor.MoveCaretBackward());
        input.On(UiInputAction.CaretRight, () => editor.MoveCaretForward());
        input.On(UiInputAction.CompletionAccept, () => editor.AcceptCompletion());
        input.On(UiInputAction.Paste, () => editor.Paste(TextInput.GetClipboardText()));
    }

    private void OpenChat()
    {
        if (context.IsSuitableToOpenUI)
        {
            Activate();
        }
    }

    private void OpenCommandChat()
    {
        if (!context.IsSuitableToOpenUI)
        {
            return;
        }

        Activate();
        editor.SetText(CommandParser.CommandPrefix);
    }

    private void CancelEditing()
    {
        MInputHack.ConsumeAllInputs();
        Deactivate();
    }

    private void SubmitEditing()
    {
        MInputHack.ConsumeAllInputs();
        string text = editor.Text;
        string trimmedText = text.Trim();
        if (trimmedText != string.Empty)
        {
            inputHistory.Add(trimmedText);
            if (!trimmedText.StartsWith(CommandParser.CommandPrefix, StringComparison.Ordinal))
            {
                if (!MiaoNetModule.Settings.LiveMode)
                    SendChat(trimmedText);
                else
                    AddLocalChat(MiaoNetChatText.CreateCommandError(Dialog.Get("miaonet_chat_disabled")));
            }
            else
            {
                HandleCommand(trimmedText);
            }
        }

        Deactivate();
    }

    // does nothing while no completion popup is up
    private void SelectCompletion(int step)
    {
        if (!editor.HasCompletions)
        {
            return;
        }

        if (step < 0)
        {
            editor.SelectPreviousCompletion();
        }
        else
        {
            editor.SelectNextCompletion();
        }
    }

    // history is edge-only: holding up/down doesn't keep scrolling
    private void StepHistory(int step)
    {
        if (editor.HasCompletions)
        {
            // while the popup is up, the arrows move its selection
            return;
        }

        int i = step < 0 ? Math.Max(historyIndex + step, 0) : Math.Min(historyIndex + step, inputHistory.Count);
        if (i == historyIndex)
        {
            return;
        }

        // only remember the draft when stepping back off the bottom of the history
        if (step < 0 && historyIndex == inputHistory.Count)
        {
            lastInput = editor.Text;
        }

        historyIndex = i;
        editor.SetSuppressCompletions();
        editor.SetText(i == inputHistory.Count ? lastInput : inputHistory[i]);
    }

    private void AddChatMessage(ChatText message, string? tabName = null)
        => chatManager.AddChatMessage(new ChatItem(message), default, tabName);

    private void AddChatMessage(
        DateTime dateTime,
        ChatText message,
        string? tabName = null,
        string? foldKey = null,
        ChatText? foldedText = null)
        => chatManager.AddChatMessage(new ChatItem(dateTime, message), dateTime, tabName, foldKey, foldedText);

    private void Settings_SettingsChanged(MiaoNetModuleSettings settings, SettingsCategory category)
    {
        if (category is not SettingsCategory.VisualsUI)
            return;

        // UIComponent reads the presentation settings every frame; only the fold window belongs
        // to the log model here.
        chatManager.FoldWindowSeconds = settings.FoldWindowSeconds;
    }

    private void Context_PlayerJoined(OnlinePlayer player)
    {
        if (!MiaoNetModule.Settings.PlayerPresenceMessages)
            return;
        string text = PFormat.Format(context.PlayerPresenceMessage.PlayerJoined, player.GetDisplayName(false, context.ShowAvatar));
        AddLocalChat(MiaoNetChatText.CreateAnnouncement(text));
    }

    private void Context_PlayerLeft(OnlinePlayer player)
    {
        if (!MiaoNetModule.Settings.PlayerPresenceMessages)
            return;
        string text = PFormat.Format(context.PlayerPresenceMessage.PlayerLeft, player.GetDisplayName(false, context.ShowAvatar));
        AddLocalChat(MiaoNetChatText.CreateAnnouncement(text));
    }

    private void Context_ChatMessageReceived(OnlinePlayer? player, PacketChatMessage packet)
    {
        var chatDisabled = MiaoNetModule.Settings.LiveMode;
        if (chatDisabled && packet.Type is not ChatMessageType.Server and not ChatMessageType.ServerChat)
            return;

        ReceivedChatMessage received = chatMessageFactory.CreateReceived(player, packet);
        if (received.Text is not null)
        {
            // Route to appropriate tab based on message type
            ChatChannel? chatChannel = packet.Type switch
            {
                ChatMessageType.Chat => ChatChannel.Global,
                ChatMessageType.ChannelChat => ChatChannel.Channel,
                ChatMessageType.MapChat => ChatChannel.Map,
                _ => null
            };
            string? tabName = ChatChannelMatcher.GetLocalizedName(chatChannel);

            string? foldKey = null;
            ChatText? foldedText = null;
            if (MiaoNetModule.Settings.MessageFolding)
                (foldKey, foldedText) = chatMessageFactory.CreateFoldInfo(player, packet, received.Content);

            AddChatMessage(packet.DateTime, received.Text, tabName, foldKey, foldedText);
        }
        else
            Logger.Warn(LT.MiaoNet, $"Null chat message received for type {packet.Type}. Content: {packet.Content}");

        if (received.MentionsSelf)
            Audio.Play(MiaoNetSFX.ChatMention);
    }
    
    private void SyncChatChannelWithTab()
    {
        var chatTabName = chatManager.ActiveTabName ?? ChatChannelMatcher.GetLocalizedName(ChatChannel.Global);
        var chatChannel = ChatChannelMatcher.MatchLocalized(chatTabName!);
        if (chatChannel != (ChatChannel)(-1))
        {
            MiaoNetModule.Settings.ChatChannel = chatChannel;
        }
    }

    public override void Update()
    {
        // the reactions registered in the ctor already ran: UiInputRouter.Route arbitrates and
        // fires them once per frame before any component updates. so what's left is the per-frame
        // stuff that doesn't need input -- keeping the scene paused and the caret blink.
        if (active)
        {
            Engine.Scene.Paused = true;
            editor.Update(Engine.RawDeltaTime);
        }
    }

    public void SendChat(string text)
        => context.QueuePacket(new PacketSendChatMessage(MiaoNetModule.Settings.ChatChannel, text));

    public void AddLocalChat(ChatText message)
        => AddChatMessage(message);

    public void OnSentPrivateMessage(DateTime dateTime, OnlinePlayer other, string text)
        => AddChatMessage(dateTime, chatMessageFactory.CreateSentPrivateMessage(other, text), null);

    public void ClearChat()
        => chatManager.CleanHistory();

    public void HandleCommand(string text)
    {
        var result = cmdParser.Parse(text, out var cmdName, out var cmd, out var args);

        AddChatMessage(MiaoNetChatText.CreateCommandEcho(text));

        if (result != CommandParser.ParseResult.Success)
        {
            TipCommandError(result, cmdName, cmd, args is null ? -1 : args.Count);
            return;
        }

        string? error = cmd!.OnExecute(new MiaoNetCommand.Context(context, args!));
        if (error is not null)
            AddLocalChat(MiaoNetChatText.CreateCommandError(error));

        void TipCommandError(CommandParser.ParseResult result, string cmdName, MiaoNetCommand? cmd, int argc)
        {
            string msg = result switch
            {
                CommandParser.ParseResult.NoSuchCommand =>
                    PFormat.Format(Dialog.Clean("miaonet_command_status_no_such_command"), cmdName),
                CommandParser.ParseResult.MissingArguments =>
                    PFormat.Format(Dialog.Clean("miaonet_command_status_missing_arguments"), cmdName, cmd!.Segments.Count, argc),
                CommandParser.ParseResult.TooManyArguments =>
                    PFormat.Format(Dialog.Clean("miaonet_command_status_too_many_arguments"), cmdName, cmd!.Segments.Count, argc),
            };
            AddLocalChat(MiaoNetChatText.CreateCommandError(msg));
        }
    }

    // TODO TODO TODO we need a clean up method
    public override void OnDisconnected()
    {
        if (active)
            Deactivate();
        ChatMessageBoxSetup();
        inputHistory.Clear();
        historyIndex = 0;
    }

    private void ChatMessageBoxSetup()
    {
        chatManager.CleanUp();
        initialTabTitle = Dialog.Get("miaonet_initial_chat_tab_name");
        foreach (ChatChannel type in Enum.GetValues(typeof(ChatChannel)))
        {
            string? localizedTabName = ChatChannelMatcher.GetLocalizedName(type); 
            if (localizedTabName == null)
                throw new UnreachableException();
            chatManager.AddTab(localizedTabName);
        }
    }

    private void Activate()
    {
        active = true;
        historyIndex = inputHistory.Count;
        editor.Activate();
        TextInput.OnInput += OnCharInput;
        TextInputEXT.TextEditing += OnTextEditing;
        previousCommandsEnabled = Engine.Commands.Enabled;
        Engine.Commands.Enabled = false;
        previousScenePaused = Engine.Scene.Paused;
        Engine.Scene.Paused = true;

        if (Engine.Scene is Level level)
        {
            previousAllowHudHide = level.AllowHudHide;
            level.Add(dummyOverlay);
            level.AllowHudHide = false;
        }
        context.UiInput.SetFocus(UiFocusOwner.Chat);
    }

    private void Deactivate()
    {
        active = false;
        TextInput.OnInput -= OnCharInput;
        TextInputEXT.TextEditing -= OnTextEditing;
        editor.Deactivate();
        lastInput = string.Empty;
        Engine.Commands.Enabled = previousCommandsEnabled;
        Engine.Scene.Paused = previousScenePaused;

        if (Engine.Scene is Level level)
        {
            level.CompletelyRemove(dummyOverlay);
            level.AllowHudHide = previousAllowHudHide;
        }
        context.UiInput.SetFocus(UiFocusOwner.None);
    }

    // the editing kernel; UIComponent draws it
    internal TextEditingController Editor => editor;

    private void OnCharInput(char chr) => editor.InputChar(chr);

    private void OnTextEditing(string? text, int start, int length)
        => editor.SetImeComposition(text, start, length);
}
