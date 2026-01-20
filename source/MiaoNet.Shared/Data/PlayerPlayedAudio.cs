using System.Diagnostics.CodeAnalysis;

namespace MiaoNet.Shared;

public readonly struct PlayerPlayedAudio : IContextualRefBinarySerializable<PlayerPlayedAudio, PooledStringManager>
{
    public const string EventPrefix = "event:";
    public const string EventMadelinePrefix = $"{EventPrefix}/char/madeline/";

    public readonly string Event;
    public readonly string? Param;
    public readonly float ParamValue;

    [MemberNotNullWhen(true, nameof(Param))]
    public readonly bool HasParam => Param is not null;

    public PlayerPlayedAudio(string @event, string? param = null, float paramValue = 0f)
    {
        if (!@event.StartsWith(EventPrefix))
            throw new ArgumentException(null, nameof(@event));
        Event = @event;
        Param = param is null ? null : new PooledString?(param);
        ParamValue = paramValue;
    }

    public void Serialize(ref RefBinaryWriter writer, PooledStringManager context)
    {
        string s = Event.StartsWith(EventMadelinePrefix)
            ? Event[EventMadelinePrefix.Length..]
            : Event[EventPrefix.Length..];
        writer.Write(new PooledString(s), context);
        if (HasParam)
        {
            writer.Write(true);
            writer.Write(new PooledString(Param), context);
            writer.Write(ParamValue);
        }
        else
        {
            writer.Write(false);
        }
    }

    public static PlayerPlayedAudio Deserialize(ref RefBinaryReader reader, PooledStringManager context)
    {
        string s = reader.Read<PooledString, PooledStringManager>(context);
        bool hasParam = reader.ReadBoolean();
        string? param = null;
        float paramValue = 0f;
        if (hasParam)
        {
            param = reader.Read<PooledString, PooledStringManager>(context);
            paramValue = reader.ReadSingle();
        }
        string @event = s.StartsWith('/')
            ? EventPrefix + s
            : EventMadelinePrefix + s;
        return new(@event, param, paramValue);
    }
}