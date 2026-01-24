using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

partial class MainComponent
{
    private MiaoNetGhost? holdingPlayerGhost;
    private MiaoNetGhost? heldByPlayerGhost;

    public bool HoldingOthers => holdingPlayerGhost is not null;
    public bool HeldByOthers => heldByPlayerGhost is not null;

    private void CleanUpInteractions(Level? level)
    {
        Player? player = level?.Tracker.GetEntity<Player>();
        if (player is not null && heldByPlayerGhost is not null)
            CleanUpHeldBy(player, null);
        holdingPlayerGhost = null;
        heldByPlayerGhost = null;
    }

    private static void OnHeldByPlayerFrame(Level level, MiaoNetGhost ghost)
    {
        var player = level.Tracker.GetEntity<Player>();
        player?.Position = Calc.Round(ghost.Position + ghost.HoldableOffset!.Value);
    }

    private static void OnHeldBy(Player? player)
    {
        if (player is null)
            return;
        player.StateMachine.State = Player.StFrozen;
        player.Speed = Vector2.Zero;
        player.DummyGravity = false;
        player.ForceCameraUpdate = true;
        player.Sprite.Play("idle");
    }

    private void CleanUpHeldBy(Player player, Vector2? force)
    {
        heldByPlayerGhost = null;
        player.StateMachine.State = Player.StNormal;
        if (force is not null)
            player.Speed = force.Value * 296f;
    }

    private void UpdateInteractions(Level level, Player player)
    {
        // ensure screen transitions
        // also see MiaoNetModule On.Celeste.Player.TransitionTo Hook
        if (heldByPlayerGhost is not null)
        {
            level.EnforceBounds(player);
            OnHeldBy(player);
        }

        // if we're holding other player
        MiaoNetGhost? holdingGhost = null;
        if (player.Holding?.Entity is MiaoNetGhost ghost)
        {
            if (heldByPlayerGhost == ghost || level.Paused)
            {
                // we're holding the one who were holding us, don't do this
                // or it's paused now, force drop too
                player.Drop();
            }
            else
            {
                holdingGhost = ghost;

                // we are holding someone that is dead or paused
                if (ghost is { Dead: true } or not { OnlinePlayer.OnlineStatus: PlayerOnlineStatus.Normal })
                    player.Drop();
            }
        }

        // if we're being held
        if (heldByPlayerGhost is not null)
        {
            // other player is (dead) or (went to another map) or (disconnected) or (paused)
            // or the level is paused
            if (heldByPlayerGhost is { Dead: true }
                or { Scene: null }
                or not { OnlinePlayer.OnlineStatus: PlayerOnlineStatus.Normal }
                || level.Paused
            )
            {
                CleanUpHeldBy(player, null);
            }
            else if (!level.Paused && Input.Jump.Pressed)
            {
                // level is not paused and we pressed jump
                // jump out
                Input.Jump.ConsumePress();
                context.QueuePacket(new PacketPlayerGrabJumpOut(heldByPlayerGhost.OnlinePlayer.ID));
                player.Jump();
                CleanUpHeldBy(player, null);
            }
        }

        // check and send the packets
        MiaoNetGhost? curHeldPlayerGhost = null;
        if (holdingGhost is not null)
            curHeldPlayerGhost = holdingGhost;
        if (curHeldPlayerGhost != holdingPlayerGhost)
        {
            SafeGuard.Assert(curHeldPlayerGhost is not null || holdingPlayerGhost is not null);
            if (curHeldPlayerGhost is not null)
                context.QueuePacket(new PacketPlayerGrabPlayer(curHeldPlayerGhost.OnlinePlayer.ID)); // grab
            else if (holdingPlayerGhost is not null)
                context.QueuePacket(new PacketPlayerGrabPlayer(holdingPlayerGhost.OnlinePlayer.ID, holdingPlayerGhost.LastReleaseForce)); // release
            holdingPlayerGhost = curHeldPlayerGhost;
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
            if (heldByPlayerGhost is not null && heldByPlayerGhost.OnlinePlayer.ID == player.ID)
            {
                Player? playerEntity = level.Tracker.GetEntity<Player>();
                if (playerEntity is not null)
                    CleanUpHeldBy(playerEntity, force);
            }
        }
    }

    private void Context_PlayerGrabJumpOut(OnlinePlayer player)
    {
        if (Engine.Scene is not Level level)
            return;

        // someone jumped out of our holding
        if (player.ID == holdingPlayerGhost?.OnlinePlayer.ID)
            level.Tracker.GetEntity<Player>()?.Drop();
    }

#if DEBUG
    public override void Render()
    {
        MiaoNetFont.DrawOutline(
            $"holding: {holdingPlayerGhost?.OnlinePlayer.Info}\n" +
            $"heldBy: {heldByPlayerGhost?.OnlinePlayer.Info}",
            new(0f, 150f),
            Vector2.Zero,
            Vector2.One,
            Color.White
        );
    }
#endif
}
