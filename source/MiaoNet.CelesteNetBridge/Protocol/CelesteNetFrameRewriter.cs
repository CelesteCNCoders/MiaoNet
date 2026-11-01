using System.Globalization;
using System.Text;

namespace MiaoNet.CelesteNetBridge.Protocol;

/// <summary>
/// Header surgery on a CelesteNet DataType frame. The bridge hands frames through unchanged except
/// for the source field, which must name the player id the receiving client knows.
/// </summary>
public static class CelesteNetFrameRewriter {

    /// <summary>
    /// Replaces the source netstring with <paramref name="sourceId"/>. Layout is
    /// <c>u16 flags | netstring typeID | netstring source | body</c>, and the body length field counts
    /// only what follows it, so changing the source does not touch anything after it.
    /// </summary>
    public static byte[] RewriteSource(byte[] payload, uint sourceId) {
        int start = SkipNetString(payload, 2);
        if (start < 0)
            return payload;

        int end = SkipNetString(payload, start);
        if (end < 0)
            return payload;

        byte[] source = Encoding.ASCII.GetBytes(sourceId.ToString(CultureInfo.InvariantCulture));
        byte[] result = new byte[payload.Length - (end - start) + source.Length + 1];
        Buffer.BlockCopy(payload, 0, result, 0, start);
        Buffer.BlockCopy(source, 0, result, start, source.Length);
        result[start + source.Length] = 0;
        Buffer.BlockCopy(payload, end, result, start + source.Length + 1, payload.Length - end);
        return result;
    }

    /// <summary>Returns the index just past the netstring starting at <paramref name="offset"/>, or -1.</summary>
    private static int SkipNetString(byte[] payload, int offset) {
        if (offset < 0 || offset >= payload.Length)
            return -1;

        int index = Array.IndexOf(payload, (byte)0, offset);
        return index < 0 ? -1 : index + 1;
    }
}
