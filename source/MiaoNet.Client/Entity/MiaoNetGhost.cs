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
                Color.White * (MiaoNetModule.Settings.NameOpacity / 10.0f)
            );
        }
    }

    private PlayerSprite playerSprite;
    private readonly PlayerHair playerHair;
    private readonly NameTag nameTag;

    private Facings facing;
    private int dashes;
    private float flashTimer;
    private bool respawning;
    private float deadEase;

    public OnlinePlayer Player { get; set; }

    public string Name { get; set; }

    [AllowNull]
    public PlayerGraphicsInfo GraphicsInfo
    {
        get => field;
        set => field = value ?? PlayerGraphicsInfo.Default;
    }

    public MiaoNetGhost(OnlinePlayer player, string name, [AllowNull] PlayerGraphicsInfo playerGraphicsInfo, PlayerState initialState)
    {
        Tag = Tags.Persistent | Tags.TransitionUpdate | Tags.FrozenUpdate | Tags.PauseUpdate | Tags.Global;
        Player = player;
        Name = name;
        GraphicsInfo = playerGraphicsInfo;
        facing = Facings.Right;
        playerSprite = new PlayerSprite(initialState.PlayerSpriteMode);
        playerSprite.Active = false;

        playerHair = new PlayerHair(playerSprite);
        Add(playerHair);
        Add(playerSprite);
        nameTag = new(this);
        playerHair.Start();

        ApplyState(initialState);
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

    public void ApplyState(PlayerState state)
    {
        if (playerSprite.Mode != state.PlayerSpriteMode)
        {
            var pAnim = playerSprite.CurrentAnimationID;
            var pFrame = playerSprite.CurrentAnimationFrame;
            playerSprite.RemoveSelf();
            playerSprite = new(state.PlayerSpriteMode);
            if (playerSprite.Has(pAnim))
            {
                playerSprite.Play(pAnim);
                playerSprite.SetAnimationFrame(pFrame);
            }
            playerHair.Sprite = playerSprite;
        }
        dashes = state.Dashes;
        Position = state.Position;
    }

    public void OnStartDash()
    {
        SceneAs<Level>().Displacement.AddBurst(Center, 0.4f, 8f, 64f, 0.5f, Ease.QuadOut, Ease.QuadOut);
    }

    public void OnEndDash()
    {
    }

    public void OnDied()
    {
        playerSprite.Visible = playerHair.Visible = false;
        SceneAs<Level>().Displacement.AddBurst(Position, 0.3f, 0f, 80f);
        Add(new DeathEffect(playerHair.Color));
        Depth = Depths.Top;
    }

    // TODO the respawned timing is not that accurate
    public void OnRespawning()
    {
        respawning = true;
        deadEase = 1f;
        var tween = Tween.Set(this, Tween.TweenMode.Oneshot, 0.6f * (Engine.RawDeltaTime / Player.State!.DeltaTime), null,
            t =>
            {
                deadEase = 1f - t.Eased;
            },
            t =>
            {
                respawning = false;
                playerSprite.Visible = playerHair.Visible = true;
                Depth = Depths.Player;
            }
        );
        tween.UseRawDeltaTime = true;
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
        if (respawning)
        {
            DeathEffect.Draw(Position, playerHair.Color, deadEase);
        }
    }
}