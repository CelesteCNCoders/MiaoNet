namespace Celeste.Mod.MiaoNet;

public sealed partial class MainComponent
{
    private void CleanUpWatching()
    {
        StopWatching();
    }

    private void UpdateWatching(Level level, Player player)
    {
        if (playerWatching is not null)
        {
            if (playerWatching.State is null)
            {
                StopWatching();
                return;
            }
            player.Visible = false;
            player.Position = playerWatching.State!.Position;
            player.StateMachine.State = Player.StFrozen;
            player.ForceCameraUpdate = true;
            player.DummyGravity = false;
            level.EnforceBounds(player);
        }
    }

    public void StartWatching(OnlinePlayer player)
    {
        playerWatching = player;
    }

    public OnlinePlayer? StopWatching()
    {
        OnlinePlayer? player = playerWatching;
        playerWatching = null;

        if (player is not null)
        {
            var level = Engine.Scene as Level ?? (Engine.Scene as AssetReloadHelper)?.OrigScene as Level;
            var playerEntity = level?.Tracker.GetEntity<Player>();
            if (playerEntity is not null)
            {
                playerEntity.Visible = true;
                playerEntity.StateMachine.State = Player.StNormal;
                playerEntity.ForceCameraUpdate = false;
            }
        }

        return player;
    }
}
