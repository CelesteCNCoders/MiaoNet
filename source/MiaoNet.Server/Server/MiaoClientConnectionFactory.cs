namespace MiaoNet.Server;

public delegate MiaoClientConnection MiaoClientConnectionFactory(
    INetworkConnection networkConnection,
    ServerPlayer serverPlayer,
    MiaoServerService miaoServerService
);