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

    public PooledString Pack(string value)
    {
        if (stringToID.TryGetValue(value, out int id))
        {
            // we sent this string before
            // now send just id
            return new(id: id, value: null);
        }
        else
        {
            // we haven't sent this string yet
            // send full string this time
            int nextID = nextLocalID++;
            stringToID.Add(value, nextID);
            return new(id: nextID, value);
        }
    }

    public string Resolve(PooledString pooledString)
    {
        if (idToString.TryGetValue(pooledString.ID, out string? value))
        {
            // we received it before
            return value;
        }
        else
        {
            // we haven't received it before
            // record it
            idToString.Add(pooledString.ID, pooledString.Value!);
            return pooledString.Value!;
        }
    }
}