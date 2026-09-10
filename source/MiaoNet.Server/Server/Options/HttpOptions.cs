namespace MiaoNet.Server;

public sealed class HttpOptions
{
    public string ListenerPrefix { get; set; } = "http://localhost:21474/";

    public string ApiToken { get; set; } = string.Empty;

    public AdminPanelOptions AdminPanel { get; set; } = new();
}
