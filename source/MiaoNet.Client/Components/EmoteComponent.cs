using MiaoNet.Shared;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.MiaoNet;

public sealed class EmoteComponent : MiaoNetComponent
{
    private static bool IsLiveMode => MiaoNetModule.Settings.LiveMode;

    private EmoteWheel? wheel;

    public EmoteComponent(MiaoNetContext context)
        : base(context)
    {
        context.EmoteReceived += Context_EmoteReceived;
        context.EmoteTextReceived += Context_EmoteTextReceived;
    }

    public override void Update()
    {
        if (Engine.Scene is not Level level)
            return;

        Player player = level.Tracker.GetEntity<Player>();
        if (player is null)
            return;

        var settings = MiaoNetModule.Settings;
        var emoteButtons = settings.EmoteButtons;
        var minCount = Math.Min(emoteButtons.Count, settings.Emotes.Count);
        for (int i = 0; i < minCount; i++)
        {
            if (emoteButtons[i].Pressed)
            {
                emoteButtons[i].ConsumePress();
                string content = settings.Emotes[i];
                EmoteData? data = EmoteData.Parse(content);
                if (data is EmoteData emoteData)
                    SendEmote(player, emoteData);
                else
                    SendEmote(player, content);
            }
        }

        if (MInput.ControllerHasFocus)
        {
            if (wheel is null)
            {
                wheel = new(player);
                wheel.OnEmote += (te) =>
                {
                    if (te.Item1 is not null)
                        SendEmote((EmoteData)te.Item1);
                    else
                        SendEmote(te.Item2!);
                };
            }
            wheel.Tracking = player;
            if (wheel.Scene != player.Scene)
            {
                wheel.RemoveSelf();
                level.Add(wheel);
            }
        }
    }

    public bool SendEmote(EmoteData emote)
    {
        if (Engine.Scene is not Level level)
            return false;
        Player player = level.Tracker.GetEntity<Player>();
        if (player is null)
            return false;
        SendEmote(player, emote);
        return true;
    }

    public bool SendEmote(string emoteText)
    {
        if (Engine.Scene is not Level level)
            return false;
        Player player = level.Tracker.GetEntity<Player>();
        if (player is null)
            return false;
        SendEmote(player, emoteText);
        return true;
    }

    private void SendEmote(Player self, EmoteData emote)
    {
        if (IsLiveMode)
            return;
        context.QueuePacket(new PacketSendEmote(emote));
        AddGhostEmote(self, emote);
    }

    private void SendEmote(Player self, string emoteText)
    {
        if (IsLiveMode)
            return;
        context.QueuePacket(new PacketSendEmoteText(emoteText));
        AddGhostEmote(self, emoteText);
    }

    private void Context_EmoteReceived(OnlinePlayer player, EmoteData emote)
    {
        if (IsLiveMode)
            return;
        int id = player.ID;
        if (context.MainComponent.TryGetGhostTarget(id, out var entity))
        {
            if (entity.Scene is not null)
                AddGhostEmote(entity, emote);
        }
        else
        {
            Logger.Warn(
                LT.MiaoNetEmoteComponent,
                $"No ghost for player {player.Info} can be attached with emote {emote.Prefix}/{emote.Frames[0]}."
            );
        }
    }

    private void Context_EmoteTextReceived(OnlinePlayer player, string text)
    {
        if (IsLiveMode)
            return;
        int id = player.ID;
        if (context.MainComponent.TryGetGhostTarget(id, out var entity))
        {
            if (entity.Scene is not null)
                AddGhostEmote(entity, text);
        }
        else
        {
            Logger.Warn(
                LT.MiaoNetEmoteComponent,
                $"No ghost for player {player.Info} can be attached with emote text \"{text}\"."
            );
        }
    }

    private static void AddGhostEmote(Entity target, EmoteData emote)
    {
        GhostEmote ghostEmote = new(target, new BakedEmoteData(emote));
        target.Scene.Add(ghostEmote);
    }

    private static void AddGhostEmote(Entity target, string text)
    {
        GhostEmote ghostEmote = new(target, text);
        target.Scene.Add(ghostEmote);
    }
}
