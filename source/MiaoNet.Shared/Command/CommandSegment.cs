using System.Runtime.CompilerServices;

namespace MiaoNet.Shared;

public abstract record CommandSegment : IRefBinarySerializable<CommandSegment>
{
    public abstract CommandSegmentType Type { get; }

    void IRefBinarySerializable.Serialize(ref RefBinaryWriter writer)
    {
        writer.Write((byte)Type);
        switch (this)
        {
        case CommandTextSegment ts: writer.Write(ts.Text); break;
        case CommandBooleanSegment ts: writer.Write(ts.Value); break;
        case CommandPlayerSegment ps: writer.Write(ps.PlayerID); break;
        case CommandChannelSegment cs: writer.Write(cs.ChannelID); break;
        default: throw new SwitchExpressionException();
        }
    }

    static CommandSegment IRefBinarySerializable<CommandSegment>.Deserialize(ref RefBinaryReader reader)
    {
        CommandSegmentType type = (CommandSegmentType)reader.ReadByte();
        return type switch
        {
            CommandSegmentType.Text => new CommandTextSegment(reader.ReadString()),
            CommandSegmentType.Boolean => new CommandBooleanSegment(reader.ReadBoolean()),
            CommandSegmentType.Player => new CommandPlayerSegment(reader.ReadInt32()),
            CommandSegmentType.Channel => new CommandChannelSegment(reader.ReadInt32()),
            _ => throw new SwitchExpressionException()
        };
    }
}

public sealed record CommandTextSegment(string Text) : CommandSegment
{
    public override CommandSegmentType Type => CommandSegmentType.Text;
}

public sealed record CommandBooleanSegment(bool Value) : CommandSegment
{
    public override CommandSegmentType Type => CommandSegmentType.Boolean;
}

public sealed record CommandPlayerSegment(int PlayerID) : CommandSegment
{
    public override CommandSegmentType Type => CommandSegmentType.Player;
}

public sealed record CommandChannelSegment(int ChannelID) : CommandSegment
{
    public override CommandSegmentType Type => CommandSegmentType.Channel;
}