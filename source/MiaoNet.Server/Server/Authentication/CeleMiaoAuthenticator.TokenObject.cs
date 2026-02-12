using System.Diagnostics;
using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed partial class CeleMiaoAuthenticator
{
    public readonly struct TokenObject : IRefBinarySerializable<TokenObject>
    {
        public const int SignatureLength = 32;

        public PlayerInfo PlayerInfo { get; }

        public string AccessToken { get; }

        public string RefreshToken { get; }

        public byte[] Signature { get; }

        public TokenObject(PlayerInfo playerInfo, string accessToken, string refreshToken, byte[] signature)
        {
            Debug.Assert(signature.Length == SignatureLength);

            PlayerInfo = playerInfo;
            AccessToken = accessToken;
            RefreshToken = refreshToken;
            Signature = signature;
        }

        public void Serialize(ref RefBinaryWriter writer)
        {
            writer.Write(PlayerInfo);
            writer.Write(AccessToken);
            writer.Write(RefreshToken);
            writer.WriteSpan(Signature);
        }

        public static TokenObject Deserialize(ref RefBinaryReader reader)
        {
            PlayerInfo playerInfo = reader.Read<PlayerInfo>();
            string accessToken = reader.ReadString();
            string refreshToken = reader.ReadString();
            byte[] signature = reader.ReadSpan(SignatureLength).ToArray();
            return new TokenObject(playerInfo, accessToken, refreshToken, signature);
        }
    }
}
