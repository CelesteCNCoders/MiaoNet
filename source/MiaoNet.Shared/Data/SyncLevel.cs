namespace MiaoNet.Shared;

public enum SyncLevel
{
    /// <summary>
    /// <para>Different channel or different map.</para>
    /// <para>Only sync locations.</para>
    /// </summary>
    L0,
    /// <summary>
    /// <para>Same channel, same map, but it's in debug map.</para>
    /// <para>Only sync positions and locations.</para>
    /// </summary>
    L1,
    /// <summary>
    /// <para>Same channel, same map, all in the map.</para>
    /// <para>Sync all the states.</para>
    /// </summary>
    L2
}
