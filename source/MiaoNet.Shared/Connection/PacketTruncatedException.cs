using System.Globalization;

namespace MiaoNet.Shared;

public sealed class PacketTruncatedException : Exception
{
    public bool IsPayload { get; }

    public int BytesRead { get; }

    public int ExpectedBytes { get; }

    public PacketTruncatedException(bool isPayload, int bytesRead, int expectedBytes)
        : base(string.Format(
            CultureInfo.InvariantCulture,
            SR.PacketTruncated,
            isPayload ? SR.PacketPartPayload : SR.PacketPartHeader,
            bytesRead,
            expectedBytes
        ))
    {
        IsPayload = isPayload;
        BytesRead = bytesRead;
        ExpectedBytes = expectedBytes;
    }
}
