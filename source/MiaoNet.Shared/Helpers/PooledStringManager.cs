using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace MiaoNet.Shared;

[DebuggerDisplay("LocalCount = {LocalCount}, RemoteCount = {RemoteCount}")]
public sealed class PooledStringManager
{
    public const int MaxRemoteEntries = 4096;
    public const int MaxRemoteStringUtf8Bytes = 1024;
    public const int MaxRemoteTotalUtf8Bytes = 1024 * 1024;

    private int currentLocalID;
    private int nextRemoteID;
    private int remoteLearnedCount;
    private int remoteLearnedUtf8Bytes;

    // only used to resolve PooledString from remote
    private readonly Dictionary<int, string> idToString;

    // only used to pack local strings to PooledString
    private readonly Dictionary<string, int> stringToID;

    private int LocalCount => stringToID.Count;
    private int RemoteCount => idToString.Count;

    public PooledStringManager(IEnumerable<string> initialStrings)
    {
        ArgumentNullException.ThrowIfNull(initialStrings);
        string[] initial = initialStrings.ToArray();
        idToString = new(initial.Select((s, i) => new KeyValuePair<int, string>(i + 1, s)));
        stringToID = new(initial.Select((s, i) => new KeyValuePair<string, int>(s, i + 1)));
        currentLocalID = initial.Length + 1;
        nextRemoteID = initial.Length + 1;
    }

    public bool GetOrCreateID(string value, out int id)
    {
        if (stringToID.TryGetValue(value, out id))
            return true;

        int nextID = currentLocalID++;
        stringToID.Add(value, nextID);
        id = nextID;
        return false;
    }

    public string GetAndRecord(int id, string? value)
    {
        if (id <= 0)
            throw new InvalidDataException(string.Format(CultureInfo.InvariantCulture, SR.InvalidPooledStringID, id));

        if (idToString.TryGetValue(id, out string? foundValue))
        {
            if (value is not null && foundValue != value)
                throw new InvalidDataException(string.Format(CultureInfo.InvariantCulture, SR.PooledStringValueNotMatch, foundValue, value));
            return foundValue;
        }
        else
        {
            if (value is null)
                throw new InvalidDataException(string.Format(CultureInfo.InvariantCulture, SR.MissingPooledString, id));
            ValidateNewRemoteEntry(id, value, out int utf8ByteCount);
            idToString.Add(id, value);
            RecordNewRemoteEntry(utf8ByteCount);
            return value;
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
