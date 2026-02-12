using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace Celeste.Mod.MiaoNet;

internal static class TokenDataUtils
{
    private static readonly byte[] salt = "MiaoNet.TokenDataSalt"u8.ToArray();
    private static readonly string envString = $"{Environment.UserName}@{Environment.MachineName}";

    private static Aes GetAes()
    {
        Aes aes = Aes.Create();
        var kd = new Rfc2898DeriveBytes(envString, salt, 12, HashAlgorithmName.SHA256);
        aes.Key = kd.GetBytes(32);
        aes.IV = kd.GetBytes(16);
        return aes;
    }

    public static string Encrypt(byte[] authenticationData)
    {
        using Aes aes = GetAes();

        return Convert.ToBase64String(aes.EncryptCbc(authenticationData, aes.IV));
    }

    public static bool TryDecrypt(string authenticationDataEncrypted, [NotNullWhen(true)] out byte[]? authenticationData)
    {
        using Aes aes = GetAes();

        try
        {
            authenticationData = aes.DecryptCbc(Convert.FromBase64String(authenticationDataEncrypted), aes.IV);
            return true;
        }
        catch (CryptographicException e)
        {
            Logger.Error(LT.MiaoNet, "Error when decrypting:");
            Logger.LogDetailed(e, LT.MiaoNet);
            authenticationData = null;
            return false;
        }
    }
}
