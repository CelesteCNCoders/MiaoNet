namespace Celeste.Mod.CelesteNet.DataTypes;

public class DataChannelList : DataType<DataChannelList>
{
    static DataChannelList() => DataID = "channelList";
    public Channel[] List = [];

    public sealed class Channel
    {
        public string Name = "";
        public uint ID;
        public uint[] Players = [];
    }
}
