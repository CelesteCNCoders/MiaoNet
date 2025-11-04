using MiaoNet.Shared;

namespace MiaoNet.Shared;

public sealed class PlayerSettings : IRefBinarySerializable<PlayerSettings>
{
    public PlayerSettings()
    {

    }

    public static PlayerSettings Deserialize(ref RefBinaryReader reader)
    {
        throw new NotImplementedException();
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        throw new NotImplementedException();
    }
}