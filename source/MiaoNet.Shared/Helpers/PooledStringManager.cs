#if MIAO_SERVER
#define CONCURRENT
#endif

#if CONCURRENT
using System.Collections.Immutable;
#endif
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace MiaoNet.Shared;

[DebuggerDisplay("LocalCount = {LocalCount}, RemoteCount = {RemoteCount}")]
public sealed class PooledStringManager
{
    // Initial, locally-known strings are trusted and don't consume these per-peer learning quotas.
    public const int MaxRemoteEntries = 4096;
    public const int MaxRemoteStringUtf8Bytes = 1024;
    public const int MaxRemoteTotalUtf8Bytes = 1024 * 1024;

    private int currentLocalID;
    // Local and remote ID spaces both start after the shared initial strings, but advance independently.
    private int nextRemoteID;
    private int remoteLearnedCount;
    private int remoteLearnedUtf8Bytes;
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
        ArgumentNullException.ThrowIfNull(initialStrings);
        string[] initial = initialStrings.ToArray();
#if CONCURRENT
        idToString = (initial.Select((s, i) => new KeyValuePair<int, string>(i + 1, s))).ToImmutableDictionary();
        stringToID = (initial.Select((s, i) => new KeyValuePair<string, int>(s, i + 1))).ToImmutableDictionary();
#else
        idToString = new(initial.Select((s, i) => new KeyValuePair<int, string>(i + 1, s)));
        stringToID = new(initial.Select((s, i) => new KeyValuePair<string, int>(s, i + 1)));
#endif
        currentLocalID = initial.Length + 1;
        nextRemoteID = initial.Length + 1;
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
        if (id <= 0)
            throw new InvalidDataException(string.Format(CultureInfo.InvariantCulture, SR.InvalidPooledStringID, id));

        if (idToString.TryGetValue(id, out string? firstFoundValue))
        {
            if (value is not null && firstFoundValue != value)
                throw new InvalidDataException(string.Format(CultureInfo.InvariantCulture, SR.PooledStringValueNotMatch, firstFoundValue, value));
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
                        throw new InvalidDataException(string.Format(CultureInfo.InvariantCulture, SR.PooledStringValueNotMatch, laterFoundValue, value));
                    return laterFoundValue;
                }

                if (value is null)
                    throw new InvalidDataException(string.Format(CultureInfo.InvariantCulture, SR.MissingPooledString, id));
                ValidateNewRemoteEntry(id, value, out int utf8ByteCount);
                idToString = idToString.Add(id, value);
                RecordNewRemoteEntry(utf8ByteCount);
                return value;
            }
#else
            if (value is null)
                throw new InvalidDataException(string.Format(CultureInfo.InvariantCulture, SR.MissingPooledString, id));
            ValidateNewRemoteEntry(id, value, out int utf8ByteCount);
            idToString.Add(id, value);
            RecordNewRemoteEntry(utf8ByteCount);
            return value;
#endif
        }
    }

    private void ValidateNewRemoteEntry(int id, string value, out int utf8ByteCount)
    {
        if (id != nextRemoteID)
        {
            throw new InvalidDataException(string.Format(
                CultureInfo.InvariantCulture,
                SR.UnexpectedPooledStringID,
                id,
                nextRemoteID
            ));
        }

        if (remoteLearnedCount >= MaxRemoteEntries)
            throw new InvalidDataException(SR.PooledStringEntryLimitExceeded);

        utf8ByteCount = Encoding.UTF8.GetByteCount(value);
        if (utf8ByteCount > MaxRemoteStringUtf8Bytes)
        {
            throw new InvalidDataException(string.Format(
                CultureInfo.InvariantCulture,
                SR.PooledStringValueTooLarge,
                utf8ByteCount,
                MaxRemoteStringUtf8Bytes
            ));
        }

        if (remoteLearnedUtf8Bytes > MaxRemoteTotalUtf8Bytes - utf8ByteCount)
            throw new InvalidDataException(SR.PooledStringTotalBytesExceeded);
    }

    private void RecordNewRemoteEntry(int utf8ByteCount)
    {
        remoteLearnedCount++;
        remoteLearnedUtf8Bytes += utf8ByteCount;
        nextRemoteID++;
    }
}
