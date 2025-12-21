using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using MiaoNet.Shared;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MiaoNet;

public sealed class MiaoNetGhost : Entity
{
    private PlayerSprite playerSprite;
    private readonly PlayerHair playerHair;
    private readonly GhostNameTag nameTag;

    private Facings facing;
    private int dashes;
    private int lastDashedDashes;
    private bool dashing;
    private float lastDashDirection;
    private float flashTimer;
    private bool respawning;
    private float deadEase;
    private bool starFlying;

    private HoldableType lastHoladableType;
    private Sprite? holdableSprite;

    private IdleHover? idleHover;

    public OnlinePlayer Player { get; set; }

    public string Name { get; set; }

    [AllowNull]
    public PlayerGraphicsInfo GraphicsInfo
    {
        get => field;
        set => field = value ?? PlayerGraphicsInfo.Default;
    }

    public MiaoNetGhost(
        OnlinePlayer player,
        string name,
        [AllowNull] PlayerGraphicsInfo playerGraphicsInfo,
        PlayerState initialState
    )
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
        UpdateHairCount();
    }

    public override void Update()
    {
        base.Update();

        if (starFlying)
        {
            playerHair.Color = GraphicsInfo.FeatherHairInfo.Color;
        }
        else if (dashes == 0)
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
        if (Player.OnlineStatus == PlayerOnlineStatus.Normal)
        {
            if (Scene.Paused)
                playerHair.AfterUpdate();
            if (dashing)
            {
                ParticleType type = dashes >= 1 ? global::Celeste.Player.P_DashB : global::Celeste.Player.P_DashA;
                SceneAs<Level>().ParticlesFG.Emit(
                    type,
                    Position + Calc.Random.Range(Vector2.One * -2f, Vector2.One * 2f),
                    lastDashDirection
                );
            }
        }
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
        lastDashedDashes = dashes;
        Position = state.Position;
    }

    public void UpdateDashing(bool dashing, float dashDirection, bool dashesChanged, int dashes)
    {
        if (dashesChanged)
        {
            this.dashes = dashes;
            if (!starFlying)
            {
                flashTimer = 0.12f;
                UpdateHairCount();
            }
        }

        if (dashing)
            lastDashDirection = dashDirection;
        bool pDashing = this.dashing;
        this.dashing = dashing;
        if (!pDashing && dashing)
        {
            lastDashedDashes = this.dashes;

            PauseUpdatedBurst.AddBurstTo(SceneAs<Level>().Displacement, Center, 0.4f, 8f, 64f, 0.5f, Ease.QuadOut);
            AddTrail(this.dashes);
        }
        else if (pDashing && !dashing)
        {
            AddTrail(lastDashedDashes);
        }
    }

    private void AddTrail(int dashes)
    {
        var snap = TrailManager.Add(
            Position,
            playerSprite, playerHair,
            Vector2.One, GraphicsInfo.GetHairInfo(dashes).Color,
            Depth + 1, useRawDeltaTime: true
        );
        snap.Tag |= Tag;
    }

    public void OnDied()
    {
        playerSprite.Visible = playerHair.Visible = false;
        PauseUpdatedBurst.AddBurstTo(SceneAs<Level>().Displacement, Position, 0.3f, 0f, 80f);
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

    public void NotifyStarFlying(bool starFlying)
    {
        if (this.starFlying != starFlying)
        {
            if (starFlying)
                playerSprite.HairCount = GraphicsInfo.FeatherHairInfo.Length;
            else
                UpdateHairCount();
            this.starFlying = starFlying;
        }
    }

    public void UpdateSprite(string? animID, ushort animFrame, bool faceLeft, Vector2 scale)
    {
        if (animID is not null)
        {
            playerSprite.Play(animID);
            playerSprite.SetAnimationFrame(animFrame);
        }
        playerHair.Facing = facing = faceLeft ? Facings.Left : Facings.Right;
        playerSprite.Scale = scale;
    }

    public void UpdateNoHoldable()
    {
        if (lastHoladableType == HoldableType.None)
            return;
        lastHoladableType = HoldableType.None;
        holdableSprite?.RemoveSelf();
        holdableSprite = null;
        return;
    }

    public void UpdateSimpleHoldable(HoldableType type)
    {
        PrepareHoldableSprite(type);
    }

    public void UpdateHoldable(HoldableType type, string? anim, ushort animFrame, Vector2 scale, float rotation)
    {
        PrepareHoldableSprite(type);

        if (type == HoldableType.Jelly)
        {
            holdableSprite!.Play(anim);
            holdableSprite.SetAnimationFrame(animFrame);
            holdableSprite.Scale = scale;
            holdableSprite.Rotation = rotation;
        }
    }

    public void OnUpdateOnlineStatus(PlayerOnlineStatus status)
    {
        if (status == PlayerOnlineStatus.Normal)
        {
            playerHair.Active = true;
            idleHover?.RemoveSelf();
            idleHover = null;
        }
        else
        {
            playerHair.Active = false;
            idleHover = new(this);
            Scene?.Add(idleHover);
        }
    }

    private void PrepareHoldableSprite(HoldableType type)
    {
        if (lastHoladableType != HoldableType.None)
            return;
        if (type == HoldableType.Theo)
        {
            holdableSprite ??= GFX.SpriteBank.Create("theo_crystal");
            Add(holdableSprite);
            holdableSprite.Scale.X = -1f;
        }
        else if (type == HoldableType.Jelly)
        {
            holdableSprite ??= GFX.SpriteBank.Create("glider");
            Add(holdableSprite);
        }
        else
        {
            return;
        }
        // TODO pick-up animation
        holdableSprite!.Position = global::Celeste.Player.CarryOffsetTarget;
        holdableSprite.Active = holdableSprite.Visible = false;
        lastHoladableType = type;
    }

    private void UpdateHairCount()
    {
        playerSprite.HairCount = GraphicsInfo.GetHairInfo(dashes).Length;
    }

    public override void Added(Scene scene)
    {
        base.Added(scene);
        scene.Add(nameTag);
        if (idleHover is not null)
            scene.Add(idleHover);
    }

    public override void Removed(Scene scene)
    {
        base.Removed(scene);
        scene.Remove(nameTag);
        idleHover?.RemoveSelf();
    }

    public override void Render()
    {
        if (lastHoladableType == HoldableType.Jelly)
        {
            holdableSprite!.DrawSimpleOutline();
            holdableSprite!.Render();
        }

        {
            playerSprite.Scale.X *= (float)facing;
            base.Render();
            playerSprite.Scale.X *= (float)facing;
        }

        if (lastHoladableType == HoldableType.Theo)
        {
            holdableSprite!.Render();
        }

        if (respawning)
        {
            DeathEffect.Draw(Position, playerHair.Color, deadEase);
        }
    }
}