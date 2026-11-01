using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.MiaoNetCelesteNetShim;

namespace Celeste.Mod.CelesteNet.Client;

/// <summary>
/// Turns a mod's data type into the bytes of one CelesteNet frame and back.
/// <para>
/// The payload keeps CelesteNet's own layout — <c>[DataID string][source string][meta and
/// body]</c> — so the receiving side identifies the type through its DataID, which both peers
/// agree on because they run the same mods.
/// </para>
/// </summary>
internal static class CelesteNetCompatTransport
{
    internal static void Send(CelesteNetClient client, DataType data, bool handleLocally)
    {
        // Every report below is once per data type: what a mod does here it does for every packet,
        // and the mods which do it wrong are the ones which do it sixty times a second.
        if (client.Con is null)
        {
            CelesteNetCompatBackend.DiagOnce($"send:{data.GetTypeID(client.Data)}:no-client",
                $"Dropped {data.GetType().Name}: the CelesteNet client is not started.");
            return;
        }
        if (!data.FilterSend(client.Data))
        {
            CelesteNetCompatBackend.DiagOnce($"send:{data.GetTypeID(client.Data)}:filtered",
                $"Dropped {data.GetType().Name}: FilterSend said no.");
            return;
        }
        if (CelesteNetCompatInertData.IsInert(data, client.Data))
        {
            // The local handler runs before the sender is stamped, which is what the original does
            // for SendAndHandle: a mod which handles its own packet sees the one it built, not a
            // copy the transport edited.
            if (handleLocally)
                client.Data.Handle(client.Con, data);
            return;
        }

        client.EnsureDataTypesScanned();
        StampSender(client, data);

        byte[] payload;
        try
        {
            using MemoryStream stream = new();
            using (CelesteNetBinaryWriter writer = new(client.Data, stream))
            {
                writer.Write(data.GetTypeID(client.Data));
                writer.Write(data.GetSource(client.Data));
                data.WriteAll(writer);
            }
            payload = stream.ToArray();
        }
        catch (Exception e)
        {
            CelesteNetCompatBackend.WarnOnce($"send:{data.GetTypeID(client.Data)}:unserializable",
                $"Failed to serialize {data.GetType()}: {e}");
            return;
        }

        CelesteNetCompatBackend.Send(payload);

        if (handleLocally)
            client.Data.Handle(client.Con, data);
    }

    /// <summary>
    /// Decodes one payload and hands the data to the context, which is where every mod's
    /// registered handler listens. MiaoNet dispatches a received net-mod packet from its own
    /// update, so this runs on the main thread.
    /// </summary>
    internal static void Receive(int senderPlayerID, byte[] payload)
    {
        if (payload.Length == 0)
            return;

        CelesteNetClient? client = CelesteNetClientModule.Instance?.Client;
        CelesteNetConnection? con = client?.Con;
        if (client is null || con is null)
        {
            CelesteNetCompatBackend.DiagOnce("recv:no-client",
                $"Dropped {payload.Length} byte(s) from player {senderPlayerID}: the CelesteNet client is not started."
            );
            return;
        }

        client.EnsureDataTypesScanned();

        DataType data;
        // Kept to name the type in the reports below, which are once per type rather than once per
        // packet: a mismatch between two peers repeats for as long as they keep exchanging it.
        string typeID = string.Empty;
        try
        {
            using MemoryStream stream = new(payload);
            using CelesteNetBinaryReader reader = new(client.Data, stream);

            typeID = reader.ReadString();
            // The payload mirrors the original framing: the data id followed by the source
            // assembly name. Reading it is what keeps the reader aligned with the writer.
            _ = reader.ReadString();

            if (!client.Data.IDToDataType.TryGetValue(typeID, out Type? type))
            {
                CelesteNetCompatBackend.WarnOnce($"recv:{typeID}:unregistered",
                    $"Dropped {senderPlayerID}'s packet: no data type registered for '{typeID}'.");
                return;
            }

            if (Activator.CreateInstance(type) is not DataType instance)
                return;

            instance.ReadAll(reader);
            data = instance;
        }
        catch (Exception e)
        {
            CelesteNetCompatBackend.WarnOnce($"recv:{typeID}:unreadable",
                $"Failed to read {senderPlayerID}'s packet: {e}");
            return;
        }

        client.Data.Handle(con, data);
    }

    /// <summary>
    /// Stamps the local player into the packet's player meta types, which is what the original
    /// server does for every packet it forwards (<c>CelesteNetPlayerSession.Filter</c>).
    /// <para>
    /// The shim has no server, so the sending client has to do it. Mods rely on this meta to
    /// learn who sent a packet and CoopHelper falls back to the <b>local</b> player when it is
    /// missing, which makes every packet look self-sent.
    /// </para>
    /// </summary>
    private static void StampSender(CelesteNetClient client, DataType data)
    {
        DataPlayerInfo? self = client.PlayerInfo;
        if (self is null)
            return;

        data.Meta = data.GenerateMeta(client.Data);

        bool fixup = false;
        if (data.TryGet(client.Data, out MetaPlayerUpdate? update))
        {
            update.Player = self;
            fixup = true;
        }
        if (data.TryGet(client.Data, out MetaPlayerPrivateState? privateState))
        {
            privateState.Player = self;
            fixup = true;
        }
        if (data.TryGet(client.Data, out MetaPlayerPublicState? publicState))
        {
            publicState.Player = self;
            fixup = true;
        }
        if (data.TryGet(client.Data, out MetaBoundRef? boundRef) && boundRef.TypeBoundTo == DataPlayerInfo.DataID)
        {
            boundRef.ID = self.ID;
            fixup = true;
        }

        // Mirrors the server: propagate the stamped meta into the packet's own fields, so a
        // GenerateMeta which builds its meta from those fields (CoopHelper does exactly that)
        // produces the same thing again when WriteAll regenerates the meta below.
        if (fixup)
            data.FixupMeta(client.Data);
    }
}
