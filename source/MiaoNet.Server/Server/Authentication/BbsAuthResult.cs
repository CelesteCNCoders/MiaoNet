using System.Text.Json.Serialization;

namespace MiaoNet.Server;

public sealed class BbsAuthResult
{
    [JsonPropertyName("id")]
    public int ID { get; set; }

    [JsonPropertyName("username")]
    public required string Username { get; set; }

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }

    [JsonPropertyName("is_email_confirmed")]
    public int IsEmailConfirmed { get; set; }

    [JsonPropertyName("prefix")]
    public string? Prefix { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }
}

public sealed class BbsAuthErrorResult
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}