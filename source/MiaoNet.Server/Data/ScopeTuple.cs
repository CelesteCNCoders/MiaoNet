namespace MiaoNet.Server;

public class ScopeTuple
{
    public ServerChannel? Channel;
    public ServerMap? Map;
    public ServerRoom? Room;

    public ScopeTuple(ServerChannel? channel, ServerMap? map, ServerRoom? room = null)
    {
        this.Channel = channel;
        this.Map = map;
        this.Room = room;
    }
}
