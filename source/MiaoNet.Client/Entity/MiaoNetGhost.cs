using System.Diagnostics.CodeAnalysis;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

[Tracked]
public sealed class MiaoNetGhost : MiaoNetGhostEntity
{
    // prevent it from being AfterUpdated by Level.Update
    private sealed class GhostHair : PlayerHair
    {
        public GhostHair(PlayerSprite sprite)
            : base(sprite)
        {
        }
    }

    private PlayerSprite playerSprite;
    private readonly GhostHair playerHair;
    private readonly GhostNameTag nameTag;
    private bool followersActive = true;
    private readonly List<GhostFollower> followers;

    private Vector2 lastPosition;

    private VertexLight? vertexLight;
    private VertexLight? theoHoldableLight;
    private bool watchFocus;
    private GhostRenderBand renderBand = GhostRenderBand.Normal;

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
    private bool tired;
    private bool flash;
    // TODO sync hitbox size?
    private readonly Hitbox normalHitbox = new Hitbox(8f, 16f, -4f, -16f);
    private readonly Hitbox duckHitbox = new Hitbox(8f, 6f, -4f, -6f);
    private Hitbox hitbox;
    private readonly Holdable selfHoldable;

    private Vector2 windDirection;
    private float windHairTimer;

    private HoldableType lastHoladableType;
    private Sprite? holdableSprite;
    private Vector2? holdableOffset;
    private Sprite? redBoosterSprite;

    private IdleHover? idleHover;

    private (Color, Color) pDashColorBaseA;
    private (Color, Color) pDashColorBaseB;
    private readonly ParticleType pDashA;
    private readonly ParticleType pDashB;

    private GhostDeadBody? lastBody;

    public OnlinePlayer OnlinePlayer { get; }

    public bool Interactions { get; private set; }

    public bool BeingHeldLocally => selfHoldable.Holder is not null;

    public Vector2? HoldableOffset => holdableOffset;

    public Facings Facing => facing;

    public bool Dead => dead;

    public bool WatchFocus => watchFocus;

    public override bool WatchPresentationFocus => watchFocus;

    public override GhostRenderBand RenderBand => renderBand;

    public Vector2 LastReleaseForce { get; private set; }

    private static bool ReceiveFollowers => MiaoNetModule.Settings.FollowersSyncMode.HasReceive;

    [AllowNull]
    public PlayerGraphicsInfo GraphicsInfo
    {
        get;
        set => field = value ?? PlayerGraphicsInfo.Default;
    }

    public MiaoNetGhost(OnlinePlayer player, bool avatar)
    {
        Tag = MiaoNetTag.Normal;
        Depth = Depths.Player + 1;
        OnlinePlayer = player;
        GraphicsInfo = player.GraphicsInfo;
        var initialState = player.State!;

        facing = Facings.Right;
        playerSprite = SafeCreatePlayerSprite(initialState.PlayerSpriteMode);
        followersActive = ReceiveFollowers;
        followers = new();

        playerHair = new GhostHair(playerSprite) { Facing = facing };

        nameTag = new(this, player, avatar);

        dashes = initialState.Dashes;
        lastDashedDashes = dashes;
        Position = initialState.Position;
        windDirection = initialState.WindDirection;
        OnFollowerInitials(initialState.FollowerInfos);
        UpdateWind(initialState.WindDirection);

        PlayerStateFlags stateFlags = initialState.StateFlags;
        dashing = stateFlags.HasFlag(PlayerStateFlags.Dashing);
        UpdateStarFlying(stateFlags.HasFlag(PlayerStateFlags.StarFlying));
        UpdateInteractions(stateFlags.HasFlag(PlayerStateFlags.Interactions));
        UpdateDucking(stateFlags.HasFlag(PlayerStateFlags.Ducking));
        tired = stateFlags.HasFlag(PlayerStateFlags.Tired);
        bool facingLeft = stateFlags.HasFlag(PlayerStateFlags.FacingLeft);
        // TODO dead

        UpdateSprite(initialState.Animation, initialState.AnimationFrame, facingLeft, initialState.Scale);

        if (dashing)
            lastDashDirection = initialState.LastDashDirection;

        Add(playerHair);
        Add(playerSprite);
        UpdateRedBoosted(stateFlags.HasFlag(PlayerStateFlags.RedBoosted));
        ResetHair();

        UpdateLightSettings(MiaoNetModule.Settings.PlayerLight || watchFocus);

        pDashA = new(Player.P_DashA);
        pDashB = new(Player.P_DashB);
        pDashColorBaseA = (pDashA.Color, pDashA.Color2);
        pDashColorBaseB = (pDashB.Color, pDashB.Color2);

        OnUpdatePaused(player.IsPaused);
        OnUpdateWatching(player.GlobalFlags.HasFlag(PlayerGlobalFlags.Watching));

        selfHoldable = new(1f / 5f)
        {
            SlowRun = false,
            SlowFall = false,
            OnPickup = () => Depth = selfHoldable!.idleDepth,
            OnRelease = f =>
            {
                if (f.X != 0f)
                    f.Y -= 0.4f;
                LastReleaseForce = f;
            }
        };
        Add(selfHoldable);

        HoldableInfo initialHoldable = initialState.HoldableInfo;
        if (initialHoldable.Type == HoldableType.Jelly)
        {
            UpdateHoldable(
                initialHoldable.Type,
                initialHoldable.Offset,
                initialHoldable.Animation,
                initialHoldable.AnimationFrame,
                initialHoldable.Scale,
                initialHoldable.Rotation
            );
        }
        else if (initialHoldable.Type != HoldableType.None)
        {
            UpdateSimpleHoldable(initialHoldable.Type, initialHoldable.Offset);
        }

        var playerCollider = new PlayerCollider(OnPlayer);
        Add(playerCollider);
    }

    public override void Update()
    {
        // Save Load issue
        if (selfHoldable.Holder?.Holding != selfHoldable)
            selfHoldable.Holder = null;

        UpdateLightSettings(MiaoNetModule.Settings.PlayerLight || watchFocus);

        // TODO these can be prevented server-side
        // thus we should introduce PlayerGlobalSettings
        bool fr = ReceiveFollowers;
        if (!fr && followersActive)
        {
            followersActive = false;
            foreach (var e in followers)
                Scene.CompletelyRemove(e);
        }
        else if (fr && !followersActive)
        {
            followersActive = true;
            foreach (var e in followers)
                Scene.Add(e);
        }


        if (OnlinePlayer.IsPaused)
            return;

        base.Update();

        if (dead)
            return;

        Level level = SceneAs<Level>();

        // simulate hair color
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

        // TODO apply others' delta time
        if (level.OnRawInterval(0.05f))
            flash = !flash;

        if (flash && tired)
            playerSprite.Color = Color.Red;
        else if (playerSprite.Mode == PlayerSpriteMode.Playback || starFlying)
            playerSprite.Color = playerHair.Color;
        else
            playerSprite.Color = Color.White;

        // simulate hair waving
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
            float timeActive = level.RawTimeActive;
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

        if (!level.Paused)
        {
            if (dashing)
            {
                float alpha = EffectiveOpacity;
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

                // TODO apply others' delta time
                if (lastPosition != Position && level.OnRawInterval(0.02f))
                    level.ParticlesFG.Emit(
                        type,
                        Position + Random.Shared.Range(Vector2.One * -2f, Vector2.One * 2f),
                        lastDashDirection
                    );
            }
            else if (starFlying)
            {
                // TODO apply others' delta time
                if (level.OnRawInterval(0.02f))
                {
                    float angle = (Position - lastPosition).Angle();
                    level.Particles.Emit(FlyFeather.P_Flying, 1, Center, Vector2.One * 2f, angle);
                }
            }
        }

        lastPosition = Position;
    }

    private void OnPlayer(Player player)
    {
        if (selfHoldable.cannotHoldTimer > 0f || dashing)
            return;

        var m = player.StateMachine;
        if (
            m.State is Player.StNormal &&
            player.Speed.Y > 0f && player.Bottom <= Top + 3f
        )
        {
            Dust.Burst(player.BottomCenter, -MathF.PI / 2f, 8);
            (Scene as Level)?.DirectionalShake(Vector2.UnitY, 0.05f);
            Input.Rumble(RumbleStrength.Light, RumbleLength.Medium);
            player.Bounce(Top + 2f);
            player.Play(SFX.game_gen_thing_booped);
        }
        else if (
            m.State is not Player.StDash and not Player.StRedDash and not Player.StDreamDash and not Player.StBirdDashTutorial &&
            player.Speed.Y <= 0f && Bottom <= player.Top + 5f
        )
        {
            player.Speed.Y = Math.Max(player.Speed.Y, 16f);
        }
    }

    public void UpdateLightSettings(bool enabled)
    {
        if (enabled)
        {
            if (vertexLight is null)
                vertexLight = new VertexLight(GetLightOffset(ducking), Color.White, 0.96f, 32, 64);

            if (!ReferenceEquals(vertexLight.Entity, this))
                Add(vertexLight);
            vertexLight.Visible = true;
        }
        else
        {
            // remove it will lead to a vanilla crash...
            vertexLight?.Visible = false;
        }
    }

    public void SetWatchFocus(bool focused)
    {
        watchFocus = focused;
        UpdateLightSettings(MiaoNetModule.Settings.PlayerLight || watchFocus);
    }

    private static Vector2 GetLightOffset(bool duck)
        => duck ? new Vector2(0f, -3f) : new Vector2(0f, -8f);

    #region state updates

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
                    float alpha = EffectiveOpacity;
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
            followers.Add(gf);
            if (followersActive)
                Scene?.Add(gf);
        }
    }

    public void OnFollowerDeltas(FollowerInfoDelta[] deltas)
    {
        if (deltas.Length != followers.Count)
        {
            Logger.Error(
                LT.MiaoNet,
                $"Received {deltas.Length} follower deltas but there's only {followers.Count} followers."
            );
            // let it crash
        }
        for (int i = 0; i < deltas.Length; i++)
        {
            FollowerInfoDelta delta = deltas[i];
            var gf = followers[i];
            gf.UpdateSprite(delta.AnimationID, delta.AnimationFrame);
            gf.Position = Position + delta.Offset;
        }
    }

    private void CleanUpFollowers()
    {
        foreach (var follower in followers)
            Scene?.CompletelyRemove(follower);
        followers.Clear();
    }

    private void AddTrail(int dashes)
    {
        float alpha = EffectiveOpacity;
        var snap = TrailManager.Add(
            Position,
            playerSprite, playerHair,
            Vector2.One, GraphicsInfo.GetHairInfo(dashes).Color * alpha,
            Depth + 1, useRawDeltaTime: true
        );
        snap?.Tag |= Tag;
    }

    public void OnDied(Vector2 direction)
    {
        dead = true;
        selfHoldable.Holder?.Drop();
        Collidable = false;
        UpdateVisible();
        if (Scene is Level level)
        {
            Remove(playerHair);
            Remove(playerSprite);
            if (vertexLight is not null)
                Remove(vertexLight);
            GhostDeadBody body = new(
                Position,
                facing,
                playerHair,
                playerSprite,
                vertexLight,
                direction,
                watchFocus
            );
            lastBody = body;
            level.Add(body);
        }
        renderBand = GhostRenderBand.High;
        Depth = Depths.Top;
    }

    // TODO the respawned timing is not that accurate
    public void OnRespawning(Vector2 position, bool fromSL)
    {
        Position = position;

        // The watched subject is revealed by the authoritative black-frame
        // lifecycle. Playing the ordinary multiplayer ghost revive effect on
        // top of it creates a second, false respawn animation.
        if (watchFocus)
        {
            RestoreAfterRespawn();
            return;
        }

        if (!fromSL)
        {
            respawning = true;
            deadEase = 1f;
            UpdateVisible();
            UpdateCollidable();
            var tween = Tween.Set(this, Tween.TweenMode.Oneshot, 0.6f, null,
                t =>
                {
                    deadEase = 1f - t.Eased;
                },
                t =>
                {
                    RestoreAfterRespawn();
                }
            );
            tween.UseRawDeltaTime = true;
        }
        else
        {
            RestoreAfterRespawn();
        }
    }

    private void RestoreAfterRespawn()
    {
        UpdateCollidable();
        respawning = false;
        dead = false;
        UpdateVisible();
        renderBand = GhostRenderBand.Normal;
        Depth = Depths.Player + 1;
        if (!ReferenceEquals(playerHair.Entity, this))
            Add(playerHair);
        if (!ReferenceEquals(playerSprite.Entity, this))
            Add(playerSprite);
        if (vertexLight is not null && !ReferenceEquals(vertexLight.Entity, this))
            Add(vertexLight);
        if (Scene is not null)
            Scene.OnEndOfFrame += new(ResetHair);
        lastBody?.RemoveSelf();
        lastBody = null;
    }

    // TODO start star flying sync?
    public void UpdateStarFlying(bool starFlying)
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

    public void UpdateSprite(string? animID, ushort animFrame, bool facingLeft, Vector2 scale)
    {
        // The constructor applies its initial sprite before selfHoldable is
        // created. Treat that initialization window as "not being held".
        if (!dead && !respawning && selfHoldable?.Holder is null)
            renderBand = animID?.StartsWith("dreamDash", StringComparison.OrdinalIgnoreCase) == true
                ? GhostRenderBand.DreamDash
                : GhostRenderBand.Normal;
        if (!string.IsNullOrEmpty(animID) && playerSprite.Has(animID))
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
        UpdateTheoHoldableLight(false);
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
        UpdateTheoHoldableLight(type == HoldableType.Theo);
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
        UpdateTheoHoldableLight(type == HoldableType.Theo);
    }

    public void UpdateRedBoosted(bool boosted)
    {
        if (!boosted)
        {
            if (redBoosterSprite is not null)
                redBoosterSprite.Active = false;
            return;
        }

        if (redBoosterSprite is null)
        {
            redBoosterSprite = GFX.SpriteBank.Create("boosterRed");
            redBoosterSprite.Visible = false;
            Add(redBoosterSprite);
        }

        redBoosterSprite.Position = Center - Position + new Vector2(0f, -2f);
        redBoosterSprite.FlipX = facing == Facings.Left;
        redBoosterSprite.Active = true;
        if (redBoosterSprite.CurrentAnimationID != "spin")
            redBoosterSprite.Play("spin");
    }

    private bool HasRoomBoosterVisual()
    {
        if (Scene is not Level level || redBoosterSprite is null)
            return false;

        Vector2 expected = Center + new Vector2(0f, -2f);
        return level.Entities.OfType<Booster>().Any(booster =>
            booster.red
            && booster.BoostingPlayer
            && booster.sprite.Visible
            && Vector2.DistanceSquared(booster.sprite.RenderPosition, expected) <= 32f * 32f
        );
    }

    private void UpdateTheoHoldableLight(bool enabled)
    {
        if (!enabled)
        {
            if (theoHoldableLight is not null)
                theoHoldableLight.Visible = false;
            return;
        }

        if (theoHoldableLight is null)
        {
            theoHoldableLight = new VertexLight(Vector2.Zero, Color.White, 1f, 32, 64);
            Add(theoHoldableLight);
        }
        theoHoldableLight.Position = (holdableOffset ?? Vector2.Zero) + new Vector2(0f, -5f);
        theoHoldableLight.Visible = true;
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
        vertexLight?.Position = GetLightOffset(ducking);
    }

    public void UpdateTired(bool tired)
        => this.tired = tired;

    public void UpdateInteractions(bool interactions)
    {
        Interactions = interactions;
        UpdateCollidable();
    }

    public void OnUpdatePaused(bool paused)
    {
        if (paused)
        {
            if (idleHover is null)
            {
                playerHair.Active = false;
                idleHover = new(this);
                idleHover.Visible = this.Visible;
                if (Scene is not null)
                {
                    Scene.Add(idleHover);
                    idleHover.PlayAnimation();
                }
                UpdateCollidable();
            }
        }
        else
        {
            playerHair.Active = true;
            idleHover?.StopAnimationAndRemove();
            idleHover = null;
            UpdateCollidable();
        }
    }

    public void OnUpdateWatching(bool watching)
    {
        UpdateVisible();
    }

    private void UpdateVisible()
    {
        bool watching = OnlinePlayer.GlobalFlags.HasFlag(PlayerGlobalFlags.Watching);

        Visible = (!dead || respawning) && !watching;
        nameTag.Visible = !watching;
        idleHover?.Visible = this.Visible;
    }

    private void UpdateCollidable()
    {
        Collidable = Interactions && MiaoNetModule.Settings.PlayerInteractions && !OnlinePlayer.IsPaused;
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
    }

    private void UpdateHairCount()
    {
        UpdateHairCount(GraphicsInfo.GetHairInfo(dashes).Length);
    }

    private void ResetHair()
    {
        playerHair.Start();
        playerHair.AfterUpdate();
    }

    #endregion

    public override void Added(Scene scene)
    {
        base.Added(scene);
        scene.Add(nameTag);
        if (idleHover is not null)
            scene.Add(idleHover);
        if (followersActive)
        {
            foreach (var follower in followers)
            {
                if (follower.Scene is null)
                    scene.Add(follower);
            }
        }
    }

    public override void Removed(Scene scene)
    {
        scene.Remove(nameTag);
        idleHover?.RemoveSelf();
        CleanUpFollowers();
        base.Removed(scene);
    }

    public void OnCreatedFireworks(Color color, float initialSpeed)
    {
        if (Scene is not Level level)
            return;

        if (!level.InsideCamera(Center, 128f))
            return;

        level.Add(new Fireworks(Position, color, initialSpeed));
    }

    public override void GhostRender()
    {
        if (lastHoladableType == HoldableType.Theo)
        {
            holdableSprite!.Render();
        }

        {
            playerSprite.Scale.X *= (float)facing;
            BaseRender();
            playerSprite.Scale.X *= (float)facing;
        }

        if (redBoosterSprite is { Active: true } && !HasRoomBoosterVisual())
            redBoosterSprite.Render();

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
    public void HairAfterUpdate()
    {
        if (dead)
            return;

        if (OnlinePlayer.IsPaused)
        {
            // only keep the position
            // yes this is kinda hacky
            Vector2 offset = playerHair.Sprite.HairOffset * new Vector2((float)playerHair.Facing, 1f);
            Vector2 expectedNode0Position = playerHair.Sprite.RenderPosition + new Vector2(0f, -9f * playerHair.Sprite.Scale.Y) + offset;
            playerHair.MoveHairBy(expectedNode0Position - playerHair.Nodes[0]);
        }
        else
        {
            playerHair.AfterUpdate();
        }
    }
}
