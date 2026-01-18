using System.Diagnostics;
using System.Text.Json.Serialization;

namespace MiaoNet.Shared;

/// <summary>
/// Player's position, dashes and so on.
/// </summary>
public sealed class PlayerState : IContextualRefBinarySerializable<PlayerState, PooledStringManager>
{
    public Vector2 Position { get; set; }

    public bool FacingLeft { get; set; }

    public byte Dashes { get; set; }

    // not serialized
    public bool Dashing { get; set; }

    public float DeltaTime { get; set; }

    // TODO some packets that update this property
    public PlayerSpriteMode PlayerSpriteMode { get; set; }

    public bool Dead { get; set; }

    public HoldableInfo HoldableInfo { get; set; }

    public FollowerInfo[] FollowerInfos { get; set; }

    public Vector2 WindDirection { get; set; }

    public bool Interactions { get; set; }

    public bool Ducking { get; set; }

    public int HeldByPlayerID { get; set; }

    public PlayerState(Vector2 position, byte dashes, float deltaTime)
    {
        Position = position;
        Dashes = dashes;
        DeltaTime = deltaTime;
        FacingLeft = false;
        PlayerSpriteMode = PlayerSpriteMode.Madeline;
        FollowerInfos = [];
        WindDirection = Vector2.Zero;
        Interactions = false;
        Ducking = false;
        HoldableInfo = new(HoldableType.None, null);
        HeldByPlayerID = 0;
    }

    public void Serialize(ref RefBinaryWriter writer, PooledStringManager pooledStringManager)
    {
        writer.Write(Position);
        writer.Write(Dashes);
        writer.Write(DeltaTime);
        writer.Write(FacingLeft);
        writer.Write((byte)PlayerSpriteMode);
        writer.Write(FollowerInfos, pooledStringManager);
        writer.Write(WindDirection);
        writer.Write(Interactions);
        writer.Write(Ducking);
        writer.Write(HoldableInfo, pooledStringManager);
        writer.Write(HeldByPlayerID);
    }

    public static PlayerState Deserialize(ref RefBinaryReader reader, PooledStringManager pooledStringManager)
        => new(reader.ReadVector2(), reader.ReadByte(), reader.ReadSingle())
        {
            FacingLeft = reader.ReadBoolean(),
            PlayerSpriteMode = (PlayerSpriteMode)reader.ReadByte(),
            FollowerInfos = reader.ReadArray<FollowerInfo, PooledStringManager>(pooledStringManager),
            WindDirection = reader.ReadVector2(),
            Interactions = reader.ReadBoolean(),
            Ducking = reader.ReadBoolean(),
            HoldableInfo = reader.Read<HoldableInfo, PooledStringManager>(pooledStringManager),
            HeldByPlayerID = reader.ReadInt32()
        };

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

    public override string ToString()
        => $"({Position.X}, {Position.Y})";
}