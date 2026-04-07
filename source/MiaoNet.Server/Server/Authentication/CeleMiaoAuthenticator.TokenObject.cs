using System.Diagnostics;
using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed partial class CeleMiaoAuthenticator
{
    public readonly struct TokenObject : IRefBinarySerializable<TokenObject>
    {
        public const int SignatureLength = 32;

        public string AccessToken { get; }

        public string RefreshToken { get; }

        public DateTime ExpiredDateTime { get; }

        public TokenObject(string accessToken, string refreshToken, DateTime expiredOn)
        {
            AccessToken = accessToken;
            RefreshToken = refreshToken;
            ExpiredDateTime = expiredOn;
        }

        public void Serialize(ref RefBinaryWriter writer)
        {
            writer.Write(AccessToken);
            writer.Write(RefreshToken);
            writer.Write(ExpiredDateTime);
        }

        public static TokenObject Deserialize(ref RefBinaryReader reader)
        {
            string accessToken = reader.ReadString();
            string refreshToken = reader.ReadString();
            DateTime expiredOn = reader.ReadDateTime();
            return new TokenObject(accessToken, refreshToken, expiredOn);
        }
    }
}
