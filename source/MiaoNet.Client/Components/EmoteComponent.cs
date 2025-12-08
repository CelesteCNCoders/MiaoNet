using MiaoNet.Shared;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.MiaoNet;

public sealed class EmoteComponent : MiaoNetComponent
{
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
        if (level.Paused)
            return;

        Player player = level.Tracker.GetEntity<Player>();
        if (player is null)
            return;
        if (MInput.Keyboard.Pressed(Keys.D1))
        {
            EmoteData emote = new(loop: true, EmoteAtlasCategory.Portrait, "granny/laugh", [string.Empty]);
            context.QueuePacket(new PacketSendEmote(emote));
            AddGhostEmote(player, emote);
        }
        else if (MInput.Keyboard.Pressed(Keys.D2))
        {
            string text = "妈妈";
            context.QueuePacket(new PacketSendEmoteText(text));
            AddGhostEmote(player, text);
        }
        else if (MInput.Keyboard.Pressed(Keys.D3))
        {
            string text = "？";
            context.QueuePacket(new PacketSendEmoteText(text));
            AddGhostEmote(player, text);
        }
    }

    private void Context_EmoteReceived(OnlinePlayer player, EmoteData emote)
    {
        int id = player.ID;
        if (context.MainComponent.TryGetGhost(id, out var ghost))
        {
            if (ghost.Scene is not null)
                AddGhostEmote(ghost, emote);
        }
        else
        {
            Logger.Warn(
                $"{nameof(MiaoNet)}/{nameof(EmoteComponent)}",
                $"No ghost for player {player.Info} can be attached with emote {emote.Prefix}/{emote.Frames[0]}."
            );
        }
    }

    private void Context_EmoteTextReceived(OnlinePlayer player, string text)
    {
        int id = player.ID;
        if (context.MainComponent.TryGetGhost(id, out var ghost))
        {
            if (ghost.Scene is not null)
                AddGhostEmote(ghost, text);
        }
        else
        {
            Logger.Warn(
                $"{nameof(MiaoNet)}/{nameof(EmoteComponent)}",
                $"No ghost for player {player.Info} can be attached with emote text \"{text}\"."
            );
        }
    }

    private static void AddGhostEmote(Entity target, EmoteData emote)
    {
        MiaoNetGhostEmote ghostEmote = new(target, new BakedEmoteData(emote));
        target.Scene.Add(ghostEmote);
    }

    private static void AddGhostEmote(Entity target, string text)
    {
        MiaoNetGhostEmote ghostEmote = new(target, text);
        target.Scene.Add(ghostEmote);
    }
}
