using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;

namespace MiaoNet.Shared;

public sealed class PlayerState : IContextualRefBinarySerializable<PlayerState, PooledStringManager>,
    ICloneable
{
    public required Vector2 Position { get; set; }

    public required PooledString Animation { get; set; }

    public required ushort AnimationFrame { get; set; }

    public required Vector2 Scale { get; set; }

    public required PlayerStateFlags StateFlags { get; set; }

    public required byte Dashes { get; set; }

    // included only when StateFlags.Dashing
    public float LastDashDirection { get; set; }

    public required float DeltaTime { get; set; }

    // TODO some packets that update this property
    public required PlayerSpriteMode PlayerSpriteMode { get; set; }

    public required HoldableInfo HoldableInfo { get; set; }

    public required FollowerInfo[] FollowerInfos { get; set; }

    public required Vector2 WindDirection { get; set; }


    // REMIND update Clone() if there're new deep-clone needed props

    public PlayerState()
    {
    }

    public void ApplyDelta(PlayerStateDelta delta)
    {
        Position = delta.Position;
        StateFlags = delta.StateFlags;
        Animation = delta.Animation;
        AnimationFrame = delta.AnimationFrame;
        Scale = delta.Scale;

        if (delta.DashesChange)
            Dashes = delta.Dashes;

        if (delta.HasFollowerInitials)
            ApplyFollowersInitials(delta.FollowerInitials);
        else if (delta.HasFollowerDeltas)
            ApplyFollowersDeltas(delta.FollowerDeltas);

        if (delta.HasWindDirection)
            WindDirection = delta.WindDirection;

        if (delta.HasHoldable)
            ApplyHoldableInfo(delta.HoldableInfo);

        if (delta.StateFlags.HasFlag(PlayerStateFlags.Dashing))
            LastDashDirection = delta.DashDirection / (float)byte.MaxValue * MathF.Tau;
    }

    public void ApplyFollowersInitials(FollowerInfo[] followerInitials)
    {
        FollowerInfos = (FollowerInfo[])followerInitials.Clone();
    }

    public void ApplyFollowersDeltas(FollowerInfoDelta[] followersDeltas)
    {
        if (followersDeltas.Length != FollowerInfos.Length)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    SR.DeltasLengthMismatch,
                    followersDeltas.Length,
                    FollowerInfos.Length
                ),
                nameof(followersDeltas)
            );
        }
        for (int i = 0; i < followersDeltas.Length; i++)
        {
            var fi = FollowerInfos[i];
            var d = followersDeltas[i];
            FollowerInfos[i] = new(
                fi.Type, fi.SpriteID,
                d.AnimationID, d.AnimationFrame,
                d.Offset
            );
        }
    }

    public void ApplyHoldableInfo(HoldableInfo holdableInfo)
    {
        Vector2? offset = HoldableInfo.Offset;
        if (holdableInfo.Offset is not null)
            offset = holdableInfo.Offset;
        HoldableInfo = holdableInfo with { Offset = offset };
    }

    public void Serialize(ref RefBinaryWriter writer, PooledStringManager pooledStringManager)
    {
        writer.Write(Position);
        writer.Write(Animation, pooledStringManager);
        writer.Write(AnimationFrame);
        writer.Write(Scale);
        writer.Write((byte)StateFlags);
        writer.Write(Dashes);
        writer.Write(DeltaTime);
        writer.Write((int)PlayerSpriteMode);
        writer.Write(FollowerInfos, pooledStringManager);
        writer.Write(WindDirection);
        writer.Write(HoldableInfo, pooledStringManager);
        if (StateFlags.HasFlag(PlayerStateFlags.Dashing))
            writer.Write(LastDashDirection);
    }

    public static PlayerState Deserialize(ref RefBinaryReader reader, PooledStringManager pooledStringManager)
    {
        PlayerState state = new()
        {
            Position = reader.ReadVector2(),
            Animation = reader.Read<PooledString, PooledStringManager>(pooledStringManager),
            AnimationFrame = reader.ReadUInt16(),
            Scale = reader.ReadVector2(),
            StateFlags = (PlayerStateFlags)reader.ReadByte(),
            Dashes = reader.ReadByte(),
            DeltaTime = reader.ReadSingle(),
            PlayerSpriteMode = (PlayerSpriteMode)reader.ReadInt32(),
            FollowerInfos = reader.ReadArray<FollowerInfo, PooledStringManager>(pooledStringManager),
            WindDirection = reader.ReadVector2(),
            HoldableInfo = reader.Read<HoldableInfo, PooledStringManager>(pooledStringManager),
        };
        if (state.StateFlags.HasFlag(PlayerStateFlags.Dashing))
            state.LastDashDirection = reader.ReadSingle();
        return state;
    }

    public PlayerState Clone()
    {
        PlayerState shallow = (PlayerState)MemberwiseClone();
        shallow.FollowerInfos = (FollowerInfo[])FollowerInfos.Clone();
        return shallow;
    }

    object ICloneable.Clone() => Clone();
}
