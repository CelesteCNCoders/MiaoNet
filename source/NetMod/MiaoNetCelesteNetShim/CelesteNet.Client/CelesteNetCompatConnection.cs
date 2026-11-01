using Celeste.Mod.CelesteNet.DataTypes;

namespace Celeste.Mod.CelesteNet.Client;

/// <summary>
/// The single concrete transport of the shim. It is a <see cref="CelesteNetTCPUDPConnection"/>
/// because mods probe that type to read the packet size limits, but all traffic is
/// relayed through the shim's MiaoNet net-mod instead of a real TCP/UDP socket.
/// </summary>
internal sealed class CelesteNetCompatConnection : CelesteNetTCPUDPConnection
{
    private const string ConnectionID = "MiaoNet";
    private const string ConnectionUID = "con-miaonet-celestenet-shim";

    private readonly CelesteNetClient client;

    public CelesteNetCompatConnection(CelesteNetClient client, DataContext data, Settings settings)
        : base(data, settings)
    {
        this.client = client;
    }

    public override bool IsConnected => IsAlive;

    public override string ID => ConnectionID;

    public override string UID => ConnectionUID;

    public override void Send(DataType? data)
    {
        if (data is null)
            return;
        CelesteNetCompatTransport.Send(client, data, handleLocally: false);
    }
}
