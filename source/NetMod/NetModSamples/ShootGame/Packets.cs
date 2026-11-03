using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNetShootGame;

/// <summary>
/// 开枪: 一条弹道, 世界坐标下的枪口与单位方向。
/// <para>
/// 谁开的枪不在这里 —— 服务端转发时会把他写进 envelope, 收件人要看身份就看那里。
/// payload 里自己写的身份既不可信也没有用武之地。
/// </para>
/// </summary>
internal sealed class PacketShoot : IContextualPacket<PacketShoot>
{
    public Vector2 Origin { get; }

    public Vector2 Direction { get; }

    public PacketShoot(Vector2 origin, Vector2 direction)
    {
        Origin = origin;
        Direction = direction;
    }

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
    {
        writer.Write(Origin);
        writer.Write(Direction);
    }

    public static PacketShoot Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context)
        => new(reader.ReadVector2(), reader.ReadVector2());
}

/// <summary>
/// 死亡通知: "我被打死了, 凶手是他"。
/// <para>
/// 发这个包的人就是被打死的那个(envelope 里的 sender), payload 只带凶手: 死亡由被打中的人判定,
/// 所以让受害者自己说, 谁也代替不了他。
/// </para>
/// </summary>
internal sealed class PacketKill : IContextualPacket<PacketKill>
{
    public int KillerPlayerID { get; }

    public PacketKill(int killerPlayerID) => KillerPlayerID = killerPlayerID;

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
        => writer.Write7BitEncodedInt(KillerPlayerID);

    public static PacketKill Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context)
        => new(reader.Read7BitEncodedInt());
}
