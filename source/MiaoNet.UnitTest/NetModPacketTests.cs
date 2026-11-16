#pragma warning disable CA1861

using System.Buffers.Binary;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
[DoNotParallelize] // MiaoNetNetMods is not thread-safe
public sealed class NetModPacketTests
{
    private const string ModName = "miao.test.netmod";

    private const byte PingLocalID = 1;

    private const byte WhisperLocalID = 2;

    private const byte ShoutLocalID = 3;

    private static readonly byte CarrierID = PacketRegistry.GetPacketID(
        PacketNetMod.FromPayload(0, 0, PacketRoute.Broadcast(PacketScope.Global), default)
    );

    private static readonly byte DeliveryCarrierID = PacketRegistry.GetPacketID(
        PacketNetModDelivery.From(0, 0, default)
    );

    [TestMethod]
    public void BuiltInPacketLayoutIsPinned()
    {
        ByteArrayBufferWriter output = new(64);
        PacketFraming.WritePacket(output, default, new PacketPing(), new TestSerializationContext());

        byte id = PacketRegistry.GetPacketID(new PacketPing());
        CollectionAssert.AreEqual(new byte[] { 0, 0, id, 0 }, output.WrittenSpan.ToArray());
    }

    [TestMethod]
    public void NetModPacketLayoutIsPinned()
    {
        using Fixture fixture = new();

        ByteArrayBufferWriter output = new(64);
        PacketFraming.WritePacket(
            output,
            PacketEnvelope.FromSender(42),
            PacketNetMod.FromPacket(
                fixture.Index,
                PingLocalID,
                PacketRoute.Broadcast(PacketScope.Map),
                new PacketWithAnInt { Value = 7 }
            ),
            fixture.Context
        );

        byte[] expected = BuildNetModPacket(
            PacketEnvelope.FromSender(42),
            fixture.Index,
            PingLocalID,
            PacketScope.Map,
            [],
            [0x07]
        );

        CollectionAssert.AreEqual(expected, output.WrittenSpan.ToArray());
    }

    [TestMethod]
    public void RelayedDeliveryLayoutIsPinned()
    {
        ByteArrayBufferWriter output = new(64);
        PacketFraming.WritePacket(
            output,
            PacketEnvelope.FromSender(42),
            PacketNetModDelivery.From(3, 5, Payload(0xAB, 0xCD)),
            new TestSerializationContext()
        );

        CollectionAssert.AreEqual(
            BuildDeliveryPacket(PacketEnvelope.FromSender(42), 3, 5, [0xAB, 0xCD]),
            output.WrittenSpan.ToArray()
        );
    }

    [TestMethod]
    public async Task DeliveryIsDecodedByTheDeclarationThatOwnsTheId()
    {
        using Fixture fixture = new();

        ByteArrayBufferWriter output = new(64);
        PacketFraming.WritePacket(
            output,
            PacketEnvelope.FromSender(42),
            PacketNetModDelivery.From(fixture.Index, PingLocalID, Payload(0x07)),
            fixture.Context
        );

        using MemoryStream stream = new(output.WrittenSpan.ToArray());
        EnvelopedPacket? read = await PacketFraming.ReadPacketAsync(stream, fixture.Context, CancellationToken.None);

        Assert.IsNotNull(read);
        Assert.AreEqual(42, read.Value.Envelope.SenderPlayerID);

        PacketNetModDelivery delivery = (PacketNetModDelivery)read.Value.Packet;
        Assert.AreEqual(fixture.Index, delivery.NetModID);
        Assert.AreEqual(PingLocalID, delivery.LocalID);
        CollectionAssert.AreEqual(new byte[] { 0x07 }, delivery.Payload.ToArray());

        MiaoNetNetMods.Dispatch(read.Value.Envelope, delivery, fixture.Context);

        Assert.AreEqual(1, fixture.PingCalls);
        Assert.AreEqual(7, fixture.LastPing!.Value);
    }

    [TestMethod]
    public void DeclaredNetModLayoutIsPinned()
    {
        ByteArrayBufferWriter output = new(64);
        RefBinaryWriter writer = new(output);
        writer.Write(new NetModInfo("ab", 0x0102));
        writer.Flush();

        CollectionAssert.AreEqual(
            new byte[] { 0x02, 0x00, (byte)'a', (byte)'b', 0x02, 0x01 },
            output.WrittenSpan.ToArray()
        );

        RefBinaryReader reader = new(output.WrittenSpan);
        NetModInfo read = reader.Read<NetModInfo>();

        Assert.AreEqual("ab", read.Name);
        Assert.AreEqual((ushort)0x0102, read.ProtocolVersion);
        Assert.AreEqual(0, reader.BytesLeft);
    }

    [TestMethod]
    public void SendingFindsTheIdAndScopeOfThePacketsOwnType()
    {
        using Fixture fixture = new();

        fixture.NetMod.Send(new PacketWithAnInt { Value = 1 });
        fixture.NetMod.SendTo(99, new PacketWithAMessage { Text = "hi" });
        fixture.NetMod.SendTo([7, 8], new PacketShout());

        Assert.HasCount(3, fixture.Transport.Sent);

        (byte netModID, byte localID, PacketRoute route, IContextualPacket packet) = fixture.Transport.Sent[0];
        Assert.AreEqual(fixture.Index, netModID);
        Assert.AreEqual(PingLocalID, localID);
        Assert.AreEqual(PacketScope.Map, route.Scope);
        Assert.IsEmpty(route.Targets);
        Assert.AreEqual(1, ((PacketWithAnInt)packet).Value);

        (_, localID, route, _) = fixture.Transport.Sent[1];
        Assert.AreEqual(WhisperLocalID, localID);
        Assert.AreEqual(PacketScope.Player, route.Scope);
        Assert.AreEqual(99, route.Targets[0]);

        (_, localID, route, _) = fixture.Transport.Sent[2];
        Assert.AreEqual(ShoutLocalID, localID);
        Assert.AreEqual(PacketScope.Players, route.Scope);
        CollectionAssert.AreEqual(new[] { 7, 8 }, route.Targets.ToArray());
    }

    [TestMethod]
    public void ScopeMismatchFails()
    {
        using Fixture fixture = new();

        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.NetMod.SendTo(1, new PacketWithAnInt()));
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.NetMod.SendTo([1], new PacketWithAnInt()));

        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.NetMod.Send(new PacketWithAMessage()));
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.NetMod.SendTo([1, 2], new PacketWithAMessage()));

        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.NetMod.Send(new PacketShout()));
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.NetMod.SendTo(1, new PacketShout()));

        Assert.IsEmpty(fixture.Transport.Sent);
    }

    [TestMethod]
    public void SendingAPacketTheModNeverDeclaredFails()
    {
        using Fixture fixture = new();

        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.NetMod.Send(new PacketWhat()));
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.NetMod.SendTo(1, new PacketWhat()));
        Assert.ThrowsExactly<ArgumentNullException>(() => fixture.NetMod.Send(null!));

        Assert.IsEmpty(fixture.Transport.Sent);
    }

    [TestMethod]
    public void MulticastWithNoTargetIsAcceptedAndCarriesNone()
    {
        using Fixture fixture = new();
        fixture.NetMod.SendTo([], new PacketShout());

        Assert.HasCount(1, fixture.Transport.Sent);
        (_, _, PacketRoute route, _) = fixture.Transport.Sent[0];
        Assert.AreEqual(PacketScope.Players, route.Scope);
        Assert.IsEmpty(route.Targets);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => fixture.NetMod.SendTo(new int[256], new PacketShout())
        );
        Assert.HasCount(1, fixture.Transport.Sent);
    }

    [TestMethod]
    public void ANameMeansOneDeclaration()
    {
        using Fixture fixture = new();

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            MiaoNetNetMods.Register(new NetMod(ModName, 1)));

        fixture.NetMod.Dispose();
        using (MiaoNetNetMods.Register(new NetMod(ModName, 1)))
        {
        }
    }

    [TestMethod]
    public void AModCannotDeclareTheSameTypeTwice()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => new NetMod(
            "miao.test.netmod.thrice",
            1,
            NetModPacketEntry.Create<PacketWhat>(PacketScope.Map, static (_, _) => { }),
            NetModPacketEntry.Create<PacketWhat>(PacketScope.Map, static (_, _) => { })
        ));
    }

    [TestMethod]
    public void TwoModsMayDeclareTheSamePacketType()
    {
        using Fixture left = new("miao.test.netmod.left");
        using Fixture right = new("miao.test.netmod.right");

        left.NetMod.Send(new PacketWithAnInt());
        right.NetMod.Send(new PacketWithAnInt());

        Assert.HasCount(2, right.Transport.Sent);
        Assert.AreEqual(left.Index, right.Transport.Sent[0].NetModID);
        Assert.AreEqual(right.Index, right.Transport.Sent[1].NetModID);

        Assert.AreEqual(PingLocalID, right.Transport.Sent[0].LocalID);
        Assert.AreEqual(PingLocalID, right.Transport.Sent[1].LocalID);
    }

    [TestMethod]
    public void DeclaredPacketsAreDispatchedToTheirOwnHandlers()
    {
        using Fixture fixture = new();

#pragma warning disable IDE0230 // what
        MiaoNetNetMods.Dispatch(
            PacketEnvelope.FromSender(7),
            Carrier(fixture, PingLocalID, Payload(0x2A)),
            fixture.Context
        );
#pragma warning restore IDE0230

        Assert.AreEqual(1, fixture.PingCalls);
        Assert.AreEqual(42, fixture.LastPing!.Value);
        Assert.AreEqual(0, fixture.HandlerCalls);

        MiaoNetNetMods.Dispatch(
            PacketEnvelope.FromSender(42),
            Carrier(fixture, WhisperLocalID, Payload(0x02, 0x00, (byte)'h', (byte)'i')),
            fixture.Context
        );

        Assert.AreEqual(1, fixture.HandlerCalls);
        Assert.AreEqual(1, fixture.PingCalls);

        Assert.AreEqual(42, fixture.LastSenderPlayerID);
    }

    [TestMethod]
    public void PacketCannotBeDeclaredWithoutAHandler()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => NetModPacketEntry.Create<PacketWithAnInt>(PacketScope.Map, null!)
        );
    }

    [TestMethod]
    public void NetModCannnotBeDeclaredTwice()
    {
        using (MiaoNetNetMods.Register(new NetMod("miao.test.netmod.empty", 1)))
        {
            Assert.ThrowsExactly<InvalidOperationException>(
                () => MiaoNetNetMods.Register(new NetMod("miao.test.netmod.empty", 1))
            );
        }
    }

    [TestMethod]
    public void ADisposedModCannotSendAndItsNameIsAvaliableAgain()
    {
        Fixture fixture = new();

        try
        {
            fixture.Dispose();

            Assert.ThrowsExactly<InvalidOperationException>(
                () => fixture.NetMod.Send(new PacketWithAnInt())
            );

            fixture.Dispose();

            using Fixture again = new();
            Assert.IsEmpty(again.Transport.Sent);

            again.NetMod.SendTo([1], new PacketShout());
            (byte sentIndex, byte localID, _, _) = again.Transport.Sent[0];
            Assert.AreEqual(again.Index, sentIndex);
            Assert.AreEqual(ShoutLocalID, localID);
        }
        finally
        {
            if (!fixture.Disposed)
                fixture.Dispose();
        }
    }

    // hmmm this is expected currently, so a disconnection is required
    [TestMethod]
    public void IndicesWhenAModInFrontIsDisposed()
    {
        using Fixture front = new("miao.test.netmod.front");
        using Fixture behind = new("miao.test.netmod.behind");

        Assert.AreEqual(0, front.Index);
        Assert.AreEqual(1, behind.Index);

        front.Dispose();

        MiaoNetNetMods.SetTransport(behind.Transport);
        behind.NetMod.Send(new PacketWithAnInt());
        Assert.AreEqual(0, behind.Transport.Sent[0].NetModID);
    }

    [TestMethod]
    public void LocalIDsShouldStartAtOne()
    {
        using Fixture fixture = new();

        Assert.ThrowsExactly<InvalidDataException>(() => MiaoNetNetMods.Dispatch(
            PacketEnvelope.FromSender(42),
            Carrier(fixture, 0, Payload(0x07)),
            fixture.Context
        ));
    }

    [TestMethod]
    public void DispatchFailsForAnUndeclaredLocalID()
    {
        using Fixture fixture = new();

        Assert.ThrowsExactly<InvalidDataException>(() => MiaoNetNetMods.Dispatch(
            PacketEnvelope.FromSender(514),
            Carrier(fixture, 114, Payload()),
            fixture.Context
        ));
    }

    [TestMethod]
    public void DispatchFailsForAnIndexThisALCDoesNotHave()
    {
        using Fixture fixture = new();

        Assert.ThrowsExactly<InvalidDataException>(() => MiaoNetNetMods.Dispatch(
            PacketEnvelope.FromSender(514),
            PacketNetModDelivery.From((byte)(fixture.Index + 1), PingLocalID, Payload(0x07)),
            fixture.Context
        ));
    }

    [TestMethod]
    public void DispatchRefusesAPacketWithoutASender()
    {
        using Fixture fixture = new();

        Assert.ThrowsExactly<InvalidDataException>(() => MiaoNetNetMods.Dispatch(
            PacketEnvelope.FromRequest(314),
            Carrier(fixture, PingLocalID, Payload()),
            fixture.Context
        ));
    }

    [TestMethod]
    [DataRow(PacketScope.Global)]
    [DataRow(PacketScope.Channel)]
    [DataRow(PacketScope.Map)]
    public void BroadcastRoutesCarryNoTargets(PacketScope scope)
    {
        ByteArrayBufferWriter output = new(16);
        RefBinaryWriter writer = new(output);
        PacketRoute.Broadcast(scope).Write(ref writer);
        writer.Flush();

        Assert.AreEqual(1, output.WrittenCount);
        Assert.AreEqual((byte)scope, output.WrittenSpan[0]);
    }

    [TestMethod]
    public void RoutesRoundTrip()
    {
        foreach (PacketRoute route in new[]
        {
            PacketRoute.Broadcast(PacketScope.Global),
            PacketRoute.Broadcast(PacketScope.Channel),
            PacketRoute.Broadcast(PacketScope.Map),
            PacketRoute.Unicast(1234),
            PacketRoute.Multicast([1, 2, 300]),
            PacketRoute.Multicast([])
        })
        {
            ByteArrayBufferWriter output = new(32);
            RefBinaryWriter writer = new(output);
            route.Write(ref writer);
            writer.Flush();

            RefBinaryReader reader = new(output.WrittenSpan);
            PacketRoute read = PacketRoute.Read(ref reader);

            Assert.AreEqual(route.Scope, read.Scope);
            CollectionAssert.AreEqual(route.Targets.ToArray(), read.Targets.ToArray());
            Assert.AreEqual(0, reader.BytesLeft);
        }
    }

    // collections expression cannot target ReadOnlyMemory<T>
    private static ReadOnlyMemory<byte> Payload(params byte[] bytes)
        => bytes;

    private static PacketNetModDelivery Carrier(Fixture fixture, byte localID, ReadOnlyMemory<byte> payload)
        => PacketNetModDelivery.From(fixture.Index, localID, payload);

    private static byte[] BuildNetModPacket(
        PacketEnvelope envelope,
        byte netModID,
        byte localID,
        PacketScope scope,
        int[] targets,
        byte[] payload
    )
    {
        List<byte> body = [];

        if (envelope.HasSender)
            Add7BitEncodedInt(body, envelope.SenderPlayerID);
        if (envelope.HasRequestID)
            Add7BitEncodedInt(body, envelope.RequestID);

        body.Add(netModID);
        body.Add(localID);

        body.Add((byte)scope);
        if (scope == PacketScope.Player)
        {
            Add7BitEncodedInt(body, targets[0]);
        }
        else if (scope == PacketScope.Players)
        {
            body.Add((byte)targets.Length);
            foreach (int target in targets)
                Add7BitEncodedInt(body, target);
        }

        body.AddRange(payload);

        byte[] frame = new byte[Connection.PacketHeaderSize + body.Count];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, (ushort)body.Count);
        frame[sizeof(ushort)] = CarrierID;
        frame[sizeof(ushort) + sizeof(byte)] = (byte)envelope.Flags;
        body.CopyTo(frame, Connection.PacketHeaderSize);
        return frame;
    }

    private static byte[] BuildDeliveryPacket(
        PacketEnvelope envelope,
        byte netModID,
        byte localID,
        byte[] payload
    )
    {
        List<byte> body = [];

        if (envelope.HasSender)
            Add7BitEncodedInt(body, envelope.SenderPlayerID);
        if (envelope.HasRequestID)
            Add7BitEncodedInt(body, envelope.RequestID);

        body.Add(netModID);
        body.Add(localID);
        body.AddRange(payload);

        byte[] frame = new byte[Connection.PacketHeaderSize + body.Count];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, (ushort)body.Count);
        frame[sizeof(ushort)] = DeliveryCarrierID;
        frame[sizeof(ushort) + sizeof(byte)] = (byte)envelope.Flags;
        body.CopyTo(frame, Connection.PacketHeaderSize);
        return frame;
    }

    private static void Add7BitEncodedInt(List<byte> body, int value)
    {
        uint remaining = (uint)value;
        while (remaining > 0x7Fu)
        {
            body.Add((byte)(remaining | ~0x7Fu));
            remaining >>= 7;
        }

        body.Add((byte)remaining);
    }

    private sealed class Fixture : IDisposable
    {
        public MockTransport Transport { get; } = new();

        public TestSerializationContext Context { get; } = new();

        public NetMod NetMod { get; }

        public byte Index { get; }

        public int HandlerCalls { get; private set; }

        public int PingCalls { get; private set; }

        public PacketWithAnInt? LastPing { get; private set; }

        public int LastSenderPlayerID { get; private set; } = -1;

        public bool Disposed { get; private set; }

        public Fixture(string name = ModName)
        {
            NetMod = new NetMod(
                name,
                1,
                NetModPacketEntry.Create<PacketWithAnInt>(PacketScope.Map, (_, packet) =>
                {
                    PingCalls++;
                    LastPing = packet;
                }),
                NetModPacketEntry.Create<PacketWithAMessage>(PacketScope.Player, (envelope, _) =>
                {
                    HandlerCalls++;
                    LastSenderPlayerID = envelope.SenderPlayerID;
                }),
                NetModPacketEntry.Create<PacketShout>(PacketScope.Players, static (_, _) => { })
            );

            MiaoNetNetMods.Register(NetMod);
            MiaoNetNetMods.SetTransport(Transport);

            NetMod.Send(new PacketWithAnInt());
            Index = Transport.Sent[0].NetModID;
            Transport.Sent.Clear();
        }

        public void Dispose()
        {
            if (Disposed)
                return;

            Disposed = true;
            MiaoNetNetMods.SetTransport(null);
            NetMod.Dispose();
        }
    }

    private sealed class MockTransport : INetModTransport
    {
        public List<(byte NetModID, byte LocalID, PacketRoute Route, IContextualPacket Packet)> Sent { get; } = [];

        public void Send(byte netModID, byte localID, PacketRoute route, IContextualPacket packet)
            => Sent.Add((netModID, localID, route, packet));
    }

    private sealed class TestSerializationContext : IPacketSerializationContext
    {
        public PooledStringManager PooledStringManager { get; } = new(KnownPooledStrings.All);
    }

    private sealed class PacketWhat : IContextualPacket<PacketWhat>
    {
        public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context) { }

        public static PacketWhat Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context) => new();
    }

    private sealed class PacketWithAnInt : IContextualPacket<PacketWithAnInt>
    {
        public int Value { get; set; }

        public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
            => writer.Write7BitEncodedInt(Value);

        public static PacketWithAnInt Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context)
            => new() { Value = reader.Read7BitEncodedInt() };
    }

    private sealed class PacketWithAMessage : IContextualPacket<PacketWithAMessage>
    {
        public string Text { get; set; } = string.Empty;

        public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
            => writer.Write(Text);

        public static PacketWithAMessage Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context)
            => new() { Text = reader.ReadString() };
    }

    private sealed class PacketShout : IContextualPacket<PacketShout>
    {
        public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context) { }

        public static PacketShout Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context) => new();
    }
}
