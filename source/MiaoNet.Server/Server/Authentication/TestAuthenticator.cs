using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed class TestAuthenticator : IMiaoAuthenticator
{
    // partially from CelesteNet
    public static readonly IReadOnlyList<string> Prefixes =
    [
        "Dashing", "Jumping", "Super", "Hyper", "Hopping",
        "Spinning", "Crouched", "Blue", "Pink", "Red",
        "Climbing", "Falling", "Dream", "Awake", "Celestial",
        "Subpixel", "Dashless", "Windy", "Pride", "Bouncy",
        "Forsaken", "Neutral", "Core", "Space","Mirror",
        "Golden", "Summit", "Moon", "Other", "Jammy",
        "Rainbow", "Parrot", "Nyan", "Jelly", "Heart",
        "Puffer", "Celeste", "Snip", "Jade", "Temple",
        "Cloud", "Petal", "Celery", "Sap", "Void", "Small"
    ];

    public static readonly IReadOnlyList<string> Names =
    [
        "Madeline", "Badeline", "Maddy", "Baddy", "Strawberry",
        "Granny", "Celia", "Zipper", "Spinner", "Waterbear",
        "Oshiro", "Kevin", "Seeker", "Puffer", "Berry",
        "Snowball", "Cassette", "Theo", "Fish", "Cloud",
        "Bubble", "Booster", "Jelly", "Feather", "Bird",
        "Petal", "Spring", "Jump", "Dash", "Farewell",
        "Maddie", "Baddie", "Jam", "Nyan", "Parrot",
        "Heart", "Rainbow", "Orb", "Mountain", "SD", "Miao"
    ];

    public Task<AuthenticationResult> AuthenticateAsync(byte[] data, AuthenticationType type, CancellationToken token)
    {
        Random r = Random.Shared;
        string name = $"{Prefixes[r.Next(Prefixes.Count)]} {Names[r.Next(Names.Count)]}";
        string prefix = $"{Prefixes[r.Next(Prefixes.Count)]}";
        return Task.FromResult<AuthenticationResult>(new(AuthenticationResultType.Success, new(name, prefix, string.Empty, Color.White), null));
    }
}