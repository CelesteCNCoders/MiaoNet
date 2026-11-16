namespace Celeste.Mod.MiaoNetShootGame;

/// <summary>
/// 一颗子弹。每个人在自己的客户端上模拟收到的每一颗子弹, 但"有没有打中"只由可能被打中的那个人判定:
/// 只有他自己的客户端知道自己的碰撞盒此刻在哪。
/// <para>
/// 所以 <see cref="ownerID"/> 为 null 的子弹(自己打的)根本不做命中判定 —— 打不到自己。
/// </para>
/// </summary>
internal sealed class Bullet : Entity
{
    private const float Speed = 420f;

    /// <summary>没人管的子弹到点消失, 免得打上天的子弹一直留在场景里。</summary>
    private const float Lifetime = 2.5f;

    private const float Size = 8f;

    private readonly Vector2 direction;

    /// <summary>凶手。null 表示这颗子弹是自己打的。</summary>
    private readonly int? ownerID;

    private float age;

    private Bullet(Vector2 position, Vector2 direction, int? ownerID)
    {
        Position = position;
        this.direction = direction;
        this.ownerID = ownerID;

        Depth = Depths.Player + 2;
    }

    public static void Spawn(Level level, Vector2 position, Vector2 direction, int? ownerID)
        => level.Add(new Bullet(position, direction, ownerID));

    public override void Update()
    {
        base.Update();

        age += Engine.DeltaTime;
        if (age >= Lifetime)
        {
            RemoveSelf();
            return;
        }

        Vector2 from = Position;
        Position += direction * Speed * Engine.DeltaTime;

        Level level = SceneAs<Level>();

        // 用线段而不是点: 一帧能飞七个像素, 点判定会穿墙。
        if (level.CollideCheck<Solid>(from, Position))
        {
            RemoveSelf();
            return;
        }

        if (ownerID is int killerID && level.CollideCheck<Player>(HitboxAt(Position)))
        {
            MiaoNetShootGameModule.OnLocalPlayerHit(killerID, direction);
            RemoveSelf();
        }
    }

    public override void Render()
    {
        base.Render();
        Draw.Rect(Position - Vector2.One * (Size / 2f), Size, Size, Color.Red);
    }

    private static Rectangle HitboxAt(Vector2 position)
        => new(
            (int)MathF.Floor(position.X - Size / 2f),
            (int)MathF.Floor(position.Y - Size / 2f),
            (int)Size,
            (int)Size
        );
}
