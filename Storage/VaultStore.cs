using System.Security.Cryptography;
using LocalVault.Models;
using LocalVault.Security;

namespace LocalVault.Storage;

public sealed class VaultSession : IDisposable
{
    private byte[]? _key;

    internal VaultSession(string path, byte[] key, KdfParameters kdf, VaultData data)
    {
        Path = path;
        _key = key;
        Kdf = kdf;
        Data = data;
    }

    public string Path { get; }
    public VaultData Data { get; }
    internal KdfParameters Kdf { get; }

    public void Save()
    {
        if (_key is null)
            throw new ObjectDisposedException(nameof(VaultSession));

        Data.UpdatedAtUtc = DateTimeOffset.UtcNow;
        VaultStore.Save(Path, _key, Kdf, Data);
    }

    public void Dispose()
    {
        if (_key is not null)
        {
            CryptographicOperations.ZeroMemory(_key);
            _key = null;
        }

        // managed stringの実体そのものは確実にはゼロ化できないが、参照を早期に外す。
        foreach (var item in Data.Items)
        {
            item.Password = string.Empty;
            item.Totp = string.Empty;
            item.Username = string.Empty;
            item.Notes = string.Empty;
            item.SourcePayloadJson = null;
            foreach (var field in item.Fields)
                field.Value = string.Empty;
            foreach (var history in item.PasswordHistory)
            {
                history.Password = string.Empty;
                history.Username = null;
            }
            item.PasswordHistory.Clear();
        }
        Data.Items.Clear();
    }
}

public static class VaultStore
{
    public static VaultSession Create(string path, string password)
    {
        var kdf = VaultCrypto.CreateKdfParameters();
        var key = VaultCrypto.DeriveKey(password, kdf);
        var data = new VaultData();
        Save(path, key, kdf, data);
        return new VaultSession(path, key, kdf, data);
    }

    public static VaultSession Open(string path, string password)
    {
        var envelope = VaultCrypto.DeserializeEnvelope(File.ReadAllText(path));
        var key = VaultCrypto.DeriveKey(password, envelope.Kdf);
        try
        {
            var data = VaultCrypto.Decrypt(envelope, key);
            return new VaultSession(path, key, envelope.Kdf, data);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            throw;
        }
    }

    internal static void Save(string path, byte[] key, KdfParameters kdf, VaultData data)
    {
        var envelope = VaultCrypto.Encrypt(data, key, kdf);
        var json = VaultCrypto.SerializeEnvelope(envelope);
        var directory = System.IO.Path.GetDirectoryName(path)
                        ?? throw new InvalidOperationException("Vault保存先が不正です。");
        Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, json);

        if (File.Exists(path))
        {
            try
            {
                // 本体は一時ファイルから原子的に置換する。
                // 同一PC内の世代バックアップは作らず、外部バックアップは保存完了後に行う。
                File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            catch (PlatformNotSupportedException)
            {
                File.Move(tempPath, path, overwrite: true);
            }
        }
        else
        {
            File.Move(tempPath, path);
        }
    }
}
