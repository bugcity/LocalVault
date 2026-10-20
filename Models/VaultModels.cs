namespace LocalVault.Models;

public sealed class VaultData
{
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<VaultItem> Items { get; set; } = [];
}

public sealed class VaultItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? SourceId { get; set; }
    public string Type { get; set; } = "Login";
    public string Name { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public bool Favorite { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public List<PasswordHistoryEntry> PasswordHistory { get; set; } = [];
    // TOTPは非対応。インポート値と既存Vaultの値を失わないよう保持だけ行い、画面には表示しない。
    public string Totp { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public List<string> Uris { get; set; } = [];
    public List<VaultField> Fields { get; set; } = [];

    // Bitwarden側で未対応の項目を失わないため、元アイテムを保持する。
    // Vault全体がAES-GCMで暗号化されるため、この値が平文でディスクに出ることはない。
    public string? SourcePayloadJson { get; set; }

    // パスワード履歴は本体パスワードと非表示項目を合わせて最大この件数まで保持する。
    public const int MaxPasswordHistory = 10;

    public void AddPasswordHistory(PasswordHistoryEntry entry)
    {
        PasswordHistory.Add(entry);
        TrimPasswordHistory();
    }

    // 上限を超えた分は古いもの（日時不明を最も古いとみなす）から捨てる。
    public void TrimPasswordHistory()
    {
        if (PasswordHistory.Count <= MaxPasswordHistory)
            return;

        PasswordHistory = PasswordHistory
            .OrderByDescending(x => x.RecordedAtUtc ?? DateTimeOffset.MinValue)
            .Take(MaxPasswordHistory)
            .ToList();
    }
}

public sealed class PasswordHistoryEntry
{
    // 旧パスワードが履歴へ移された日時。既存Vaultとの互換性のためプロパティ名は維持する。
    public DateTimeOffset? RecordedAtUtc { get; set; }

    // 通常のパスワード履歴では null。
    // Type=1（非表示）の追加項目履歴では項目名を保持し、画面では「Name: 値」と表示する。
    public string? FieldName { get; set; }

    // 通常のパスワード履歴で、記録時点のユーザー名。画面では「ユーザー名: 値」と表示する。
    // 既存Vaultやインポートした履歴では null で、その場合はアイテムの現在のユーザー名を使う。
    public string? Username { get; set; }

    // 既存Vaultとの互換性のためプロパティ名 Password は維持する。
    // FieldName が設定されている場合は、その追加項目の旧値を保持する。
    public string Password { get; set; } = string.Empty;
}

public sealed class VaultField
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    // Bitwarden custom field type: 0=Text, 1=Hidden.
    // Unknown future/other numeric values are preserved instead of being normalized away.
    public int Type { get; set; }
}

public sealed class KdfParameters
{
    public string Algorithm { get; set; } = "argon2id";
    public int MemoryKiB { get; set; } = 65_536;
    public int Iterations { get; set; } = 3;
    public int Parallelism { get; set; } = 2;
    public string Salt { get; set; } = string.Empty;
}

public sealed class CipherParameters
{
    public string Algorithm { get; set; } = "aes-256-gcm";
    public string Nonce { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
}

public sealed class VaultEnvelope
{
    public int Version { get; set; } = 1;
    public KdfParameters Kdf { get; set; } = new();
    public CipherParameters Cipher { get; set; } = new();
    public string Ciphertext { get; set; } = string.Empty;
}
