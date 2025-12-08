using MiaoNet.Shared;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.MiaoNet;

public sealed class EmoteComponent : MiaoNetComponent
{
    public EmoteComponent(MiaoNetContext context)
        : base(context)
    {
        context.EmoteReceived += Context_EmoteReceived;
    }

    public override void Update()
    {
        if (Engine.Scene is not Level level)
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
                $"No ghost for player {player.Info} can be attached with emote {emote.Prefix}/{emote.Frames[0]}"
            );
        }
    }

    private static void AddGhostEmote(Entity target, EmoteData emote)
    {
        MiaoNetGhostEmote ghostEmote = new(target, new BakedEmoteData(emote));
        target.Scene.Add(ghostEmote);
    }
}
