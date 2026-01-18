using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

partial class MainComponent
{
    private MiaoNetGhost? heldPlayerGhost;
    private MiaoNetGhost? heldByPlayerGhost;

    private void CleanUpInteractions()
    {
        CleanUpHeldBy(Engine.Scene.Tracker.GetEntity<Player>(), null);
        heldPlayerGhost = null;
        heldByPlayerGhost = null;
    }

    private static void OnHeldByPlayerFrame(Level level, MiaoNetGhost ghost)
    {
        var player = level.Tracker.GetEntity<Player>();
        player?.Position = ghost.Position + ghost.HoldableOffset!.Value;
    }

    private static void OnHeldBy(Player? player)
    {
        if (player is null)
            return;
        player.StateMachine.State = Player.StFrozen;
        player.Collidable = false;
        player.Speed = Vector2.Zero;
        player.DummyGravity = false;
        player.ForceCameraUpdate = true;
        player.Sprite.Play("idle", true);
    }

    private void CleanUpHeldBy(Player? player, Vector2? force)
    {
        if (player is not null)
        {
            player.StateMachine.State = Player.StNormal;
            if (force is not null)
                player.Speed = force.Value * 296f;
            player.Collidable = true;
        }
        heldByPlayerGhost = null;
    }

    private void UpdateInteractions(Level level, Player player)
    {
        // ensure screen transitions
        if (heldByPlayerGhost is not null)
            level.EnforceBounds(player);

        // if we're holding other player
        MiaoNetGhost? holdingGhost = null;
        if (player.Holding?.Entity is MiaoNetGhost ghost)
        {
            if (heldByPlayerGhost == ghost)
            {
                // we're holding the one who were holding us, don't do this
                player.Drop();
            }
            else
            {
                holdingGhost = ghost;

                // we are holding someone that is dead or paused
                if (ghost is { Dead: true } or not { Player.OnlineStatus: PlayerOnlineStatus.Normal })
                    player.Drop();
            }
        }

        // we're paused, don't be held by or hold someone
        if (level.Paused)
        {
            player.Drop();
            CleanUpHeldBy(player, null);
        }

        // we're being held
        if (heldByPlayerGhost is not null)
        {
            // other player is dead or went to another map or disconnected or paused
            if (heldByPlayerGhost is { Dead: true }
                or { Scene: null }
                or not { Player.OnlineStatus: PlayerOnlineStatus.Normal }
            )
            {
                CleanUpHeldBy(player, null);
            }
            else if (!level.Paused && Input.Jump.Pressed)
            {
                // jump out
                Input.Jump.ConsumePress();
                context.QueuePacket(new PacketPlayerGrabJumpOut(heldByPlayerGhost.Player.ID));
                player.Jump();
                CleanUpHeldBy(player, null);
            }
        }

        // check and send the packets
        MiaoNetGhost? curHeldPlayerGhost = null;
        if (holdingGhost is not null)
            curHeldPlayerGhost = holdingGhost;
        if (curHeldPlayerGhost != heldPlayerGhost)
        {
            SafeGuard.Assert(curHeldPlayerGhost is not null || heldPlayerGhost is not null);
            if (curHeldPlayerGhost is not null)
                context.QueuePacket(new PacketPlayerGrabPlayer(curHeldPlayerGhost.Player.ID)); // grab
            else if (heldPlayerGhost is not null)
                context.QueuePacket(new PacketPlayerGrabPlayer(heldPlayerGhost.Player.ID, heldPlayerGhost.LastReleaseForce)); // release
            heldPlayerGhost = curHeldPlayerGhost;
        }
    }

    private void Context_PlayerGrabPlayer(OnlinePlayer player, Vector2? force)
    {
        if (Engine.Scene is not Level level)
            return;

        if (force is null)
        {
            // someone held us
            if (heldByPlayerGhost is not null)
            {
                // we have been held already

                // TODO maybe we should broadcast the grab state to all players?
                context.QueuePacket(new PacketPlayerGrabJumpOut(player.ID));
            }
            else
            {
                // let them hold us

                heldByPlayerGhost = ghosts[player.ID];
                OnHeldBy(level.Tracker.GetEntity<Player>());
            }
        }
        else
        {
            // someone released us
            if (heldByPlayerGhost is not null && heldByPlayerGhost.Player.ID == player.ID)
            {
                CleanUpHeldBy(level.Tracker.GetEntity<Player>(), force);
            }
        }
    }

    private void Context_PlayerGrabJumpOut(OnlinePlayer player)
    {
        if (Engine.Scene is not Level level)
            return;

        // someone jumped out of our holding
        if (player.ID == heldPlayerGhost?.Player.ID)
            level.Tracker.GetEntity<Player>()?.Drop();
    }

#if DEBUG
    public override void Render()
    {
        MiaoNetFont.DrawOutline(
            $"curHeld: {heldPlayerGhost?.Player.Info}\n" +
            $"curHeldBy: {heldByPlayerGhost?.Player.Info}",
            new(0f, 150f),
            Vector2.Zero,
            Vector2.One,
            Color.White
        );
    }
#endif
}
