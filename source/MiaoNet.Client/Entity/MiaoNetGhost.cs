using System.Diagnostics;
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
            MiaoNetFont.DrawGhostName(
                text, 
                SceneAs<Level>().WorldToScreen(worldPos),
                Color.White with { A = (byte)(255f * (MiaoNetModule.Settings.NameOpacity / 10.0f)) }
            );
        }
    }

    private readonly PlayerSprite playerSprite;
    private readonly PlayerHair playerHair;
    private readonly NameTag nameTag;

    private Facings facing;
    private int dashes;
    private float flashTimer;

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
        Tag = Tags.Persistent | Tags.TransitionUpdate | Tags.FrozenUpdate | Tags.PauseUpdate | Tags.Global;
        PlayerID = id;
        Name = name;
        GraphicsInfo = playerGraphicsInfo;
        facing = Facings.Right;
        playerSprite = new PlayerSprite(GraphicsInfo.PlayerSpriteMode);
        playerHair = new PlayerHair(playerSprite);
        Add(playerHair);
        Add(playerSprite);
        nameTag = new(this);
        playerHair.Start();

        Position = initialState.Position;
        dashes = initialState.Dashes;
        UpdateHair();
    }

    public override void Update()
    {
        base.Update();
        if (dashes == 0)
        {
            Color target = GraphicsInfo.GetHairInfo(dashes).Color;
            playerHair.Color = Color.Lerp(playerHair.Color, target, 6f * Engine.DeltaTime);
        }
        else if (flashTimer > 0f)
        {
            flashTimer -= Engine.DeltaTime;
            playerHair.Color = Color.White;
        }
        else
        {
            playerHair.Color = GraphicsInfo.GetHairInfo(dashes).Color;
        }
        if (Scene.Paused)
            playerHair.AfterUpdate();
    }

    public void OnStartDash()
    {
    }

    public void OnEndDash()
    {

    }

    public void OnDashesChange(int dashes)
    {
        flashTimer = 0.12f;
        this.dashes = dashes;
        UpdateHair();
    }

    public void UpdateSprite(ushort animationFrame, string? animationID, bool faceLeft, Vector2 scale)
    {
        if (animationID is not null)
        {
            if (playerSprite.CurrentAnimationID != animationID)
                playerSprite.Play(animationID, true);
            playerSprite.SetAnimationFrame(animationFrame);
        }
        playerHair.Facing = facing = faceLeft ? Facings.Left : Facings.Right;
        playerSprite.Scale = scale;
    }

    private void UpdateHair()
    {
        playerSprite.HairCount = GraphicsInfo.GetHairInfo(dashes).Length;
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