using System.Collections;
using System.Runtime.CompilerServices;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchCrumblePlatformAdapter : IWatchEntityAdapter
{
    private const byte TileOutEvent = 1;
    private const byte TileInEvent = 2;
    private const byte ShakeEvent = 3;

    private sealed class PlatformInfo
    {
        public string Level { get; }

        public int ID { get; }
        public Vector2[] InitialImagePositions { get; set; } = [];

        public PlatformInfo(string level, int id)
        {
            Level = level;
            ID = id;
        }
    }

    private static readonly WatchCrumblePlatformAdapter instance = new();
    private static readonly ConditionalWeakTable<CrumblePlatform, PlatformInfo> infos = new();

    public WatchEntityKind Kind => WatchEntityKind.CrumblePlatform;

    public static void Load()
    {
        On.Celeste.CrumblePlatform.ctor_EntityData_Vector2 += CrumblePlatform_ctor;
        On.Celeste.CrumblePlatform.Added += CrumblePlatform_Added;
        On.Celeste.CrumblePlatform.Sequence += CrumblePlatform_Sequence;
        On.Celeste.CrumblePlatform.TileOut += CrumblePlatform_TileOut;
        On.Celeste.CrumblePlatform.TileIn += CrumblePlatform_TileIn;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.CrumblePlatform.TileIn -= CrumblePlatform_TileIn;
        On.Celeste.CrumblePlatform.TileOut -= CrumblePlatform_TileOut;
        On.Celeste.CrumblePlatform.Sequence -= CrumblePlatform_Sequence;
        On.Celeste.CrumblePlatform.Added -= CrumblePlatform_Added;
        On.Celeste.CrumblePlatform.ctor_EntityData_Vector2 -= CrumblePlatform_ctor;
        infos.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (CrumblePlatform platform in level.Entities.OfType<CrumblePlatform>())
        {
            if (!infos.TryGetValue(platform, out PlatformInfo? info)
                || !StringComparer.Ordinal.Equals(info.Level, level.Session.Level))
                continue;

            int imageCount = Math.Min(platform.images.Count, ushort.MaxValue);
            byte[] payload = new byte[4 + (imageCount + 7) / 8];
            payload[0] = (byte)GetPhase(platform);
            payload[1] = platform.Collidable ? (byte)1 : (byte)0;
            WatchEntityPayloadCodec.WriteUInt16(payload, 2, (ushort)imageCount);
            for (int i = 0; i < imageCount; i++)
            {
                if (platform.images[i].Visible)
                    payload[4 + i / 8] |= (byte)(1 << (i % 8));
            }
            yield return new WatchEntityState(new WatchEntityKey(Kind, info.ID), payload);
        }
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        Dictionary<int, WatchEntityState> desiredByID = new();
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> payload = state.Payload.Span;
            if (state.Key.Kind != Kind
                || state.Key.SubID != 0
                || payload.Length < 4
                || payload[0] > (byte)WatchEntityPhase.Returning
                || payload[1] > 1)
                return WatchEntityApplyResult.None;

            int imageCount = WatchEntityPayloadCodec.ReadUInt16(payload, 2);
            if (payload.Length != 4 + (imageCount + 7) / 8
                || !desiredByID.TryAdd(state.Key.EntityID, state))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        foreach (CrumblePlatform platform in level.Entities.OfType<CrumblePlatform>())
        {
            if (!infos.TryGetValue(platform, out PlatformInfo? info)
                || !StringComparer.Ordinal.Equals(info.Level, level.Session.Level)
                || !desiredByID.TryGetValue(info.ID, out WatchEntityState state))
                continue;

            ReadOnlySpan<byte> payload = state.Payload.Span;
            WatchEntityPhase previous = GetPhase(platform);
            WatchEntityPhase desired = (WatchEntityPhase)payload[0];
            bool collidable = payload[1] != 0;
            int imageCount = WatchEntityPayloadCodec.ReadUInt16(payload, 2);
            if (imageCount != platform.images.Count)
                continue;

            bool differs = previous != desired || platform.Collidable != collidable;
            for (int i = 0; i < imageCount; i++)
            {
                bool visible = (payload[4 + i / 8] & (1 << (i % 8))) != 0;
                differs |= platform.images[i].Visible != visible;
                platform.images[i].Visible = visible;
            }
            if (!differs)
                continue;

            platform.Collidable = collidable;
            bool showOutline = desired is WatchEntityPhase.Gone or WatchEntityPhase.Returning;
            foreach (Image outline in platform.outline)
                outline.Visible = showOutline;

            if (desired == WatchEntityPhase.Ready)
            {
                for (int i = 0; i < platform.images.Count; i++)
                {
                    Image image = platform.images[i];
                    if (i < info.InitialImagePositions.Length)
                        image.Position = info.InitialImagePositions[i];
                    image.Rotation = 0f;
                    image.Scale = Vector2.One;
                    image.Color = Color.White;
                }
            }
            else if (previous == WatchEntityPhase.Gone && desired == WatchEntityPhase.Returning)
                Audio.Play("event:/game/general/platform_return", platform.Center);

            changed = true;
        }

        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (entityEvent.EventID == ShakeEvent)
        {
            if (entityEvent.Payload.Length != 4)
                return;

            float duration = WatchEntityPayloadCodec.ReadSingle(entityEvent.Payload.Span, 0);
            if (!float.IsFinite(duration) || duration <= 0f || duration > 2f)
                return;

            CrumblePlatform? target = FindPlatform(level, entityEvent.Key.EntityID);
            if (target is null)
                return;

            Audio.Play("event:/game/general/platform_disintegrate", target.Center);
            target.shaker.ShakeFor(duration, false);
            target.Add(new Coroutine(EmitCrumbleParticles(target)));
            return;
        }

        if (entityEvent.EventID is not (TileOutEvent or TileInEvent)
            || entityEvent.Payload.Length != 6)
            return;

        CrumblePlatform? platform = FindPlatform(level, entityEvent.Key.EntityID);
        if (platform is null)
            return;

        ReadOnlySpan<byte> payload = entityEvent.Payload.Span;
        int imageIndex = WatchEntityPayloadCodec.ReadUInt16(payload, 0);
        float delay = WatchEntityPayloadCodec.ReadSingle(payload, 2);
        if (imageIndex >= platform.images.Count || !float.IsFinite(delay) || delay < 0f)
            return;

        Image image = platform.images[imageIndex];
        if (entityEvent.EventID == TileOutEvent)
        {
            if (platform.Collidable)
                platform.Collidable = false;
            platform.Add(new Coroutine(platform.TileOut(image, delay)));
        }
        else
        {
            if (platform.images.All(item => !item.Visible))
                Audio.Play("event:/game/general/platform_return", platform.Center);
            platform.Add(new Coroutine(platform.TileIn(imageIndex, image, delay)));
        }
    }

    private static WatchEntityPhase GetPhase(CrumblePlatform platform)
    {
        int visible = platform.images.Count(image => image.Visible);
        if (visible == platform.images.Count && platform.Collidable)
            return WatchEntityPhase.Ready;
        if (visible == 0)
            return WatchEntityPhase.Gone;
        return platform.Collidable ? WatchEntityPhase.Active : WatchEntityPhase.Returning;
    }

    private static CrumblePlatform? FindPlatform(Level level, int id)
        => level.Entities.OfType<CrumblePlatform>().FirstOrDefault(candidate =>
            infos.TryGetValue(candidate, out PlatformInfo? info)
            && StringComparer.Ordinal.Equals(info.Level, level.Session.Level)
            && info.ID == id
        );

    private static void CrumblePlatform_ctor(
        On.Celeste.CrumblePlatform.orig_ctor_EntityData_Vector2 orig,
        CrumblePlatform self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        infos.AddOrUpdate(self, new PlatformInfo(data.Level.Name, data.ID));
    }

    private static void CrumblePlatform_Added(
        On.Celeste.CrumblePlatform.orig_Added orig,
        CrumblePlatform self,
        Scene scene
    )
    {
        orig(self, scene);
        if (infos.TryGetValue(self, out PlatformInfo? info))
            info.InitialImagePositions = self.images.Select(image => image.Position).ToArray();
    }

    private static IEnumerator CrumblePlatform_Sequence(
        On.Celeste.CrumblePlatform.orig_Sequence orig,
        CrumblePlatform self
    )
        => TrackSequence(self, orig(self));

    private static IEnumerator TrackSequence(CrumblePlatform self, IEnumerator sequence)
    {
        try
        {
            while (true)
            {
                bool wasShaking = self.shaker.On;
                bool hasNext = sequence.MoveNext();
                if (!wasShaking && self.shaker.On)
                {
                    float duration = self.GetPlayerOnTop() is null ? 1f : 0.6f;
                    byte[] payload = new byte[4];
                    WatchEntityPayloadCodec.WriteSingle(payload, 0, duration);
                    PublishPlatformEvent(self, ShakeEvent, payload);
                }

                if (!hasNext)
                    yield break;
                yield return sequence.Current;
            }
        }
        finally
        {
            (sequence as IDisposable)?.Dispose();
        }
    }

    private static IEnumerator EmitCrumbleParticles(CrumblePlatform platform)
    {
        EmitCrumbleParticleBurst(platform);
        yield return 0.2f;
        if (platform.Scene is Level)
            EmitCrumbleParticleBurst(platform);
    }

    private static void EmitCrumbleParticleBurst(CrumblePlatform platform)
    {
        if (platform.Scene is not Level level)
            return;
        foreach (Image image in platform.images)
        {
            level.Particles.Emit(
                CrumblePlatform.P_Crumble,
                2,
                platform.Position + image.Position + new Vector2(0f, 2f),
                Vector2.One * 3f
            );
        }
    }

    private static IEnumerator CrumblePlatform_TileOut(
        On.Celeste.CrumblePlatform.orig_TileOut orig,
        CrumblePlatform self,
        Image image,
        float delay
    )
    {
        PublishTileEvent(self, image, delay, TileOutEvent);
        return orig(self, image, delay);
    }

    private static IEnumerator CrumblePlatform_TileIn(
        On.Celeste.CrumblePlatform.orig_TileIn orig,
        CrumblePlatform self,
        int index,
        Image image,
        float delay
    )
    {
        PublishTileEvent(self, image, delay, TileInEvent, index);
        return orig(self, index, image, delay);
    }

    private static void PublishTileEvent(
        CrumblePlatform self,
        Image image,
        float delay,
        byte eventID,
        int? knownIndex = null
    )
    {
        if (WatchEntitySyncRegistry.IsApplyingRemoteState
            || self.Scene is not Level level
            || !infos.TryGetValue(self, out PlatformInfo? info)
            || !StringComparer.Ordinal.Equals(info.Level, level.Session.Level))
            return;

        int index = knownIndex ?? self.images.IndexOf(image);
        if (index < 0 || index > ushort.MaxValue)
            return;

        byte[] payload = new byte[6];
        WatchEntityPayloadCodec.WriteUInt16(payload, 0, (ushort)index);
        WatchEntityPayloadCodec.WriteSingle(payload, 2, delay);
        WatchEntitySyncRegistry.PublishEvent(
            level,
            new WatchEntityEvent(new WatchEntityKey(WatchEntityKind.CrumblePlatform, info.ID), eventID, payload)
        );
    }

    private static void PublishPlatformEvent(
        CrumblePlatform self,
        byte eventID,
        ReadOnlySpan<byte> payload
    )
    {
        if (WatchEntitySyncRegistry.IsApplyingRemoteState
            || self.Scene is not Level level
            || !infos.TryGetValue(self, out PlatformInfo? info)
            || !StringComparer.Ordinal.Equals(info.Level, level.Session.Level))
            return;

        WatchEntitySyncRegistry.PublishEvent(
            level,
            new WatchEntityEvent(new WatchEntityKey(WatchEntityKind.CrumblePlatform, info.ID), eventID, payload)
        );
    }
}
