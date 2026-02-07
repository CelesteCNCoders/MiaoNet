namespace Celeste.Mod.MiaoNet;

public enum JumpthruType
{
    Default,
    Wood,
    Cliffside,
    Temple,
    TempleB,
    Dream,
    Reflection,
    Core,
    Moon
}

[Tracked]
public sealed class GroupPhotoPlatform : JumpthruPlatform
{
    private const int PlatformWidth = 16;

    private bool confirmed;

    public GroupPhotoPlatform()
        : base(Vector2.Zero, PlatformWidth, MapJumpthruType(MiaoNetModule.Settings.GroupPhotoPlatformType))
    {
        Depth = Depths.Top;
    }

    public override void Added(Scene scene)
    {
        base.Added(scene);
        UpdatePosition((Level)scene);
    }

    public override void Update()
    {
        base.Update();
        if (confirmed)
        {
            if (MInput.Mouse.PressedRightButton)
            {
                confirmed = false;
                Reset();
            }
            else if (MInput.Mouse.PressedMiddleButton)
            {
                Visible = !Visible;
            }
        }
        else
        {
            UpdatePosition(SceneAs<Level>());
            if (MInput.Mouse.PressedLeftButton)
            {
                confirmed = true;
                var player = SceneAs<Level>().Tracker.GetEntity<Player>();
                if (player is not null)
                {
                    player.Position = Position + Vector2.UnitX * PlatformWidth / 2f;
                    player.Speed = Vector2.Zero;
                    player.StateMachine.State = Player.StNormal;
                }
            }
        }
    }

    private void UpdatePosition(Level level)
    {
        Vector2 mPos = MInput.Mouse.Position;
        Position = level.ScreenToWorld(mPos) - Vector2.UnitX * PlatformWidth / 2f;
        Position = Calc.Round(Position);
    }

    private void Reset()
    {
        overrideTexture = MapJumpthruType(MiaoNetModule.Settings.GroupPhotoPlatformType);
        Components.RemoveAll<Image>();
        var scene = Scene;
        RemoveSelf();
        scene.Add(this);
    }

    private static string MapJumpthruType(JumpthruType jumpthruType) => jumpthruType switch
    {
        JumpthruType.Wood => "wood",
        JumpthruType.Cliffside => "cliffside",
        JumpthruType.Temple => "temple",
        JumpthruType.TempleB => "templeB",
        JumpthruType.Dream => "dream",
        JumpthruType.Reflection => "reflection",
        JumpthruType.Core => "core",
        JumpthruType.Moon => "moon",
        _ => "default"
    };
}
