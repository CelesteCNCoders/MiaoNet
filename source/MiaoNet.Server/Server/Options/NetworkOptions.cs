using System.ComponentModel.DataAnnotations;

namespace MiaoNet.Server;

public sealed class NetworkOptions
{
    public required string ListenEndPoint { get; set; }
}
