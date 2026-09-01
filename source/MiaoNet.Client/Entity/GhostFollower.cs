using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

[Tracked]
public sealed class GhostFollower : MiaoNetGhostEntity
{
    private readonly MiaoNetGhost owner;
    private readonly bool spriteFallbacked;
    private readonly Sprite sprite;
    private readonly BloomPoint? bloomPoint;
    private readonly VertexLight? vertexLight;
    private bool remotePresentationSuppressed;

    internal FollowerType FollowerType { get; }
    internal bool RemotePresentationSuppressed => remotePresentationSuppressed;

    public override bool WatchPresentationFocus => owner.WatchFocus;

    public GhostFollower(MiaoNetGhost ghost, Vector2 offset, FollowerType type, string spriteID)
        : base(ghost.Position + offset)
    {
        owner = ghost;
        FollowerType = type;
        Tag |= ghost.Tag;
        Depth = Depths.Player + 1;

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
        const float SizeLimit = 40f;
        float scale = Math.Min(1f, SizeLimit / Math.Max(sprite.Width, sprite.Height));
        sprite.Scale = Vector2.One * scale;
        Add(sprite);
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
        float v = remotePresentationSuppressed ? 0f : EffectiveOpacity;
        bloomPoint?.Alpha = v;
        vertexLight?.Alpha = v;
    }

    internal void SetRemotePresentationSuppressed(bool suppressed)
        => remotePresentationSuppressed = suppressed;

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
    {
        if (!remotePresentationSuppressed)
            BaseRender();
    }
}
