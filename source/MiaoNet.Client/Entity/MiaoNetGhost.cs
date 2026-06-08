using System.Diagnostics.CodeAnalysis;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

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
    private readonly Leader leader;

    private Vector2 lastPosition;

    private VertexLight? vertexLight;

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

    public Vector2 LastReleaseForce { get; private set; }

    [AllowNull]
    public PlayerGraphicsInfo GraphicsInfo
    {
        get => field;
        set => field = value ?? PlayerGraphicsInfo.Default;
    }

    public MiaoNetGhost(
        OnlinePlayer player,
        PlayerGraphicsInfo? playerGraphicsInfo,
        PlayerState initialState,
        bool avatar
    )
    {
        Tag = MiaoNetTag.Tag;
        Depth = Depths.Player + 1;
        OnlinePlayer = player;
        GraphicsInfo = playerGraphicsInfo;
        facing = Facings.Right;
        playerSprite = SafeCreatePlayerSprite(initialState.PlayerSpriteMode);
        Add(leader = new Leader(new Vector2(0f, -8f)));
        Add(new MirrorReflection());

        playerHair = new GhostHair(playerSprite) { Facing = facing };

        nameTag = new(this, player, avatar);

        ApplyState(initialState);

        Add(playerHair);
        Add(playerSprite);
        ResetHair();

        UpdateLightSettings(MiaoNetModule.Settings.PlayerLight);

        pDashA = new(Player.P_DashA);
        pDashB = new(Player.P_DashB);
        pDashColorBaseA = (pDashA.Color, pDashA.Color2);
        pDashColorBaseB = (pDashB.Color, pDashB.Color2);

        OnUpdatePaused(player.IsPaused);
        OnUpdateWatching();

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

        var playerCollider = new PlayerCollider(OnPlayer);
        Add(playerCollider);
    }

    public override void Update()
    {
        // Save Load issue
        if (selfHoldable.Holder?.Holding != selfHoldable)
            selfHoldable.Holder = null;

        UpdateLightSettings(MiaoNetModule.Settings.PlayerLight);

        // TODO these can be prevented server-side
        // thus we should introduce PlayerGlobalSettings
        bool fr = MiaoNetModule.Settings.FollowersSyncMode.HasReceive;
        if (!fr && leader.Active)
        {
            leader.Active = false;
            foreach (var e in leader.Followers)
                e.Entity.RemoveSelf();
        }
        else if (fr && !leader.Active)
        {
            leader.Active = true;
            foreach (var e in leader.Followers)
                Scene.Add(e.Entity);
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

        playerHair.AfterUpdate();

        if (!level.Paused)
        {
            if (dashing)
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

                // TODO apply others' delta time
                if (lastPosition != Position && level.OnRawInterval(0.02f))
                    level.ParticlesFG.Emit(
                        type,
                        Position + Calc.Random.Range(Vector2.One * -2f, Vector2.One * 2f),
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
            {
                vertexLight = new VertexLight(GetLightOffset(ducking), Color.White, 0.96f, 32, 64);
                Add(vertexLight);
            }
            vertexLight.Visible = true;
        }
        else
        {
            // remove it will lead to a vanilla crash...
            vertexLight?.Visible = false;
        }
    }

    private static Vector2 GetLightOffset(bool duck)
        => duck ? new Vector2(0f, -3f) : new Vector2(0f, -8f);

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
            UpdateHairCount();
        }
        dashes = state.Dashes;
        lastDashedDashes = dashes;
        Position = state.Position;
        windDirection = state.WindDirection;
        ducking = state.Ducking;
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
            Scene?.CompletelyRemove(follower.Entity);
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
            GhostDeadBody body = new(Position, facing, playerHair, playerSprite, vertexLight, direction);
            lastBody = body;
            level.Add(body);
        }
        Depth = Depths.Top;
    }

    // TODO the respawned timing is not that accurate
    public void OnRespawning(Vector2 position, bool fromSL)
    {
        Position = position;

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
                    respawning = false;
                    dead = false;
                    UpdateVisible();
                    Depth = Depths.Player + 1;
                    Add(playerHair);
                    Add(playerSprite);
                    if (vertexLight is not null)
                        Add(vertexLight);
                    Scene.OnEndOfFrame += new(ResetHair);
                    lastBody = null;
                }
            );
            tween.UseRawDeltaTime = true;
        }
        else
        {
            UpdateCollidable();
            respawning = false;
            dead = false;
            UpdateVisible();
            Depth = Depths.Player + 1;
            Add(playerHair);
            Add(playerSprite);
            if (vertexLight is not null)
                Add(vertexLight);
            Scene.OnEndOfFrame += new(ResetHair);
            lastBody?.RemoveSelf();
        }
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

    public void OnUpdateWatching()
    {
        UpdateVisible();
    }

    private void UpdateVisible()
    {
        bool watching = OnlinePlayer.GlobalFlags.HasFlag(PlayerGlobalFlags.Watching);
        Visible = (!dead || respawning) && !watching;
        nameTag.Visible = !watching;
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
        foreach (var follower in leader.Followers)
        {
            Entity e = follower.Entity;
            if (e.Scene is null)
                scene.Add(e);
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
}