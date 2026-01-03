using System.Collections.Immutable;
using System.Diagnostics;

namespace MiaoNet.Shared;

// TODO maybe client don't need concurrent
[DebuggerDisplay("LocalCount = {LocalCount}, RemoteCount = {RemoteCount}")]
public sealed class PooledStringManager
{
    private int nextLocalID;
    // only used to resolve PooledString from remote
    private ImmutableDictionary<int, string> idToString;
    // only used to pack local strings to PooledString
    private ImmutableDictionary<string, int> stringToID;

    private int LocalCount => stringToID.Count;
    private int RemoteCount => idToString.Count;

    public PooledStringManager(IEnumerable<string> initialStrings)
    {
        idToString = (initialStrings.Select((s, i) => new KeyValuePair<int, string>(i + 1, s))).ToImmutableDictionary();
        stringToID = (initialStrings.Select((s, i) => new KeyValuePair<string, int>(s, i + 1))).ToImmutableDictionary();
        nextLocalID = initialStrings.Count() + 1;
    }

    public bool GetOrCreateID(string value, out int id)
    {
        if (stringToID.TryGetValue(value, out id))
            return true;
        int nextID = Interlocked.Increment(ref nextLocalID);
        ImmutableInterlocked.Update(ref stringToID, d => stringToID.SetItem(value, nextID));
        id = nextID;
        return false;
    }

    public string GetAndRecord(int id, string? value)
    {
        if (idToString.TryGetValue(id, out string? foundValue))
        {
            if (value is not null && foundValue != value)
                throw new InvalidDataException(string.Format(SR.PooledStringValueNotMatch, foundValue, value));
            return foundValue;
        }
        else
        {
            if (value is null)
                throw new InvalidDataException(SR.MissingPooledString);
            ImmutableInterlocked.Update(ref idToString, d => d.Add(id, value));
            return value;
        }
    }
}