using MiaoNet.Shared;

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

            if (playerWatching.GlobalFlags.HasFlag(PlayerGlobalFlags.Watching))
            {
                context.ChatComponent.AddLocalChat(MiaoNetChatText.CreateCommandTip(
                    PFormat.Format(
                        Dialog.Get("miaonet_commands_watch_others_watching"),
                        playerWatching.Info.Name
                    )
                ));
                StopWatching();
                return;
            }

            var selfLoc = ClientState.Self.Location;
            var otherLoc = playerWatching.Location;
            if (selfLoc.MapRoom != otherLoc.MapRoom && !otherLoc.IsInDebugMap && level.transition is null)
            {
                var otherPos = playerWatching.State.Position;
                var session = level.Session;
                var data = session.MapData.Get(otherLoc.MapRoom);
                Vector2 newRoomSpawnPoint = data.Spawns.ClosestTo(otherPos);
                session.RespawnPoint = newRoomSpawnPoint;
                var p = player.Position;
                player.Position = newRoomSpawnPoint;

                level.TransitionTo(data, (player.Position - p).SafeNormalize());
            }
            player.Visible = false;
            player.StateMachine.State = Player.StFrozen;

            if (level.InCutscene && !level.SkippingCutscene)
                level.SkipCutscene();

            const int W = Celeste.GameWidth;
            const int H = Celeste.GameHeight;

            var cam = level.Camera;

            Vector2 target = playerWatching.State.Position;
            Vector2 camTarget = target - new Vector2(W, H) / 2f;
            camTarget.X = MathHelper.Clamp(camTarget.X, level.Bounds.Left, level.Bounds.Right - W);
            camTarget.Y = MathHelper.Clamp(camTarget.Y, level.Bounds.Top, level.Bounds.Bottom - H);
            cam.Position = Calc.Approach(cam.Position, camTarget, ((cam.Position - camTarget).Length() * 4f) * Engine.RawDeltaTime);
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

    private static void GotoLevel(Level level, Player player, Vector2 at)
    {
        var session = level.Session;
        var data = session.MapData.GetAt(at);
        session.Level = data.Name;
        session.RespawnPoint = data.Spawns.ClosestTo(at);
        player.Position = session.RespawnPoint.Value;
        level.LoadLevel(Player.IntroTypes.Transition);
    }
}
