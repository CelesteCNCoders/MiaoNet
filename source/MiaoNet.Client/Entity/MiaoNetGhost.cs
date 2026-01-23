using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using FMOD.Studio;
using MiaoNet.Shared;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MiaoNet;

[Tracked]
public sealed class MiaoNetGhost : Entity
{
    private PlayerSprite playerSprite;
    private readonly PlayerHair playerHair;
    private readonly GhostNameTag nameTag;
    private readonly Leader leader;

    private VertexLight? light;

    private Facings facing;
    private int dashes;
    private int lastDashedDashes;
    private bool dashing;
    private float lastDashDirection;
    private float flashTimer;
    private bool respawning;
    private float deadEase;
    private bool dead;
    private bool starFlying;
    private bool ducking;
    // TODO sync hitbox size?
    private readonly Hitbox normalHitbox = new Hitbox(8f, 11f, -4f, -11f);
    private readonly Hitbox duckHitbox = new Hitbox(8f, 6f, -4f, -6f);
    private Hitbox hitbox;
    private readonly Holdable selfHoldable;

    private Vector2 windDirection;
    private float windHairTimer;

    private HoldableType lastHoladableType;
    private Sprite? holdableSprite;
    private Vector2? holdableOffset;

    private IdleHover? idleHover;

    private (Color, Color) pDashColorBaseA;
    private (Color, Color) pDashColorBaseB;
    private readonly ParticleType pDashA;
    private readonly ParticleType pDashB;

    public OnlinePlayer Player { get; }

    public string Name { get; }

    public bool Interactions { get; private set; }

    public bool BeingHeldLocally => selfHoldable.Holder is not null;

    public Vector2? HoldableOffset => holdableOffset;

    public Facings Facing => facing;

    public bool Dead => dead;

    public Vector2 LastReleaseForce { get; private set; }

    [AllowNull]
    public PlayerGraphicsInfo GraphicsInfo
    {
        get => field;
        set => field = value ?? PlayerGraphicsInfo.Default;
    }

    public MiaoNetGhost(
        OnlinePlayer player,
        string name,
        PlayerGraphicsInfo? playerGraphicsInfo,
        PlayerState initialState
    )
    {
        Tag = Tags.Persistent | Tags.TransitionUpdate | Tags.FrozenUpdate | Tags.PauseUpdate | Tags.Global;
        Depth = Depths.Player + 1;
        Player = player;
        Name = name;
        GraphicsInfo = playerGraphicsInfo;
        facing = Facings.Right;
        playerSprite = SafeCreatePlayerSprite(initialState.PlayerSpriteMode);
        Add(leader = new Leader(new Vector2(0f, -8f)));
        Add(new MirrorReflection());
        UpdateLightSettings(MiaoNetModule.Settings.OtherPlayersLight);

        playerHair = new PlayerHair(playerSprite);

        playerHair.Facing = facing;
        Add(playerHair);

        Add(playerSprite);
        nameTag = new(this, name);
        playerHair.Start();

        ApplyState(initialState);
        UpdateHairCount();

        pDashA = new(global::Celeste.Player.P_DashA);
        pDashB = new(global::Celeste.Player.P_DashB);
        pDashColorBaseA = (pDashA.Color, pDashA.Color2);
        pDashColorBaseB = (pDashB.Color, pDashB.Color2);

        if (player.OnlineStatus != PlayerOnlineStatus.Normal)
            OnUpdateOnlineStatus(player.OnlineStatus);

        selfHoldable = new(1f / 3f)
        {
            SlowRun = false,
            SlowFall = false,
            OnPickup = () => Depth = selfHoldable!.Entity.Depth + 1,
            OnRelease = f =>
            {
                if (f.X != 0f)
                    f.Y -= 0.4f;
                LastReleaseForce = f;
            }
        };
        Add(selfHoldable);
    }

    public override void Update()
    {
        base.Update();

        UpdateLightSettings(MiaoNetModule.Settings.OtherPlayersLight);

        bool updateOthers = Player.OnlineStatus == PlayerOnlineStatus.Normal;
        if (!updateOthers)
            return;

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
            // TODO apply others' delta time
            flashTimer -= Engine.RawDeltaTime;
            playerHair.Color = Color.White;
        }
        else
        {
            playerHair.Color = GraphicsInfo.GetHairInfo(dashes).Color;
        }
        if (windDirection.X != 0f)
        {
            // TODO apply others' delta time
            windHairTimer += Engine.RawDeltaTime * 8f;
            playerHair.StepPerSegment = new Vector2(windDirection.X * 5f, MathF.Sin(windHairTimer));
            playerHair.StepInFacingPerSegment = 0f;
            playerHair.StepApproach = 128f;
            playerHair.StepYSinePerSegment = 0f;
        }
        else if (dashes > 1)
        {
            // TODO apply others' delta time
            float timeActive = Scene.RawTimeActive;
            playerHair.StepPerSegment = new Vector2(
                MathF.Sin(timeActive * 2f) * 0.7f - ((float)facing * 3f),
                MathF.Sin(timeActive * 1f)
            );
            playerHair.StepInFacingPerSegment = 0f;
            playerHair.StepApproach = 90f;
            playerHair.StepYSinePerSegment = 1f;
            playerHair.StepPerSegment.Y += windDirection.Y * 2f;
        }
        else
        {
            playerHair.StepPerSegment = new Vector2(0f, 2f);
            playerHair.StepInFacingPerSegment = 0.5f;
            playerHair.StepApproach = 64f;
            playerHair.StepYSinePerSegment = 0f;
            playerHair.StepPerSegment.Y += windDirection.Y * 0.5f;
        }
        if (!Scene.Paused)
        {
            if (dashing && !dead)
            {
                float alpha = MiaoNetModule.Settings.PlayerOpacityValue;
                // TODO apply graphics info
                ParticleType type;
                if (lastDashedDashes == 0)
                {
                    type = pDashA;
                    type.Color = pDashColorBaseA.Item1 * alpha;
                    type.Color2 = pDashColorBaseA.Item2 * alpha;
                }
                else
                {
                    type = pDashB;
                    type.Color = pDashColorBaseB.Item1 * alpha;
                    type.Color2 = pDashColorBaseB.Item2 * alpha;
                }

                SceneAs<Level>().ParticlesFG.Emit(
                    type,
                    Position + Calc.Random.Range(Vector2.One * -2f, Vector2.One * 2f),
                    lastDashDirection
                );
            }
        }
        else
        {
            playerHair.AfterUpdate();
        }
    }

    public void UpdateLightSettings(bool enabled)
    {
        if (enabled)
        {
            if (light is null)
            {
                // TODO player duck light offset
                light = new VertexLight(new Vector2(0f, -8f), Color.White with { A = 233 }, 1f, 32, 64);
                Add(light);
            }
            light.Visible = true;
        }
        else
        {
            // remove it will lead to a vanilla crash...
            light?.Visible = false;
        }
    }

    #region state updates

    [MemberNotNull(nameof(hitbox))]
    public void ApplyState(PlayerState state)
    {
        if (playerSprite.Mode != state.PlayerSpriteMode)
        {
            var pAnim = playerSprite.CurrentAnimationID;
            var pFrame = playerSprite.CurrentAnimationFrame;
            playerSprite.RemoveSelf();
            playerSprite = SafeCreatePlayerSprite(state.PlayerSpriteMode);
            if (playerSprite.Has(pAnim))
            {
                playerSprite.Play(pAnim);
                playerSprite.SetAnimationFrame(pFrame);
            }
            playerHair.Sprite = playerSprite;
            Add(playerSprite);
            playerHair.Start();
            UpdateHairCount();
        }
        dashes = state.Dashes;
        lastDashedDashes = dashes;
        Position = state.Position;
        windDirection = state.WindDirection;
        UpdateFacing(state.FacingLeft);
        OnFollowerInitials(state.FollowerInfos);
        UpdateDucking(state.Ducking);
        UpdateWind(state.WindDirection);
    }

    private static PlayerSprite SafeCreatePlayerSprite(PlayerSpriteMode spriteMode)
    {
        PlayerSprite playerSprite;
    CreatePlayerSprite:
        try
        {
            // CelesteNet do this, same for us for compatibility
            playerSprite = new PlayerSprite(spriteMode | (PlayerSpriteMode)(1 << 31));
        }
        catch when (!Enum.IsDefined(spriteMode))
        {
            // if we're receiving a locally non-exists skin
            // use madeline as fallback
            spriteMode = PlayerSpriteMode.Madeline;
            goto CreatePlayerSprite;
        }
        playerSprite.Active = false;
        return playerSprite;
    }

    // TODO these tons of UpdateXXX method could be more maintainerable?
    public void UpdateDashing(bool dashing, float dashDirection, bool dashesChanged, int dashes)
    {
        Level level = SceneAs<Level>();

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

            if (level is not null)
            {
                if (!level.Paused)
                {
                    float alpha = MiaoNetModule.Settings.PlayerOpacityValue;
                    level.Displacement.AddBurst(Center, 0.4f, 8f, 64f, 0.5f * alpha, Ease.QuadOut);
                }
                AddTrail(this.dashes);
            }
        }
        else if (pDashing && !dashing)
        {
            if (level is not null)
                AddTrail(lastDashedDashes);
        }
    }

    public void OnFollowerInitials(FollowerInfo[] followerInfos)
    {
        CleanUpFollowers();
        foreach (var info in followerInfos)
        {
            GhostFollower gf = new(this, info.Offset, info.Type, info.SpriteID);
            gf.UpdateSprite(info.AnimationID, info.AnimationFrame);
            leader.GainFollower(gf.Follower);
            Scene?.Add(gf);
        }
    }

    public void OnFollowerDeltas(FollowerInfoDelta[] deltas)
    {
        if (deltas.Length != leader.Followers.Count)
        {
            Logger.Error(
                LT.MiaoNet,
                $"Received {deltas.Length} follower deltas but there's only {leader.Followers.Count} followers."
            );
            // let it crash
        }
        for (int i = 0; i < deltas.Length; i++)
        {
            FollowerInfoDelta delta = deltas[i];
            var gf = leader.Followers[i].EntityAs<GhostFollower>();
            gf.UpdateSprite(delta.AnimationID, delta.AnimationFrame);
            gf.Position = leader.Entity.Position + delta.Offset;
        }
    }

    private void CleanUpFollowers()
    {
        foreach (var follower in leader.Followers)
            follower.Entity.RemoveSelf();
        leader.Followers.Clear();
    }

    private void AddTrail(int dashes)
    {
        float alpha = MiaoNetModule.Settings.PlayerOpacityValue;
        var snap = TrailManager.Add(
            Position,
            playerSprite, playerHair,
            Vector2.One, GraphicsInfo.GetHairInfo(dashes).Color * alpha,
            Depth + 1, useRawDeltaTime: true
        );
        snap.Tag |= Tag;
    }

    public void OnDied()
    {
        dead = true;
        selfHoldable.Holder?.Drop();
        Collidable = false;
        playerSprite.Visible = playerHair.Visible = false;
        if (Scene is Level level)
        {
            level.Displacement.AddBurst(Position, 0.3f, 0f, 80f);
            float alpha = MiaoNetModule.Settings.PlayerOpacityValue;
            Add(new DeathEffect(playerHair.Color * alpha));
        }
        Depth = Depths.Top;
        if (MiaoNetModule.Settings.SyncAudio)
            OnPlayAudio(MiaoNetSFX.PlayerDeath);
    }

    // TODO the respawned timing is not that accurate
    public void OnRespawning()
    {
        respawning = true;
        deadEase = 1f;
        Collidable = true;
        var tween = Tween.Set(this, Tween.TweenMode.Oneshot, 0.6f, null,
            t =>
            {
                deadEase = 1f - t.Eased;
            },
            t =>
            {
                respawning = false;
                dead = false;
                playerSprite.Visible = playerHair.Visible = true;
                Depth = Depths.Player + 1;
            }
        );
        tween.UseRawDeltaTime = true;
    }

    // TODO start star flying sync?
    public void NotifyStarFlying(bool starFlying)
    {
        if (this.starFlying != starFlying)
        {
            if (starFlying)
            {
                UpdateHairCount(GraphicsInfo.FeatherHairInfo.Length);
                playerHair.DrawPlayerSpriteOutline = true;
                playerHair.SimulateMotion = false;
            }
            else
            {
                UpdateHairCount();
                playerHair.DrawPlayerSpriteOutline = false;
                playerHair.SimulateMotion = true;
            }
            this.starFlying = starFlying;

        }
    }

    public void UpdateSprite(string animID, ushort animFrame, bool facingLeft, Vector2 scale)
    {
        if (animID != string.Empty && playerSprite.Has(animID))
        {
            playerSprite.Play(animID);
            playerSprite.SetAnimationFrame(animFrame);
        }
        UpdateFacing(facingLeft);
        playerSprite.Scale = scale;
    }

    private void UpdateFacing(bool facingLeft)
    {
        playerHair.Facing = facing = facingLeft ? Facings.Left : Facings.Right;
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

    public void UpdateSimpleHoldable(HoldableType type, Vector2? offset)
    {
        PrepareHoldableSprite(type);
        if (offset is not null)
        {
            holdableOffset = offset;
            holdableSprite?.Position = holdableOffset.Value;
        }
    }

    public void UpdateHoldable(HoldableType type, Vector2? offset, string? anim, ushort animFrame, Vector2 scale, float rotation)
    {
        PrepareHoldableSprite(type);
        if (offset is not null)
        {
            holdableOffset = offset;
            holdableSprite?.Position = holdableOffset.Value;
        }

        if (type == HoldableType.Jelly)
        {
            holdableSprite!.Play(anim);
            holdableSprite.SetAnimationFrame(animFrame);
            holdableSprite.Scale = scale;
            holdableSprite.Rotation = rotation;
        }
    }

    public void UpdateWind(Vector2 wind)
    {
        windDirection = wind;
    }

    [MemberNotNull(nameof(hitbox))]
    public void UpdateDucking(bool ducking)
    {
        this.ducking = ducking;
        hitbox = ducking ? duckHitbox : normalHitbox;
        Collider = hitbox;
    }

    public void UpdateInteractions(bool interactions)
    {
        Interactions = interactions;
        UpdateCollidable();
    }

    public void OnUpdateOnlineStatus(PlayerOnlineStatus status)
    {
        if (status == PlayerOnlineStatus.Normal)
        {
            playerHair.Active = true;
            if (idleHover is not null)
                Scene?.CompletelyRemove(idleHover);
            idleHover = null;
            UpdateCollidable();
        }
        else
        {
            playerHair.Active = false;
            idleHover = new(this);
            Scene?.Add(idleHover);
            UpdateCollidable();
        }
    }

    private void UpdateCollidable()
    {
        Collidable = Interactions && MiaoNetModule.Settings.PlayerInteractions && Player.OnlineStatus == PlayerOnlineStatus.Normal;
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
        holdableSprite.Active = holdableSprite.Visible = false;
        lastHoladableType = type;
    }

    private void UpdateHairCount(int count)
    {
        playerSprite.HairCount = count;
        playerHair.AfterUpdate();
    }

    private void UpdateHairCount()
    {
        UpdateHairCount(GraphicsInfo.GetHairInfo(dashes).Length);
    }

    #endregion

    public override void Added(Scene scene)
    {
        base.Added(scene);
        scene.Add(nameTag);
        if (idleHover is not null)
            scene.Add(idleHover);
        foreach (var follower in leader.Followers)
        {
            Entity e = follower.Entity;
            if (e.Scene is null)
                scene.Add(e);
        }
    }

    public override void Removed(Scene scene)
    {
        base.Removed(scene);
        scene.Remove(nameTag);
        idleHover?.RemoveSelf();
        CleanUpFollowers();
    }

    public void OnPlayAudio(string @event)
        => OnPlayAudio(@event, null, 0f);

    public void OnPlayAudio(string @event, string? param, float paramValue)
    {
        if (Scene is not Level level || level.Paused)
            return;

        float baseValue = MiaoNetModule.Settings.OtherPlayersAudioVolumeValue;

        EventDescription eventDescription = Audio.GetEventDescription(@event);
        if (eventDescription is null)
            return;

        eventDescription.createInstance(out var instance);

        if (instance is null)
            return;

        eventDescription.is3D(out var is3D);

        // TODO prevent this earlier server-side
        if (!level.InsideCamera(Center, is3D ? 128f : 64f))
            return;

        if (is3D)
            Audio.Position(instance, Center);

        instance.setVolume(baseValue);

        if (param is not null)
            instance.setParameterValue(param, paramValue);

        instance.start();
        instance.release();
    }

    public void OnCreatedFireworks(Color color, float initialSpeed)
    {
        // TODO do not early quit when paused
        if (Scene is not Level level || level.Paused)
            return;

        if (!level.InsideCamera(Center, 128f))
            return;

        level.Add(new Fireworks(Position, color, initialSpeed));
    }

    public void GhostRender()
    {
        if (lastHoladableType == HoldableType.Theo)
        {
            holdableSprite!.Render();
        }

        {
            playerSprite.Scale.X *= (float)facing;
            base.Render();
            playerSprite.Scale.X *= (float)facing;
        }

        if (lastHoladableType == HoldableType.Jelly)
        {
            holdableSprite!.DrawSimpleOutline();
            holdableSprite!.Render();
        }

        if (respawning)
        {
            DeathEffect.Draw(Position, playerHair.Color, deadEase);
        }
    }

    public override void Render()
    {
        // do nothing as if it's invisible
        // but do not set Visible to false
        // or its component will skip rendering
    }
}