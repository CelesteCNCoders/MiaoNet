using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

[Tracked]
public sealed class GhostFollower : MiaoNetGhostEntity
{
    private readonly bool spriteFallbacked;
    private readonly Sprite sprite;
    private readonly BloomPoint? bloomPoint;
    private readonly VertexLight? vertexLight;
    private readonly FollowerType type;

    public Follower Follower { get; }

    public GhostFollower(MiaoNetGhost ghost, Vector2 offset, FollowerType type, string spriteID)
        : base(ghost.Position + offset)
    {
        this.type = type;
        Tag |= ghost.Tag;
        Depth = ghost.Depth;
        Add(Follower = new() { MoveTowardsLeader = false });

        if (GFX.SpriteBank.SpriteData.ContainsKey(spriteID))
        {
            sprite = GFX.SpriteBank.Create(spriteID);
            sprite.Active = false;
        }
        else
        {
            sprite = GFX.SpriteBank.Create("flutterBird");
            sprite.Play("idle");
            spriteFallbacked = true;
        }
        Add(sprite);
        Add(new MirrorReflection());
        if (type is FollowerType.Strawberry or FollowerType.StrawberrySeed)
        {
            Add(bloomPoint = new BloomPoint(1f, 12f));
            Add(vertexLight = new VertexLight(Color.White, 1f, 16, 24));
        }
        else if (type == FollowerType.Key)
        {
            Add(vertexLight = new VertexLight(Color.White, 1f, 32, 48));
        }
    }

    public override void Update()
    {
        base.Update();
        var settings = MiaoNetModule.Settings;
        float alphaFactor = 1f;

        if (settings.PlayerFollowersVisibility == RemotePlayerVisibility.CustomAlpha)
        {
            bool targetMatch = settings.PlayerFollowersCustomTarget == FollowerTargetType.All ||
                               (settings.PlayerFollowersCustomTarget == FollowerTargetType.CustomOnly && type == FollowerType.Custom);
            if (targetMatch)
                alphaFactor = settings.PlayerFollowersCustomOpacityValue;
        }
        else if (settings.PlayerFollowersVisibility == RemotePlayerVisibility.DistanceBased)
        {
            bool targetMatch = settings.PlayerFollowersDistanceTarget == FollowerTargetType.All ||
                               (settings.PlayerFollowersDistanceTarget == FollowerTargetType.CustomOnly && type == FollowerType.Custom);

            if (targetMatch && Scene.Tracker.GetEntity<Player>() is Player player)
            {
                float dist = Vector2.Distance(Position, player.Position);
                float fadeDist = Math.Max(0, dist - settings.PlayerFollowersDistanceRadius);
                float fadeRange = settings.PlayerFollowersDistanceFadeRadius;
                alphaFactor = Calc.Clamp(fadeDist / fadeRange, 0f, 1f);
            }
        }

        sprite.Color = Color.White * alphaFactor;
        float v = settings.PlayerOpacityValue * alphaFactor;
        if (bloomPoint != null) bloomPoint.Alpha = v;
        if (vertexLight != null) vertexLight.Alpha = v;
    }

    public void UpdateSprite(string animationID, int animationFrame)
    {
        // TODO should we tell server?
        if (spriteFallbacked)
            return;
        if (sprite.Has(animationID))
        {
            sprite.Play(animationID);
            sprite.SetAnimationFrame(animationFrame);
        }
    }

    public override void GhostRender()
        => BaseRender();
}
