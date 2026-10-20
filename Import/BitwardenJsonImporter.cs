using System.Text.Json;
using LocalVault.Models;

namespace LocalVault.Import;

public sealed record BitwardenImportResult(List<VaultItem> Items, int FolderCount);

public static class BitwardenJsonImporter
{
    public static BitwardenImportResult Import(string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        if (!root.TryGetProperty("items", out var itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Bitwarden JSONの items 配列が見つかりません。");

        var folders = ReadFolders(root);
        var items = new List<VaultItem>();

        foreach (var source in itemsElement.EnumerateArray())
        {
            var item = new VaultItem
            {
                SourceId = GetString(source, "id"),
                Type = DetectType(source),
                Name = GetString(source, "name") ?? "(名称なし)",
                Notes = GetString(source, "notes") ?? string.Empty,
                Favorite = GetBool(source, "favorite"),
                SourcePayloadJson = source.GetRawText()
            };

            var folderId = GetString(source, "folderId");
            if (folderId is not null && folders.TryGetValue(folderId, out var folderName))
                item.Folder = folderName;

            ReadCustomFields(source, item);
            ReadLogin(source, item);
            ReadPasswordHistory(source, item);
            ReadObjectAsFields(source, "card", "Card", item);
            ReadObjectAsFields(source, "identity", "Identity", item);
            ReadObjectAsFields(source, "sshKey", "SSH", item);

            items.Add(item);
        }

        return new BitwardenImportResult(items, folders.Count);
    }

    private static Dictionary<string, string> ReadFolders(JsonElement root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("folders", out var folders) || folders.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var folder in folders.EnumerateArray())
        {
            var id = GetString(folder, "id");
            var name = GetString(folder, "name");
            if (!string.IsNullOrWhiteSpace(id) && name is not null)
                result[id] = name;
        }
        return result;
    }

    private static string DetectType(JsonElement source)
    {
        if (source.TryGetProperty("sshKey", out _)) return "SSH Key";
        if (source.TryGetProperty("login", out _)) return "Login";
        if (source.TryGetProperty("secureNote", out _)) return "Secure Note";
        if (source.TryGetProperty("card", out _)) return "Card";
        if (source.TryGetProperty("identity", out _)) return "Identity";

        if (source.TryGetProperty("type", out var type) && type.TryGetInt32(out var value))
        {
            return value switch
            {
                1 => "Login",
                2 => "Secure Note",
                3 => "Card",
                4 => "Identity",
                5 => "SSH Key",
                _ => $"Unknown ({value})"
            };
        }
        return "Unknown";
    }

    private static void ReadLogin(JsonElement source, VaultItem item)
    {
        if (!source.TryGetProperty("login", out var login) || login.ValueKind != JsonValueKind.Object)
            return;

        item.Username = GetString(login, "username") ?? string.Empty;
        item.Password = GetString(login, "password") ?? string.Empty;
        item.Totp = GetString(login, "totp") ?? string.Empty;

        if (login.TryGetProperty("uris", out var uris) && uris.ValueKind == JsonValueKind.Array)
        {
            foreach (var uri in uris.EnumerateArray())
            {
                var value = GetString(uri, "uri");
                if (!string.IsNullOrWhiteSpace(value))
                    item.Uris.Add(value);
            }
        }
    }


    private static void ReadPasswordHistory(JsonElement source, VaultItem item)
    {
        if (!source.TryGetProperty("passwordHistory", out var history) || history.ValueKind != JsonValueKind.Array)
            return;

        foreach (var entry in history.EnumerateArray())
        {
            var password = GetString(entry, "password");
            if (string.IsNullOrEmpty(password))
                continue;

            DateTimeOffset? recordedAt = null;
            var dateText = GetString(entry, "lastUsedDate") ?? GetString(entry, "date");
            if (dateText is not null && DateTimeOffset.TryParse(dateText, out var parsed))
                recordedAt = parsed.ToUniversalTime();

            item.PasswordHistory.Add(new PasswordHistoryEntry
            {
                Password = password,
                RecordedAtUtc = recordedAt
            });
        }

        item.TrimPasswordHistory();
    }

    private static void ReadCustomFields(JsonElement source, VaultItem item)
    {
        if (!source.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array)
            return;

        foreach (var field in fields.EnumerateArray())
        {
            item.Fields.Add(new VaultField
            {
                Name = GetString(field, "name") ?? string.Empty,
                Value = GetString(field, "value") ?? string.Empty,
                Type = GetFieldType(field)
            });
        }
    }

    private static void ReadObjectAsFields(JsonElement source, string propertyName, string prefix, VaultItem item)
    {
        if (!source.TryGetProperty(propertyName, out var obj) || obj.ValueKind != JsonValueKind.Object)
            return;

        foreach (var property in obj.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;

            var value = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? string.Empty
                : property.Value.GetRawText();

            item.Fields.Add(new VaultField
            {
                Name = $"{prefix}.{property.Name}",
                Value = value,
                Type = 0
            });
        }
    }

    private static int GetFieldType(JsonElement field)
    {
        if (!field.TryGetProperty("type", out var type) || type.ValueKind == JsonValueKind.Null)
            return 0;

        if (type.ValueKind == JsonValueKind.Number && type.TryGetInt32(out var numeric))
            return numeric;

        return int.TryParse(type.ToString(), out var parsed) ? parsed : 0;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
    }

    private static bool GetBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
               && value.ValueKind == JsonValueKind.True;
    }
}
