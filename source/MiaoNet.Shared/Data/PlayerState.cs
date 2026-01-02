using System.Diagnostics;
using System.Text.Json.Serialization;
#if MIAO_SERVER
using MiaoNet.Server.Primitives;
#endif
namespace MiaoNet.Shared;

/// <summary>
/// Player's position, dashes and so on.
/// </summary>
public sealed class PlayerState : IContextualRefBinarySerializable<PlayerState, PooledStringManager>
{
    public Vector2 Position { get; set; }

    public byte Dashes { get; set; }

    // not serialized
    public bool Dashing { get; set; }

    public float DeltaTime { get; set; }

    // TODO some packets that update this property
    public PlayerSpriteMode PlayerSpriteMode { get; set; }

    public bool Dead { get; set; }

    public FollowerInfo[] FollowerInfos { get; set; }

    public PlayerState(Vector2 position, byte dashes, float deltaTime)
    {
        Position = position;
        Dashes = dashes;
        DeltaTime = deltaTime;
        FollowerInfos = [];
    }

    public void Serialize(ref RefBinaryWriter writer, PooledStringManager pooledStringManager)
    {
        writer.Write(Position);
        writer.Write(Dashes);
        writer.Write(DeltaTime);
        writer.Write((byte)PlayerSpriteMode);
        writer.Write(FollowerInfos, pooledStringManager);
    }

    public static PlayerState Deserialize(ref RefBinaryReader reader, PooledStringManager pooledStringManager)
        => new(reader.ReadVector2(), reader.ReadByte(), reader.ReadSingle())
        {
            PlayerSpriteMode = (PlayerSpriteMode)reader.ReadByte(),
            FollowerInfos = reader.ReadArray<FollowerInfo, PooledStringManager>(pooledStringManager)
        };

    public void ApplyFollowersInitials(FollowerInfo[] followerInitials)
    {
        FollowerInfos = (FollowerInfo[])followerInitials.Clone();
    }

    public void ApplyFollowersDeltas(FollowerInfoDelta[] followersDeltas)
    {
        for (int i = 0; i < FollowerInfos.Length; i++)
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

    public override string ToString()
        => $"({Position.X}, {Position.Y})";
}