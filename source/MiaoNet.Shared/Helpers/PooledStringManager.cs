using System.Diagnostics;

namespace MiaoNet.Shared;

[DebuggerDisplay("LocalCount = {LocalCount}, RemoteCount = {RemoteCount}")]
public sealed class PooledStringManager
{
    private int nextLocalID;
    // only used to resolve PooledString from remote
    private readonly Dictionary<int, string> idToString;
    // only used to pack local strings to PooledString
    private readonly Dictionary<string, int> stringToID;

    private int LocalCount => stringToID.Count;
    private int RemoteCount => idToString.Count;

    public PooledStringManager(IEnumerable<string> initialStrings)
    {
        idToString = new(initialStrings.Select((s, i) => new KeyValuePair<int, string>(i + 1, s)));
        stringToID = new(initialStrings.Select((s, i) => new KeyValuePair<string, int>(s, i + 1)));
        nextLocalID = initialStrings.Count() + 1;
    }

    public bool GetOrCreateID(string value, out int id)
    {
        if (stringToID.TryGetValue(value, out id))
            return true;
        int nextID = nextLocalID++;
        stringToID[value] = nextID;
        id = nextID;
        return false;
    }

    public string GetAndRecord(int id, string? value)
    {
        if (idToString.TryGetValue(id, out string? foundValue))
        {
            if (value is not null && foundValue != value)
                throw new InvalidDataException(SR.PooledStringValueNotMatch);
            return foundValue;
        }
        else
        {
            if (value is null)
                throw new InvalidDataException(SR.MissingPooledString);
            idToString.Add(id, value);
            return value;
        }
    }
}