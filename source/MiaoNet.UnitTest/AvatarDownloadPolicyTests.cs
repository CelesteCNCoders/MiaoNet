using System.Buffers.Binary;
using System.Net;
using Celeste.Mod.MiaoNet;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class AvatarDownloadPolicyTests
{
    [TestMethod]
    public void OnlyAllowsCredentialFreeHttpsUrls()
    {
        Assert.IsTrue(AvatarDownloadPolicy.IsAllowedUri(new Uri("https://cdn.example/avatar.png")));
        Assert.IsFalse(AvatarDownloadPolicy.IsAllowedUri(new Uri("http://cdn.example/avatar.png")));
        Assert.IsFalse(AvatarDownloadPolicy.IsAllowedUri(new Uri("https://user:secret@cdn.example/avatar.png")));
        Assert.IsFalse(AvatarDownloadPolicy.IsAllowedUri(new Uri("file:///etc/passwd")));
    }

    [TestMethod]
    [DataRow("127.0.0.1")]
    [DataRow("10.0.0.1")]
    [DataRow("172.16.0.1")]
    [DataRow("192.168.1.1")]
    [DataRow("169.254.1.1")]
    [DataRow("100.64.0.1")]
    [DataRow("::1")]
    [DataRow("fd00::1")]
    [DataRow("fe80::1")]
    public void RejectsNonPublicAddresses(string text)
        => Assert.IsFalse(AvatarDownloadPolicy.IsPublicAddress(IPAddress.Parse(text)));

    [TestMethod]
    [DataRow("1.1.1.1")]
    [DataRow("8.8.8.8")]
    [DataRow("2606:4700:4700::1111")]
    public void AllowsPublicAddresses(string text)
        => Assert.IsTrue(AvatarDownloadPolicy.IsPublicAddress(IPAddress.Parse(text)));

    [TestMethod]
    public void ValidatesPngDimensions()
    {
        byte[] png = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
        "IHDR"u8.CopyTo(png.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16, 4), 64);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20, 4), 64);
        Assert.IsTrue(AvatarDownloadPolicy.IsSupportedImage(png));

        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16, 4), AvatarDownloadPolicy.MaxImageDimension + 1);
        Assert.IsFalse(AvatarDownloadPolicy.IsSupportedImage(png));
        Assert.IsFalse(AvatarDownloadPolicy.IsSupportedImage("not an image"u8));
    }

    [TestMethod]
    public void RejectsGifAndJpegImages()
    {
        ReadOnlySpan<byte> gif = [71, 73, 70, 56, 57, 97, 64, 0, 64, 0];
        ReadOnlySpan<byte> jpeg = [0xff, 0xd8, 0xff, 0xc0, 0, 7, 8, 0, 64, 0, 64];

        Assert.IsFalse(AvatarDownloadPolicy.IsSupportedImage(gif));
        Assert.IsFalse(AvatarDownloadPolicy.IsSupportedImage(jpeg));
    }
}
