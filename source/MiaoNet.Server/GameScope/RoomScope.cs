namespace MiaoNet.Server.GameScope;

public sealed class RoomScope: Scope<string, NoKey>
{
    public string RoomId { get; }

    public override bool Permanent => false;

    public RoomScope(string roomId, MapScope parent) : base(roomId, parent)
    {
        RoomId = roomId;
        parent.AddChild(roomId, this);
    }

    public override string ToString() => $"Room({RoomId})";
}