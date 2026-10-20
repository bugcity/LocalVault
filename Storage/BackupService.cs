using System.Text.Json;

namespace LocalVault.Storage;

public sealed class BackupSettings
{
    public bool Enabled { get; set; }
    public string DestinationDirectory { get; set; } = string.Empty;
    public int RetentionCount { get; set; } = 30;
    public int AutoLockMinutes { get; set; } = 5;
    public bool ClipboardAutoClearEnabled { get; set; } = true;
    public int ClipboardClearSeconds { get; set; } = 30;
    public PasswordGeneratorSettings PasswordGenerator { get; set; } = new();
}

public sealed class PasswordGeneratorSettings
{
    public int Length { get; set; } = 20;
    public bool UseUppercase { get; set; } = true;
    public bool UseLowercase { get; set; } = true;
    public bool UseNumbers { get; set; } = true;
    public bool UseSymbols { get; set; } = true;
    public bool ExcludeAmbiguous { get; set; } = true;
    public int MinimumNumbers { get; set; } = 2;
    public int MinimumSymbols { get; set; } = 2;
}

public static class BackupSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static BackupSettings Load(string path)
    {
        if (!File.Exists(path))
            return new BackupSettings();

        try
        {
            return JsonSerializer.Deserialize<BackupSettings>(File.ReadAllText(path), JsonOptions)
                   ?? new BackupSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new BackupSettings();
        }
    }

    public static void Save(string path, BackupSettings settings)
    {
        var directory = Path.GetDirectoryName(path)
                        ?? throw new InvalidOperationException("設定ファイルの保存先が不正です。");
        Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tempPath, path, overwrite: true);
    }
}

public sealed record BackupResult(bool Attempted, bool Succeeded, string? Path, string? ErrorMessage)
{
    public static BackupResult NotConfigured() => new(false, false, null, null);
    public static BackupResult Success(string path) => new(true, true, path, null);
    public static BackupResult Failure(string message) => new(true, false, null, message);
}

public static class BackupService
{
    private const int MaxRetention = 1000;

    public static BackupResult CreateBackup(string vaultPath, BackupSettings settings)
    {
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.DestinationDirectory))
            return BackupResult.NotConfigured();

        try
        {
            if (!File.Exists(vaultPath))
                return BackupResult.Failure("Vaultファイルが存在しません。");

            var destinationDirectory = Path.GetFullPath(settings.DestinationDirectory);
            var vaultDirectory = Path.GetFullPath(Path.GetDirectoryName(vaultPath)
                                ?? throw new InvalidOperationException("Vault保存先が不正です。"));

            if (IsSameOrChildDirectory(destinationDirectory, vaultDirectory))
                return BackupResult.Failure("バックアップ先にはVault本体と同じローカルフォルダを指定できません。");

            Directory.CreateDirectory(destinationDirectory);

            var fileName = $"vault-{DateTime.Now:yyyyMMdd-HHmmss-fffffff}.lvault";
            var destinationPath = Path.Combine(destinationDirectory, fileName);
            var tempPath = destinationPath + ".tmp";

            File.Copy(vaultPath, tempPath, overwrite: false);
            if (new FileInfo(vaultPath).Length != new FileInfo(tempPath).Length)
                throw new IOException("バックアップのファイルサイズ検証に失敗しました。");

            File.Move(tempPath, destinationPath, overwrite: false);

            PruneOldBackups(destinationDirectory, settings.RetentionCount);
            return BackupResult.Success(destinationPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return BackupResult.Failure(ex.Message);
        }
    }

    private static void PruneOldBackups(string directory, int retentionCount)
    {
        var keep = Math.Clamp(retentionCount, 1, MaxRetention);
        var files = Directory
            .EnumerateFiles(directory, "vault-*.lvault", SearchOption.TopDirectoryOnly)
            .OrderByDescending(x => Path.GetFileName(x), StringComparer.Ordinal)
            .Skip(keep)
            .ToList();

        foreach (var file in files)
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // OneDrive同期中などで削除できない場合は次回保存時に再試行する。
            }
            catch (UnauthorizedAccessException)
            {
                // バックアップ作成自体は成功しているため、世代整理失敗だけでは保存を失敗扱いにしない。
            }
        }
    }

    private static bool IsSameOrChildDirectory(string candidate, string parent)
    {
        var relative = Path.GetRelativePath(parent, candidate);
        return relative == "." ||
               (!relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                !string.Equals(relative, "..", StringComparison.Ordinal));
    }
}
