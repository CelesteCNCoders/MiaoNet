#if MIAO_SERVER
#define CONCURRENT
#endif

#if CONCURRENT
using System.Collections.Immutable;
#endif
using System.Diagnostics;

namespace MiaoNet.Shared;

[DebuggerDisplay("LocalCount = {LocalCount}, RemoteCount = {RemoteCount}")]
public sealed class PooledStringManager
{
    private int currentLocalID;
#if CONCURRENT
    // only used to resolve PooledString from remote
    private ImmutableDictionary<int, string> idToString;
    // only used to pack local strings to PooledString
    private ImmutableDictionary<string, int> stringToID;
    private readonly Lock thisLock = new();
#else
    // only used to resolve PooledString from remote
    private readonly Dictionary<int, string> idToString;
    // only used to pack local strings to PooledString
    private readonly Dictionary<string, int> stringToID;
#endif

    private int LocalCount => stringToID.Count;
    private int RemoteCount => idToString.Count;

    public PooledStringManager(IEnumerable<string> initialStrings)
    {
#if CONCURRENT
        idToString = (initialStrings.Select((s, i) => new KeyValuePair<int, string>(i + 1, s))).ToImmutableDictionary();
        stringToID = (initialStrings.Select((s, i) => new KeyValuePair<string, int>(s, i + 1))).ToImmutableDictionary();
#else
        idToString = new(initialStrings.Select((s, i) => new KeyValuePair<int, string>(i + 1, s)));
        stringToID = new(initialStrings.Select((s, i) => new KeyValuePair<string, int>(s, i + 1)));
#endif
        currentLocalID = initialStrings.Count() + 1;
    }

    public bool GetOrCreateID(string value, out int id)
    {
        if (stringToID.TryGetValue(value, out id))
            return true;
#if CONCURRENT
        lock (thisLock)
        {
            if (stringToID.TryGetValue(value, out id))
                return true;

            int nextID = currentLocalID++;
            stringToID = stringToID.Add(value, nextID);
            id = nextID;
            return false;
        }
#else
        int nextID = currentLocalID++;
        stringToID.Add(value, nextID);
        id = nextID;
        return false;
#endif
    }

    public string GetAndRecord(int id, string? value)
    {
        if (idToString.TryGetValue(id, out string? firstFoundValue))
        {
            if (value is not null && firstFoundValue != value)
                throw new InvalidDataException(string.Format(SR.PooledStringValueNotMatch, firstFoundValue, value));
            return firstFoundValue;
        }
        else
        {
#if CONCURRENT
            lock (thisLock)
            {
                if (idToString.TryGetValue(id, out string? laterFoundValue))
                {
                    if (value is not null && laterFoundValue != value)
                        throw new InvalidDataException(string.Format(SR.PooledStringValueNotMatch, laterFoundValue, value));
                    return laterFoundValue;
                }

                if (value is null)
                    throw new InvalidDataException(string.Format(SR.MissingPooledString, id));
                idToString = idToString.Add(id, value);
                return value;
            }
#else
            if (value is null)
                throw new InvalidDataException(string.Format(SR.MissingPooledString, id));
            idToString.Add(id, value);
            return value;
#endif
        }
    }
}