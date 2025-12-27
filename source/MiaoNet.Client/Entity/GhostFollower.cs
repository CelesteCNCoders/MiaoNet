using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

[Tracked]
public sealed class GhostFollower : Entity
{
    private readonly Sprite sprite;

    public Follower Follower { get; }

    public GhostFollower(MiaoNetGhost ghost, Vector2 offset, FollowerType type, string animationID)
        : base(ghost.Position + offset)
    {
        Visible = false;
        Tag |= ghost.Tag;
        Add(Follower = new());

        Add(sprite = GFX.SpriteBank.Create(animationID));
        sprite.Active = false;
        Add(new MirrorReflection());
        if (type is FollowerType.Strawberry or FollowerType.StrawberrySeed)
        {
            Add(new BloomPoint(1f, 12f));
            Add(new VertexLight(Color.White, 1f, 16, 24));
        }
        else if (type == FollowerType.Key)
        {
            Add(new VertexLight(Color.White, 1f, 32, 48));
        }
    }

    public void UpdateSprite(string animation, int animationFrame)
    {
        if (sprite.CurrentAnimationID != animation)
            sprite.Play(animation);
        sprite.SetAnimationFrame(animationFrame);
    }
}
