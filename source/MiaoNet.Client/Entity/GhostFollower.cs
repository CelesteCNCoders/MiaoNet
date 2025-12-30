using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

[Tracked]
public sealed class GhostFollower : Entity
{
    private readonly Sprite sprite;
    private readonly BloomPoint? bloomPoint;
    private readonly VertexLight? vertexLight;

    public Follower Follower { get; }

    public GhostFollower(MiaoNetGhost ghost, Vector2 offset, FollowerType type, string animationID)
        : base(ghost.Position + offset)
    {
        Visible = false;
        Tag |= ghost.Tag;
        Add(Follower = new() { MoveTowardsLeader = false });

        Add(sprite = GFX.SpriteBank.Create(animationID));
        sprite.Active = false;
        Add(new MirrorReflection() { IgnoreEntityVisible = true });
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
        bloomPoint?.Alpha = MiaoNetModule.Settings.PlayerOpacityValue;
        vertexLight?.Alpha = MiaoNetModule.Settings.PlayerOpacityValue;
    }

    public void UpdateSprite(string animation, int animationFrame)
    {
        if (sprite.CurrentAnimationID != animation)
            sprite.Play(animation);
        sprite.SetAnimationFrame(animationFrame);
    }
}
