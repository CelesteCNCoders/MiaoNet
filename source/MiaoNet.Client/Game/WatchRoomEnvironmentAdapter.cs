using MiaoNet.Shared;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchRoomEnvironmentAdapter : IWatchEntityAdapter
{
    private const int FixedPayloadSize = 72;
    private const int MaxMusicEventBytes = 192;
    private const int MaxColorGradeBytes = 64;
    private const byte HasBlackholeFlag = 1 << 0;
    private const byte HasWindControllerFlag = 1 << 1;
    private const byte HasMusicEventFlag = 1 << 2;

    private readonly record struct EnvironmentState(
        byte Flags,
        byte BlackholeStrength,
        byte WindPattern,
        int MusicProgress,
        float BloomBaseAdd,
        float LightingAlphaAdd,
        float BloomStrength,
        float LightingAlpha,
        float Glitch,
        float BlackholeAlpha,
        float BlackholeStrengthMultiplier,
        Vector2 Wind,
        float MusicFade,
        float MusicEscape,
        float MusicInSpace,
        float AmbienceBasement,
        float AmbienceShrine,
        string MusicEvent,
        string ColorGrade,
        Color BackgroundColor
    );

    private sealed class Baseline
    {
        public required EnvironmentState State { get; init; }
        public required AudioState Audio { get; init; }
    }

    private sealed class RemoteInfo
    {
        public bool HasState { get; set; }
        public EnvironmentState State { get; set; }
    }

    private static readonly WatchRoomEnvironmentAdapter instance = new();
    private static readonly WatchEntityKey StateKey = new(WatchEntityKind.RoomEnvironment, 0);
    private static readonly ConditionalWeakTable<Level, Baseline> baselines = new();
    private static readonly ConditionalWeakTable<Level, RemoteInfo> remoteInfo = new();

    public WatchEntityKind Kind => WatchEntityKind.RoomEnvironment;

    public static void Load()
    {
        On.Celeste.BloomFadeTrigger.OnStay += BloomFadeTrigger_OnStay;
        On.Celeste.LightFadeTrigger.OnStay += LightFadeTrigger_OnStay;
        On.Celeste.MusicFadeTrigger.OnStay += MusicFadeTrigger_OnStay;
        On.Celeste.AmbienceParamTrigger.OnStay += AmbienceParamTrigger_OnStay;
        On.Celeste.MusicTrigger.OnEnter += MusicTrigger_OnEnter;
        On.Celeste.MusicTrigger.OnLeave += MusicTrigger_OnLeave;
        On.Celeste.AltMusicTrigger.OnEnter += AltMusicTrigger_OnEnter;
        On.Celeste.AltMusicTrigger.OnLeave += AltMusicTrigger_OnLeave;
        On.Celeste.BlackholeStrengthTrigger.OnEnter += BlackholeStrengthTrigger_OnEnter;
        On.Celeste.MoonGlitchBackgroundTrigger.OnEnter += MoonGlitchBackgroundTrigger_OnEnter;
        On.Celeste.WindTrigger.OnEnter += WindTrigger_OnEnter;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.WindTrigger.OnEnter -= WindTrigger_OnEnter;
        On.Celeste.MoonGlitchBackgroundTrigger.OnEnter -= MoonGlitchBackgroundTrigger_OnEnter;
        On.Celeste.BlackholeStrengthTrigger.OnEnter -= BlackholeStrengthTrigger_OnEnter;
        On.Celeste.AltMusicTrigger.OnLeave -= AltMusicTrigger_OnLeave;
        On.Celeste.AltMusicTrigger.OnEnter -= AltMusicTrigger_OnEnter;
        On.Celeste.MusicTrigger.OnLeave -= MusicTrigger_OnLeave;
        On.Celeste.MusicTrigger.OnEnter -= MusicTrigger_OnEnter;
        On.Celeste.AmbienceParamTrigger.OnStay -= AmbienceParamTrigger_OnStay;
        On.Celeste.MusicFadeTrigger.OnStay -= MusicFadeTrigger_OnStay;
        On.Celeste.LightFadeTrigger.OnStay -= LightFadeTrigger_OnStay;
        On.Celeste.BloomFadeTrigger.OnStay -= BloomFadeTrigger_OnStay;
        baselines.Clear();
        remoteInfo.Clear();
    }

    internal static void CaptureBaseline(Level level)
    {
        if (baselines.TryGetValue(level, out _))
            return;
        baselines.Add(level, new()
        {
            State = Capture(level),
            Audio = level.Session.Audio.Clone(),
        });
    }

    internal static void RestoreBaseline(Level level)
    {
        remoteInfo.Remove(level);
        if (!baselines.TryGetValue(level, out Baseline? baseline))
            return;
        ApplyVisual(level, baseline.State);
        level.Session.Audio = baseline.Audio.Clone();
        level.Session.Audio.Apply(false);
        baselines.Remove(level);
    }

    internal static void ApplyFrame(Level level)
    {
        if (!MiaoNetModule.IsWatching
            || !remoteInfo.TryGetValue(level, out RemoteInfo? info) || !info.HasState)
            return;
        ApplyVisual(level, info.State);
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        yield return Encode(Capture(level));
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        if (states.Count != 1 || !TryDecode(states.First(), out EnvironmentState state))
            return WatchEntityApplyResult.None;

        CaptureBaseline(level);
        RemoteInfo info = remoteInfo.GetValue(level, static _ => new());
        bool audioChanged = !info.HasState
            || info.State.MusicEvent != state.MusicEvent
            || info.State.MusicProgress != state.MusicProgress
            || info.State.MusicFade != state.MusicFade
            || info.State.MusicEscape != state.MusicEscape
            || info.State.MusicInSpace != state.MusicInSpace
            || info.State.AmbienceBasement != state.AmbienceBasement
            || info.State.AmbienceShrine != state.AmbienceShrine;
        info.State = state;
        info.HasState = true;
        ApplyVisual(level, state);
        if (audioChanged)
            ApplyAudio(level, state);
        return WatchEntityApplyResult.SceneChanged;
    }


    private static EnvironmentState Capture(Level level)
    {
        BlackholeBG? blackhole = level.Background.Get<BlackholeBG>();
        WindController? wind = FindWindController(level);
        byte flags = 0;
        if (blackhole is not null)
            flags |= HasBlackholeFlag;
        if (wind is not null)
            flags |= HasWindControllerFlag;
        string musicEvent = level.Session.Audio.Music.Event ?? string.Empty;
        if (musicEvent.Length > 0)
            flags |= HasMusicEventFlag;
        return new(
            flags,
            (byte)(blackhole?.strength ?? BlackholeBG.Strengths.Mild),
            (byte)(wind?.pattern ?? WindController.Patterns.None),
            level.Session.Audio.Music.Progress,
            level.Session.BloomBaseAdd,
            level.Session.LightingAlphaAdd,
            level.Bloom.Strength,
            level.Lighting.Alpha,
            Glitch.Value,
            blackhole?.Alpha ?? 0f,
            blackhole?.StrengthMultiplier ?? 0f,
            level.Wind,
            level.Session.Audio.Music.GetParam("fade"),
            level.Session.Audio.Music.GetParam("escape"),
            level.Session.Audio.Music.GetParam("in_space"),
            level.Session.Audio.Ambience.GetParam("basement"),
            level.Session.Audio.Ambience.GetParam("shrine"),
            musicEvent,
            level.Session.ColorGrade ?? string.Empty,
            level.BackgroundColor
        );
    }

    private static void ApplyVisual(Level level, EnvironmentState state)
    {
        level.Session.BloomBaseAdd = state.BloomBaseAdd;
        level.Session.LightingAlphaAdd = state.LightingAlphaAdd;
        level.Bloom.Strength = state.BloomStrength;
        level.Lighting.Alpha = state.LightingAlpha;
        Glitch.Value = state.Glitch;
        level.Wind = state.Wind;
        level.BackgroundColor = state.BackgroundColor;
        if (!string.IsNullOrEmpty(state.ColorGrade)
            && level.Session.ColorGrade != state.ColorGrade)
        {
            level.Session.ColorGrade = state.ColorGrade;
            level.SnapColorGrade(state.ColorGrade);
        }

        if ((state.Flags & HasBlackholeFlag) != 0
            && level.Background.Get<BlackholeBG>() is { } blackhole)
        {
            BlackholeBG.Strengths strength = (BlackholeBG.Strengths)state.BlackholeStrength;
            if (blackhole.strength != strength)
                blackhole.SnapStrength(level, strength);
            blackhole.Alpha = state.BlackholeAlpha;
            blackhole.StrengthMultiplier = state.BlackholeStrengthMultiplier;
        }

        if ((state.Flags & HasWindControllerFlag) != 0
            && FindWindController(level) is { } wind
            && wind.pattern != (WindController.Patterns)state.WindPattern)
        {
            wind.SetPattern((WindController.Patterns)state.WindPattern);
        }
    }

    private static WindController? FindWindController(Level level)
        => level.Entities.OfType<WindController>().FirstOrDefault();

    private static void ApplyAudio(Level level, EnvironmentState state)
    {
        if ((state.Flags & HasMusicEventFlag) != 0)
        {
            level.Session.Audio.Music.Event = state.MusicEvent;
            level.Session.Audio.Music.SetProgress(state.MusicProgress);
        }
        level.Session.Audio.Music.Param("fade", state.MusicFade);
        level.Session.Audio.Music.Param("escape", state.MusicEscape);
        level.Session.Audio.Music.Param("in_space", state.MusicInSpace);
        level.Session.Audio.Ambience.Param("basement", state.AmbienceBasement);
        level.Session.Audio.Ambience.Param("shrine", state.AmbienceShrine);
        level.Session.Audio.Apply(false);
    }

    private static WatchEntityState Encode(EnvironmentState state)
    {
        byte[] music = Encoding.UTF8.GetBytes(state.MusicEvent);
        if (music.Length > MaxMusicEventBytes)
            music = music[..MaxMusicEventBytes];
        byte[] colorGrade = Encoding.UTF8.GetBytes(state.ColorGrade);
        if (colorGrade.Length > MaxColorGradeBytes)
            colorGrade = colorGrade[..MaxColorGradeBytes];
        byte[] payload = new byte[FixedPayloadSize + music.Length + colorGrade.Length];
        payload[0] = state.Flags;
        payload[1] = state.BlackholeStrength;
        payload[2] = state.WindPattern;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), state.MusicProgress);
        WatchEntityPayloadCodec.WriteSingle(payload, 8, state.BloomBaseAdd);
        WatchEntityPayloadCodec.WriteSingle(payload, 12, state.LightingAlphaAdd);
        WatchEntityPayloadCodec.WriteSingle(payload, 16, state.BloomStrength);
        WatchEntityPayloadCodec.WriteSingle(payload, 20, state.LightingAlpha);
        WatchEntityPayloadCodec.WriteSingle(payload, 24, state.Glitch);
        WatchEntityPayloadCodec.WriteSingle(payload, 28, state.BlackholeAlpha);
        WatchEntityPayloadCodec.WriteSingle(payload, 32, state.BlackholeStrengthMultiplier);
        WatchEntityPayloadCodec.WriteVector2(payload, 36, state.Wind);
        WatchEntityPayloadCodec.WriteSingle(payload, 44, state.MusicFade);
        WatchEntityPayloadCodec.WriteSingle(payload, 48, state.MusicEscape);
        WatchEntityPayloadCodec.WriteSingle(payload, 52, state.MusicInSpace);
        WatchEntityPayloadCodec.WriteSingle(payload, 56, state.AmbienceBasement);
        WatchEntityPayloadCodec.WriteSingle(payload, 60, state.AmbienceShrine);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(64), (ushort)music.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(66), (ushort)colorGrade.Length);
        payload[68] = state.BackgroundColor.R;
        payload[69] = state.BackgroundColor.G;
        payload[70] = state.BackgroundColor.B;
        payload[71] = state.BackgroundColor.A;
        music.CopyTo(payload.AsSpan(FixedPayloadSize));
        colorGrade.CopyTo(payload.AsSpan(FixedPayloadSize + music.Length));
        return new(StateKey, payload);
    }

    private static bool TryDecode(WatchEntityState state, out EnvironmentState value)
    {
        value = default;
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key != StateKey || payload.Length < FixedPayloadSize
            || (payload[0] & ~0b0000_0111) != 0
            || payload[1] > (byte)BlackholeBG.Strengths.Wild
            || payload[2] > (byte)WindController.Patterns.Space || payload[3] != 0)
            return false;
        int musicLength = BinaryPrimitives.ReadUInt16LittleEndian(payload[64..]);
        int colorGradeLength = BinaryPrimitives.ReadUInt16LittleEndian(payload[66..]);
        if (musicLength > MaxMusicEventBytes || colorGradeLength > MaxColorGradeBytes
            || payload.Length != FixedPayloadSize + musicLength + colorGradeLength)
            return false;
        float[] numbers = new float[14];
        for (int index = 0; index < numbers.Length; index++)
        {
            numbers[index] = WatchEntityPayloadCodec.ReadSingle(payload, 8 + index * 4);
            if (!float.IsFinite(numbers[index]))
                return false;
        }
        string musicEvent;
        try
        {
            musicEvent = Encoding.UTF8.GetString(payload.Slice(FixedPayloadSize, musicLength));
        }
        catch
        {
            return false;
        }
        string colorGrade;
        try
        {
            colorGrade = Encoding.UTF8.GetString(
                payload.Slice(FixedPayloadSize + musicLength, colorGradeLength)
            );
        }
        catch
        {
            return false;
        }
        value = new(
            payload[0], payload[1], payload[2],
            BinaryPrimitives.ReadInt32LittleEndian(payload[4..]),
            numbers[0], numbers[1], numbers[2], numbers[3], numbers[4],
            numbers[5], numbers[6], new(numbers[7], numbers[8]),
            numbers[9], numbers[10], numbers[11], numbers[12], numbers[13],
            musicEvent,
            colorGrade,
            new Color(payload[68], payload[69], payload[70], payload[71])
        );
        return true;
    }

    private static void BloomFadeTrigger_OnStay(
        On.Celeste.BloomFadeTrigger.orig_OnStay orig, BloomFadeTrigger self, Player player
    ) { if (!MiaoNetModule.IsWatching) orig(self, player); }

    private static void LightFadeTrigger_OnStay(
        On.Celeste.LightFadeTrigger.orig_OnStay orig, LightFadeTrigger self, Player player
    ) { if (!MiaoNetModule.IsWatching) orig(self, player); }

    private static void MusicFadeTrigger_OnStay(
        On.Celeste.MusicFadeTrigger.orig_OnStay orig, MusicFadeTrigger self, Player player
    ) { if (!MiaoNetModule.IsWatching) orig(self, player); }

    private static void AmbienceParamTrigger_OnStay(
        On.Celeste.AmbienceParamTrigger.orig_OnStay orig,
        AmbienceParamTrigger self,
        Player player
    ) { if (!MiaoNetModule.IsWatching) orig(self, player); }

    private static void MusicTrigger_OnEnter(
        On.Celeste.MusicTrigger.orig_OnEnter orig, MusicTrigger self, Player player
    ) { if (!MiaoNetModule.IsWatching) orig(self, player); }

    private static void MusicTrigger_OnLeave(
        On.Celeste.MusicTrigger.orig_OnLeave orig, MusicTrigger self, Player player
    ) { if (!MiaoNetModule.IsWatching) orig(self, player); }

    private static void AltMusicTrigger_OnEnter(
        On.Celeste.AltMusicTrigger.orig_OnEnter orig, AltMusicTrigger self, Player player
    ) { if (!MiaoNetModule.IsWatching) orig(self, player); }

    private static void AltMusicTrigger_OnLeave(
        On.Celeste.AltMusicTrigger.orig_OnLeave orig, AltMusicTrigger self, Player player
    ) { if (!MiaoNetModule.IsWatching) orig(self, player); }

    private static void BlackholeStrengthTrigger_OnEnter(
        On.Celeste.BlackholeStrengthTrigger.orig_OnEnter orig,
        BlackholeStrengthTrigger self,
        Player player
    ) { if (!MiaoNetModule.IsWatching) orig(self, player); }

    private static void MoonGlitchBackgroundTrigger_OnEnter(
        On.Celeste.MoonGlitchBackgroundTrigger.orig_OnEnter orig,
        MoonGlitchBackgroundTrigger self,
        Player player
    ) { if (!MiaoNetModule.IsWatching) orig(self, player); }

    private static void WindTrigger_OnEnter(
        On.Celeste.WindTrigger.orig_OnEnter orig, WindTrigger self, Player player
    ) { if (!MiaoNetModule.IsWatching) orig(self, player); }
}
