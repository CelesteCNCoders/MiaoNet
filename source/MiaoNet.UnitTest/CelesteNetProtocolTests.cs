using System.Globalization;
using System.Text;

using MiaoNet.CelesteNetBridge.Protocol;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class CelesteNetProtocolTests
{
    private const string ReadyHex =
        "00 20 72 65 61 64 79 00 43 65 6c 65 73 74 65 4e 65 74 2e 53 68 61 72 65 64 00 01 00";

    private const string TickRateHex =
        "00 20 74 69 63 6b 52 61 74 65 00 43 65 6c 65 73 74 65 4e 65 74 2e 53 68 61 72 65 64 00 05 00 00 00 70 42";

    private const string PlayerInfoHex =
        "00 00 70 6c 61 79 65 72 49 6e 66 6f 00 43 65 6c 65 73 74 65 4e 65 74 2e 53 68 61 72 65 64 00 1d 00 "
        + "01 72 65 66 00 02 01 01 50 6c 61 79 65 72 00 50 6c 61 79 65 72 00 50 6c 61 79 65 72 00";

    private const string KeepAliveHex =
        "10 20 6b 65 65 70 41 6c 69 76 65 00 43 65 6c 65 73 74 65 4e 65 74 2e 53 68 61 72 65 64 00 01 00";

    private const string ChannelListHex =
        "00 00 63 68 61 6e 6e 65 6c 4c 69 73 74 00 43 65 6c 65 73 74 65 4e 65 74 2e 53 68 61 72 65 64 00 16 00 "
        + "00 01 00 00 00 6d 61 69 6e 00 00 00 00 00 01 00 00 00 01 00 00 00";

    private const string ClientRequest =
        "TEAREQ /teapot HTTP/4.2\r\n"
        + "Connection: keep-alive\r\n"
        + "CelesteNet-TeapotVersion: 2\r\n"
        + "CelesteNet-PlayerNameKey: Player\r\n"
        + "\r\n";

    private static readonly string[] RequiredHandshakeHeaders =
    {
        "CelesteNet-ConnectionToken:",
        "CelesteNet-ConnectionFeatures:",
        "CelesteNet-Settings-MaxPacketSize:",
        "CelesteNet-Settings-MaxQueueSize:",
        "CelesteNet-Settings-MergeWindow:",
        "CelesteNet-Settings-MaxHeartbeatDelay:",
        "CelesteNet-Settings-HeartbeatInterval:",
        "CelesteNet-Settings-UDPAliveScoreMax:",
        "CelesteNet-Settings-UDPDowngradeScoreMin:",
        "CelesteNet-Settings-UDPDowngradeScoreMax:",
        "CelesteNet-Settings-UDPDeathScoreMin:",
        "CelesteNet-Settings-UDPDeathScoreMax:",
    };

    [TestMethod]
    public void ReadyPayloadMatchesPythonSelftest()
    {
        byte[] payload = CelesteNetDataTypes.Ready();

        Assert.HasCount(28, payload);
        Assert.AreEqual(ReadyHex, ToHex(payload));
    }

    [TestMethod]
    public void TickRatePayloadMatchesPythonSelftest()
    {
        byte[] payload = CelesteNetDataTypes.TickRate(60f);

        Assert.HasCount(35, payload);
        Assert.AreEqual(TickRateHex, ToHex(payload));
    }

    [TestMethod]
    public void PlayerInfoPayloadMatchesPythonSelftest()
    {
        byte[] payload = CelesteNetDataTypes.PlayerInfo(1, "Player");

        Assert.HasCount(62, payload);
        Assert.AreEqual(PlayerInfoHex, ToHex(payload));
    }

    [TestMethod]
    public void KeepAlivePayloadMatchesPythonSelftest()
    {
        byte[] payload = CelesteNetDataTypes.KeepAlive();

        Assert.HasCount(32, payload);
        Assert.AreEqual(KeepAliveHex, ToHex(payload));
    }

    [TestMethod]
    public void ChannelListPayloadMatchesPythonSelftest()
    {
        byte[] payload = CelesteNetDataTypes.ChannelList(new[] { ("main", 0u, new uint[] { 1u }) });

        Assert.HasCount(56, payload);
        Assert.AreEqual(ChannelListHex, ToHex(payload));
    }

    [TestMethod]
    public void FrameWrapsReadyWithLittleEndianLengthPrefix()
    {
        byte[] frame = CelesteNetFraming.Wrap(CelesteNetDataTypes.Ready());

        Assert.HasCount(30, frame);
        Assert.AreEqual("1c 00 " + ReadyHex, ToHex(frame));
    }

    [TestMethod]
    public void TryReadReassemblesFrameFedByteByByte()
    {
        byte[] frame = CelesteNetFraming.Wrap(CelesteNetDataTypes.Ready());

        for (int length = 0; length < frame.Length; length++)
        {
            Assert.IsFalse(
                CelesteNetFraming.TryRead(frame.AsSpan(0, length), out byte[] partialPayload, out int partialConsumed),
                $"A {length} B prefix must not yield a frame."
            );
            Assert.IsEmpty(partialPayload);
            Assert.AreEqual(0, partialConsumed);
        }

        Assert.IsTrue(CelesteNetFraming.TryRead(frame, out byte[] payload, out int consumed));
        Assert.AreEqual(ReadyHex, ToHex(payload));
        Assert.AreEqual(frame.Length, consumed);
    }

    [TestMethod]
    public void TryReadConsumesOneFrameAtATime()
    {
        byte[] readyFrame = CelesteNetFraming.Wrap(CelesteNetDataTypes.Ready());
        byte[] keepAliveFrame = CelesteNetFraming.Wrap(CelesteNetDataTypes.KeepAlive());
        byte[] buffer = new byte[readyFrame.Length + keepAliveFrame.Length];
        readyFrame.CopyTo(buffer, 0);
        keepAliveFrame.CopyTo(buffer, readyFrame.Length);

        Assert.IsTrue(CelesteNetFraming.TryRead(buffer, out byte[] firstPayload, out int firstConsumed));
        Assert.AreEqual(readyFrame.Length, firstConsumed);
        Assert.AreEqual(ReadyHex, ToHex(firstPayload));

        Assert.IsTrue(CelesteNetFraming.TryRead(buffer.AsSpan(firstConsumed), out byte[] secondPayload, out int secondConsumed));
        Assert.AreEqual(keepAliveFrame.Length, secondConsumed);
        Assert.AreEqual(KeepAliveHex, ToHex(secondPayload));

        Assert.IsFalse(
            CelesteNetFraming.TryRead(buffer.AsSpan(firstConsumed + secondConsumed), out _, out _),
            "The buffer must be fully consumed."
        );
    }

    [TestMethod]
    public void TryReadRejectsFrameOverMaxPacketSize()
    {
        // 2049 = 0x0801, one byte over the handshake-advertised limit.
        byte[] buffer = new byte[] { 0x01, 0x08, 0x00, 0x00 };

        Assert.ThrowsExactly<InvalidDataException>(() =>
        {
            CelesteNetFraming.TryRead(buffer, out _, out _);
        });
    }

    [TestMethod]
    public void TryReadAcceptsFrameAtMaxPacketSize()
    {
        byte[] payload = new byte[CelesteNetFraming.MaxPacketSize];
        for (int i = 0; i < payload.Length; i++)
            payload[i] = (byte)i;

        byte[] frame = CelesteNetFraming.Wrap(payload);

        Assert.AreEqual(0x00, frame[0]);
        Assert.AreEqual(0x08, frame[1]);
        Assert.HasCount(payload.Length + sizeof(ushort), frame);

        Assert.IsTrue(CelesteNetFraming.TryRead(frame, out byte[] roundTripped, out int consumed));
        Assert.AreEqual(frame.Length, consumed);
        CollectionAssert.AreEqual(payload, roundTripped);
    }

    [TestMethod]
    public void WrapRejectsPayloadOverMaxPacketSize()
    {
        byte[] payload = new byte[CelesteNetFraming.MaxPacketSize + 1];

        Assert.ThrowsExactly<ArgumentException>(() =>
        {
            CelesteNetFraming.Wrap(payload);
        });
    }

    [TestMethod]
    public void HandshakeResponseStartsWith418AndEndsWithBlankLineAfterBody()
    {
        byte[] response = CelesteNetHandshake.BuildResponse();
        string text = Encoding.UTF8.GetString(response);

        Assert.AreEqual((byte)'H', response[0], "The response must not carry a UTF-8 BOM.");
        Assert.IsTrue(
            text.StartsWith(CelesteNetHandshake.TeapotLine + "\r\n", StringComparison.Ordinal),
            "The first line must be the 418 teapot status line."
        );
        Assert.IsTrue(
            text.Contains("\r\n\r\nWho wants some tea!\r\n\r\n", StringComparison.Ordinal),
            "The client reads the body up to a blank line, so the body must be non-empty and terminated."
        );
    }

    [TestMethod]
    public void HandshakeResponseIsExactly598Bytes()
    {
        byte[] response = CelesteNetHandshake.BuildResponse();

        Assert.HasCount(598, response);
        Assert.HasCount(CelesteNetHandshake.ResponseSize, response);
    }

    [TestMethod]
    public void HandshakeResponseCarriesAllRequiredHeaders()
    {
        string text = Encoding.UTF8.GetString(CelesteNetHandshake.BuildResponse());

        foreach (string header in RequiredHandshakeHeaders)
        {
            Assert.IsTrue(
                text.Contains(header, StringComparison.Ordinal),
                $"The handshake response is missing the '{header}' header."
            );
        }

        Assert.IsTrue(text.Contains("CelesteNet-ConnectionToken: DEADBEEF\r\n", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("CelesteNet-ConnectionFeatures: \r\n", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("CelesteNet-Settings-MaxPacketSize: 2048\r\n", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("CelesteNet-Settings-UDPDeathScoreMax: -1\r\n", StringComparison.Ordinal));
    }

    [TestMethod]
    public void HandshakeSettingsValuesAreIntegerLiterals()
    {
        string text = Encoding.UTF8.GetString(CelesteNetHandshake.BuildResponse());
        int settingsCount = 0;

        foreach (string line in text.Split("\r\n"))
        {
            if (!line.StartsWith("CelesteNet-Settings-", StringComparison.Ordinal))
                continue;

            settingsCount++;

            int colon = line.IndexOf(':', StringComparison.Ordinal);
            Assert.IsGreaterThan(0, colon, $"Header '{line}' has no value separator.");

            string value = line[(colon + 1)..].Trim();
            Assert.IsTrue(
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
                $"Header '{line}' must parse with Int32.Parse in any culture."
            );
        }

        Assert.AreEqual(10, settingsCount, "All ten CelesteNet-Settings-* headers are mandatory.");
    }

    [TestMethod]
    public void TryReadRequestWaitsForTheBlankLine()
    {
        byte[] request = Encoding.UTF8.GetBytes(ClientRequest);

        for (int length = 0; length < request.Length; length++)
        {
            Assert.IsFalse(
                CelesteNetHandshake.TryReadRequest(request.AsSpan(0, length), out string firstLine, out int requestBytes),
                $"A {length} B prefix must not complete the request."
            );
            Assert.AreEqual(string.Empty, firstLine);
            Assert.AreEqual(0, requestBytes);
        }

        Assert.IsTrue(CelesteNetHandshake.TryReadRequest(request, out string parsedFirstLine, out int parsedRequestBytes));
        Assert.AreEqual("TEAREQ /teapot HTTP/4.2", parsedFirstLine);
        Assert.AreEqual(request.Length, parsedRequestBytes);
    }

    [TestMethod]
    public void TryReadRequestStopsAtTheTerminator()
    {
        byte[] request = Encoding.UTF8.GetBytes(ClientRequest);
        byte[] frame = CelesteNetFraming.Wrap(CelesteNetDataTypes.KeepAlive());
        byte[] combined = new byte[request.Length + frame.Length];
        request.CopyTo(combined, 0);
        frame.CopyTo(combined, request.Length);

        Assert.IsTrue(CelesteNetHandshake.TryReadRequest(combined, out string firstLine, out int requestBytes));
        Assert.AreEqual("TEAREQ /teapot HTTP/4.2", firstLine);
        Assert.AreEqual(request.Length, requestBytes);

        Assert.IsTrue(CelesteNetFraming.TryRead(combined.AsSpan(requestBytes), out byte[] payload, out int consumed));
        Assert.AreEqual(frame.Length, consumed);
        Assert.AreEqual(KeepAliveHex, ToHex(payload));
    }

    [TestMethod]
    public void TryReadTypeIdParsesKnownDataTypes()
    {
        Assert.AreEqual("ready", CelesteNetTypeReader.TryReadTypeId(CelesteNetDataTypes.Ready()));
        Assert.AreEqual("tickRate", CelesteNetTypeReader.TryReadTypeId(CelesteNetDataTypes.TickRate(60f)));
        Assert.AreEqual("playerInfo", CelesteNetTypeReader.TryReadTypeId(CelesteNetDataTypes.PlayerInfo(1, "Player")));
        Assert.AreEqual("keepAlive", CelesteNetTypeReader.TryReadTypeId(CelesteNetDataTypes.KeepAlive()));
        Assert.AreEqual(
            "channelList",
            CelesteNetTypeReader.TryReadTypeId(CelesteNetDataTypes.ChannelList(new[] { ("main", 0u, new uint[] { 1u }) }))
        );
    }

    [TestMethod]
    public void TryReadTypeIdRejectsMalformedPayloads()
    {
        Assert.IsNull(CelesteNetTypeReader.TryReadTypeId(ReadOnlySpan<byte>.Empty));
        Assert.IsNull(CelesteNetTypeReader.TryReadTypeId(new byte[] { 0x00, 0x20 }));
        Assert.IsNull(CelesteNetTypeReader.TryReadTypeId(new byte[] { 0x00, 0x20, 0x61 }));
        Assert.IsNull(CelesteNetTypeReader.TryReadTypeId(new byte[] { 0x00, 0x20, 0x00 }));
        // Slim packets carry a mapping varint instead of a type ID string.
        Assert.IsNull(CelesteNetTypeReader.TryReadTypeId(new byte[] { 0x00, 0x80, 0x01, 0x01 }));
    }

    [TestMethod]
    public void EncodeWritesCustomTypeWithMeta()
    {
        byte[] payload = CelesteNetDataTypes.Encode(
            0x0010,
            "myThing",
            new byte[] { 0x2a },
            new[] { ("m", new byte[] { 0x01 }) }
        );

        Assert.AreEqual(
            "10 00 6d 79 54 68 69 6e 67 00 43 65 6c 65 73 74 65 4e 65 74 2e 53 68 61 72 65 64 00 "
            + "06 00 01 6d 00 01 01 2a",
            ToHex(payload)
        );
        Assert.AreEqual("myThing", CelesteNetTypeReader.TryReadTypeId(payload));
    }

    [TestMethod]
    public void EncodeUsesOneLengthByteOnlyWhenSmallBodyFits()
    {
        byte[] fits = CelesteNetDataTypes.Encode(CelesteNetDataTypes.FlagSmall, "x", new byte[254]);
        Assert.HasCount(2 + 2 + 18 + 1 + 255, fits);
        Assert.AreEqual(0xff, fits[22]);
        Assert.AreEqual(0x00, fits[23], "The first byte of the body is metaCount.");

        byte[] tooLong = new byte[255];
        Assert.ThrowsExactly<ArgumentException>(() =>
        {
            CelesteNetDataTypes.Encode(CelesteNetDataTypes.FlagSmall, "x", tooLong);
        });
    }

    private static string ToHex(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return string.Empty;

        return string.Join(' ', bytes.ToArray().Select(static b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }
}
