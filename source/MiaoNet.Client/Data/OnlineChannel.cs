namespace Celeste.Mod.MiaoNet;

public sealed class OnlineChannel
{
    public int ID { get; set; }

    public string Name { get; set; }

    public Dictionary<int, OnlinePlayer> Players { get; set; }

    public OnlineChannel(int id, string name)
    {
        ID = id;
        Name = name;
        Players = new();
    }

    public override string ToString()
        => $"C-{Name}:{ID}";
}
