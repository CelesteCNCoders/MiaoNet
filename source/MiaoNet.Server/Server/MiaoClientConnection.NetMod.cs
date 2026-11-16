using System.Collections.Frozen;
using System.Globalization;
using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed partial class MiaoClientConnection
{
    private FrozenDictionary<NetModInfo, byte> netModIndices = FrozenDictionary<NetModInfo, byte>.Empty;

    public IReadOnlyList<NetModInfo> NetMods { get; private set; } = [];

    internal void SetNetMods(IReadOnlyList<NetModInfo> mods)
    {
        if (mods.Count > NetModInfo.MaxNetMods)
        {
            throw new InvalidDataException(string.Format(
                CultureInfo.InvariantCulture,
                SR.NetModDeclarationTooLong,
                mods.Count,
                NetModInfo.MaxNetMods
            ));
        }

        Dictionary<NetModInfo, byte> indices = new(mods.Count);
        for (int i = 0; i < mods.Count; i++)
        {
            NetModInfo mod = mods[i];
            if (string.IsNullOrEmpty(mod.Name))
            {
                throw new InvalidDataException(string.Format(
                    CultureInfo.InvariantCulture, SR.NetModDeclarationIsUnnamed, i
                ));
            }

            indices.TryAdd(mod, (byte)i);
        }

        NetMods = mods;
        netModIndices = indices.ToFrozenDictionary();
    }

    public bool TryGetNetModID(NetModInfo mod, out byte netModID)
        => netModIndices.TryGetValue(mod, out netModID);
}
