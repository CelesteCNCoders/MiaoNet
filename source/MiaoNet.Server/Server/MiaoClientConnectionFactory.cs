namespace MiaoNet.Server;

public delegate MiaoClientConnection MiaoClientConnectionFactory(
    int id,
    INetworkConnection networkConnection,
    ServerPlayer serverPlayer,
    MiaoServerService miaoServerService
);