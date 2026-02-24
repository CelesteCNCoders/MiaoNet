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

    public FollowerType Type => type;

    public Follower Follower { get; }

    public GhostFollower(MiaoNetGhost ghost, Vector2 offset, FollowerType type, string spriteID)
        : base(ghost.Position + offset)
    {
        this.type = type;
        Tag |= ghost.Tag;
        Depth = ghost.Depth + 1;
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

        sprite.Color = Color.White;
        
        float v = settings.PlayerOpacityValue;
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
