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
        if (TryReadPngDimensions(data, out int width, out int height))
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
}
