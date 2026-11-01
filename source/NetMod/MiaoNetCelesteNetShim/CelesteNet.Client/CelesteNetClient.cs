using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.MiaoNetCelesteNetShim;

namespace Celeste.Mod.CelesteNet.Client;

public sealed class CelesteNetClient : IDisposable
{
    // Fields on purpose: the original type exposes these as fields and mods bind by kind.
    public readonly CelesteNetClientSettings Settings;
    public readonly CelesteNetClientOptions Options;
    public readonly DataContext Data;
    public CelesteNetConnection? Con;
    public DataPlayerInfo? PlayerInfo;
    public bool IsAlive { get; private set; }
    public bool IsReady { get; private set; }

    public CelesteNetClient() : this(new(), new()) { }

    public CelesteNetClient(CelesteNetClientSettings settings, CelesteNetClientOptions options)
    {
        Settings = settings;
        Options = options;
        Data = new DataContext();
        EnsureDataTypesScanned();
    }

    /// <summary>
    /// Mods load after the shim, so the data type registry is refreshed whenever the set
    /// of loaded assemblies changed. A data type which is not registered cannot be sent:
    /// the receiver identifies it by its DataID.
    /// </summary>
    public void EnsureDataTypesScanned()
    {
        int count = AppDomain.CurrentDomain.GetAssemblies().Length;
        if (count == scannedAssemblyCount)
            return;

        int previous = Data.IDToDataType.Count;
        scannedAssemblyCount = count;
        Data.RescanAllDataTypes();
        CelesteNetCompatBackend.Diag(
            $"Scanned data types ({count} assemblies): {Data.IDToDataType.Count} registered, " +
            $"{Data.IDToDataType.Count - previous} new. Meta types: " +
            $"{string.Join(", ", Data.IDToMetaType.Keys.Order())}");
    }

    public void Start(CancellationToken token = default)
    {
        if (IsAlive)
            return;

        EnsureDataTypesScanned();

        IsAlive = true;
        IsReady = true;
        Con = new CelesteNetCompatConnection(this, Data, new CelesteNetTCPUDPConnection.Settings());
        CelesteNetCompatBackend.Diag("CelesteNet client started (MiaoNet session is up).");
    }

    /// <summary>
    /// Tears the connection down but keeps the data context and the registered handlers
    /// alive, so a reconnect can reuse them.
    /// </summary>
    internal void Stop()
    {
        IsAlive = false;
        IsReady = false;
        Con?.Dispose();
        Con = null;
    }

    public T Send<T>(T data) where T : DataType<T>
    {
        CelesteNetCompatTransport.Send(this, data, false);
        return data;
    }

    public T SendAndHandle<T>(T data) where T : DataType<T>
    {
        CelesteNetCompatTransport.Send(this, data, true);
        return data;
    }

    public DataType Send(DataType data)
    {
        CelesteNetCompatTransport.Send(this, data, false);
        return data;
    }

    public DataType SendAndHandle(DataType data)
    {
        CelesteNetCompatTransport.Send(this, data, true);
        return data;
    }

    public void Dispose()
    {
        Stop();
        Data.Dispose();
    }

    private int scannedAssemblyCount;
}
