using System.Diagnostics.CodeAnalysis;
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
    private int dashes;

    public int PlayerID { get; set; }

    public string Name { get; set; }

    [AllowNull]
    public PlayerGraphicsInfo GraphicsInfo
    {
        get => field;
        set => field = value ?? PlayerGraphicsInfo.Default;
    }

    public MiaoNetGhost(int id, string name, [AllowNull] PlayerGraphicsInfo playerGraphicsInfo, PlayerState initialState)
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

        X = initialState.X;
        Y = initialState.Y;
        dashes = initialState.Dashes;
        UpdateHair();
    }

    public override void Update()
    {
        base.Update();
    }

    public void OnStartDash()
    {
        
    }

    public void OnEndDash()
    {
        
    }

    public void OnDashesChange(int dashes)
    {
        this.dashes = dashes;
        UpdateHair();
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

    private void UpdateHair()
    {
        playerHair.Color = GetHairColor(GraphicsInfo, dashes);
        playerSprite.HairCount = GetHairLength(GraphicsInfo, dashes);
    }

    private static Color GetHairColor(PlayerGraphicsInfo graphicsInfo, int dashes) => dashes switch
    {
        0 => graphicsInfo.Dash0Color,
        1 => graphicsInfo.Dash1Color,
        2 => graphicsInfo.Dash2Color,
        _ => graphicsInfo.Dash2Color
    };

    private static int GetHairLength(PlayerGraphicsInfo graphicsInfo, int dashes) => dashes switch
    {
        0 => graphicsInfo.Dash0HairLength,
        1 => graphicsInfo.Dash1HairLength,
        2 => graphicsInfo.Dash2HairLength,
        _ => graphicsInfo.Dash2HairLength
    };

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