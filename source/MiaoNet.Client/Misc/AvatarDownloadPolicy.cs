using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Celeste.Mod.MiaoNet;

internal static class AvatarDownloadPolicy
{
    internal const int MaxDownloadBytes = 2 * 1024 * 1024;
    internal const int MaxImageDimension = 2048;
    internal const int MaxRedirects = 3;

    internal static bool IsAllowedUri(Uri uri)
        => uri.IsAbsoluteUri
            && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.UserInfo);

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            ReadOnlySpan<byte> bytes = address.GetAddressBytes();
            byte a = bytes[0], b = bytes[1], c = bytes[2];
            return a != 0 && a != 10 && a != 127
                && !(a == 100 && b is >= 64 and <= 127)
                && !(a == 169 && b == 254)
                && !(a == 172 && b is >= 16 and <= 31)
                && !(a == 192 && b == 0 && c is 0 or 2)
                && !(a == 192 && b == 168)
                && !(a == 198 && b is 18 or 19)
                && !(a == 198 && b == 51 && c == 100)
                && !(a == 203 && b == 0 && c == 113)
                && a < 224;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            ReadOnlySpan<byte> bytes = address.GetAddressBytes();
            return !IPAddress.IsLoopback(address)
                && !address.IsIPv6LinkLocal
                && !address.IsIPv6Multicast
                && !address.IsIPv6SiteLocal
                && !(bytes[0] is 0xfc or 0xfd)
                && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8)
                && !address.Equals(IPAddress.IPv6Any)
                && !address.Equals(IPAddress.IPv6None);
        }

        return false;
    }

    internal static bool IsSupportedImage(ReadOnlySpan<byte> data)
    {
        if (TryReadPngDimensions(data, out int width, out int height)
            || TryReadGifDimensions(data, out width, out height)
            || TryReadJpegDimensions(data, out width, out height))
        {
            return width is > 0 and <= MaxImageDimension
                && height is > 0 and <= MaxImageDimension;
        }
        return false;
    }

    private static bool TryReadPngDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (data.Length >= 24 && data[..8].SequenceEqual(signature) && data.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            width = BinaryPrimitives.ReadInt32BigEndian(data.Slice(16, 4));
            height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(20, 4));
            return true;
        }
        width = height = 0;
        return false;
    }

    private static bool TryReadGifDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        if (data.Length >= 10 && (data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8)))
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6, 2));
            height = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2));
            return true;
        }
        width = height = 0;
        return false;
    }

    private static bool TryReadJpegDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = height = 0;
        if (data.Length < 4 || data[0] != 0xff || data[1] != 0xd8)
            return false;

        int offset = 2;
        while (offset + 4 <= data.Length)
        {
            while (offset < data.Length && data[offset] == 0xff)
                offset++;
            if (offset >= data.Length)
                return false;
            byte marker = data[offset++];
            if (marker is 0xd8 or 0xd9 || marker is >= 0xd0 and <= 0xd7)
                continue;
            if (offset + 2 > data.Length)
                return false;
            int segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
            if (segmentLength < 2 || offset + segmentLength > data.Length)
                return false;
            if (marker is 0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7
                or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf)
            {
                if (segmentLength < 7)
                    return false;
                height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 3, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 5, 2));
                return true;
            }
            offset += segmentLength;
        }
        return false;
    }
}
