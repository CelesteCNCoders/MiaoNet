namespace MiaoNet.Shared;

public sealed partial class PlayerSessionData
{
    public readonly struct StringIntPair : IRefBinarySerializable<StringIntPair>
    {
        public string Key { get; }

        public int Value { get; }

        public StringIntPair(string key, int value)
        {
            Key = key;
            Value = value;
        }

#if MIAO_CLIENT
        public static implicit operator EntityID(StringIntPair pair)
            => new(pair.Key, pair.Value);

        public static implicit operator Session.Counter(StringIntPair pair)
            => new() { Key = pair.Key, Value = pair.Value };

        public static implicit operator StringIntPair(EntityID entityID)
            => new(entityID.Level, entityID.ID);

        public static implicit operator StringIntPair(Session.Counter counter)
            => new(counter.Key, counter.Value);
#endif

        public void Serialize(ref RefBinaryWriter writer)
        {
            writer.Write(Key);
            writer.Write(Value);
        }

        public static StringIntPair Deserialize(ref RefBinaryReader reader)
        {
            return new(reader.ReadString(), reader.ReadInt32());
        }
    }
}