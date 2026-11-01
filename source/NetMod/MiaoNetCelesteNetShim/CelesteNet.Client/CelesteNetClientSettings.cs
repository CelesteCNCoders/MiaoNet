namespace Celeste.Mod.CelesteNet.Client;

public sealed class CelesteNetClientSettings
{
    public bool Interactions { get; set; } = true;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 21473;
}
