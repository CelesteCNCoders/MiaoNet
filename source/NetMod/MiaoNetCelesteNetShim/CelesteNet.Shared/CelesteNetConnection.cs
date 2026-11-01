using Celeste.Mod.CelesteNet.DataTypes;

namespace Celeste.Mod.CelesteNet;

public delegate void DataHandler(CelesteNetConnection connection, DataType data);
public delegate void DataHandler<T>(CelesteNetConnection connection, T data) where T : DataType<T>;
public delegate bool DataFilter(CelesteNetConnection connection, DataType data);
public delegate bool DataFilter<T>(CelesteNetConnection connection, T data) where T : DataType<T>;

/// <summary>
/// Mirrors the shape of the original CelesteNet connection: <see cref="Data"/> is a
/// field, <see cref="IsAlive"/> is virtual and the identity members are abstract.
/// Mods are compiled against the original assembly, so member kind (field vs property)
/// must match or the runtime binding fails.
/// </summary>
public abstract class CelesteNetConnection : IDisposable
{
    public readonly DataContext Data;

    private Action<CelesteNetConnection>? onDisconnect;

    public event Action<CelesteNetConnection> OnDisconnect
    {
        add
        {
            onDisconnect += value;
            if (!IsAlive)
                value?.Invoke(this);
        }
        remove => onDisconnect -= value;
    }

    public virtual bool IsAlive { get; protected set; } = true;

    public abstract bool IsConnected { get; }

    public abstract string ID { get; }

    public abstract string UID { get; }

    protected CelesteNetConnection(DataContext data)
    {
        Data = data;
    }

    /// <summary>Routed by the concrete connection into the MiaoNet backend.</summary>
    public virtual void Send(DataType? data)
    {
    }

    protected virtual void Dispose(bool disposing)
    {
        IsAlive = false;
        onDisconnect?.Invoke(this);
    }

    public void Dispose()
    {
        if (!IsAlive)
            return;
        Dispose(true);
    }

    public virtual void DisposeSafe() => Dispose();

    public override string ToString() => $"{GetType()}({ID} [{UID}])";
}

/// <summary>
/// Mods detect the TCP/UDP transport through this type to read
/// <c>ConnectionSettings.MaxPacketSize</c>, so the same type name, nested settings type
/// and field kinds are kept. The shim has no real UDP channel; it only relays over MiaoNet.
/// </summary>
public abstract class CelesteNetTCPUDPConnection : CelesteNetConnection
{
    public class Settings
    {
        /// <summary>
        /// The original server default is 2048 (with a 1024 queue). Mods size their own
        /// fragmentation from this value, so it must stay in that range: a single MiaoNet
        /// scope packet can carry 65535 bytes, so every shimmed chunk fits without splitting.
        /// </summary>
        public int MaxPacketSize = 2048;
        public int MaxQueueSize = 1024;
    }

    public readonly Settings ConnectionSettings;

    protected CelesteNetTCPUDPConnection(DataContext data, Settings settings) : base(data)
    {
        ConnectionSettings = settings;
    }
}
