using MiaoNet.Shared;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MiaoNet;

public sealed class MiaoNetGhost : Entity
{
    public sealed class NameTag : Entity
    {
        private readonly Entity entity;
        private readonly string text;

        public NameTag(Entity entity, string text)
        {
            Tag = TagsExt.SubHUD | entity.Tag;
            this.entity = entity;
            this.text = text;
        }

        public NameTag(MiaoNetGhost ghost) : this(ghost, ghost.Name)
        {
        }

        public override void Render()
        {
            base.Render();
            Vector2 worldPos = entity.Position;
            worldPos.Y -= 16f;
            MiaoNetFont.DrawGhostName(text, SceneAs<Level>().WorldToScreen(worldPos), Color.White * (MiaoNetModule.Settings.NameOpacity / 10.0f));
        }
    }

    private readonly PlayerSprite playerSprite;
    private readonly PlayerHair playerHair;
    private readonly NameTag nameTag;

    private Facings facing;

    public int PlayerID { get; set; }

    public string Name { get; set; }

    public PlayerGraphicsInfo GraphicsInfo { get; set; }

    public MiaoNetGhost(int id, string name, PlayerGraphicsInfo playerGraphicsInfo, PlayerStats initialStats)
    {
        Tag = Tags.Persistent | Tags.TransitionUpdate | Tags.FrozenUpdate | Tags.PauseUpdate;
        PlayerID = id;
        Name = name;
        GraphicsInfo = playerGraphicsInfo;
        facing = Facings.Right;
        playerSprite = new PlayerSprite(PlayerSpriteMode.Madeline);
        playerHair = new PlayerHair(playerSprite);
        Add(playerHair);
        Add(playerSprite);
        nameTag = new(this);
        playerHair.Start();

        X = initialStats.X;
        Y = initialStats.Y;
    }

    public override void Update()
    {
        base.Update();
    }

    public void ApplyGraphicsInfo()
    {

    }

    public void UpdateSprite(ushort animationFrame, string? animationID, bool faceLeft, float scaleX, float scaleY)
    {
        if (animationID is not null)
        {
            if (playerSprite.CurrentAnimationID != animationID)
                playerSprite.Play(animationID, true);
            playerSprite.SetAnimationFrame(animationFrame);
        }

        playerHair.Facing = facing = faceLeft ? Facings.Left : Facings.Right;
        playerSprite.Scale = new((float)scaleX, (float)scaleY);
    }

    public override void Added(Scene scene)
    {
        base.Added(scene);
        scene.Add(nameTag);
    }

    public override void Removed(Scene scene)
    {
        base.Removed(scene);
        scene.Remove(nameTag);
    }

    public override void Render()
    {
        playerSprite.Scale.X *= (float)facing;
        base.Render();
        playerSprite.Scale.X *= (float)facing;
    }
}