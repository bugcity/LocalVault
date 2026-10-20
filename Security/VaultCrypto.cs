using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Konscious.Security.Cryptography;
using LocalVault.Models;

namespace LocalVault.Security;

internal static class VaultCrypto
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("LocalVault|v1|argon2id|aes-256-gcm");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static KdfParameters CreateKdfParameters()
    {
        return new KdfParameters
        {
            Salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))
        };
    }

    public static byte[] DeriveKey(string password, KdfParameters parameters)
    {
        if (!string.Equals(parameters.Algorithm, "argon2id", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("未対応のKDFです。");

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var salt = Convert.FromBase64String(parameters.Salt);
        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                MemorySize = parameters.MemoryKiB,
                Iterations = parameters.Iterations,
                DegreeOfParallelism = parameters.Parallelism
            };
            return argon2.GetBytes(KeySize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(salt);
        }
    }

    public static VaultEnvelope Encrypt(VaultData data, byte[] key, KdfParameters kdf)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData);

            return new VaultEnvelope
            {
                Version = 1,
                Kdf = kdf,
                Cipher = new CipherParameters
                {
                    Nonce = Convert.ToBase64String(nonce),
                    Tag = Convert.ToBase64String(tag)
                },
                Ciphertext = Convert.ToBase64String(ciphertext)
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
        }
    }

    public static VaultData Decrypt(VaultEnvelope envelope, byte[] key)
    {
        if (envelope.Version != 1)
            throw new InvalidDataException($"未対応のVaultバージョンです: {envelope.Version}");
        if (!string.Equals(envelope.Cipher.Algorithm, "aes-256-gcm", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("未対応の暗号方式です。");

        var nonce = Convert.FromBase64String(envelope.Cipher.Nonce);
        var tag = Convert.FromBase64String(envelope.Cipher.Tag);
        var ciphertext = Convert.FromBase64String(envelope.Ciphertext);
        var plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData);
            return JsonSerializer.Deserialize<VaultData>(plaintext, JsonOptions)
                   ?? throw new InvalidDataException("Vaultデータを読み取れません。");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static string SerializeEnvelope(VaultEnvelope envelope) =>
        JsonSerializer.Serialize(envelope, JsonOptions);

    public static VaultEnvelope DeserializeEnvelope(string json) =>
        JsonSerializer.Deserialize<VaultEnvelope>(json, JsonOptions)
        ?? throw new InvalidDataException("Vaultファイル形式が不正です。");
}
