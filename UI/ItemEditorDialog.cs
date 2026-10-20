using System.Diagnostics;
using LocalVault.Models;
using LocalVault.Storage;

namespace LocalVault.UI;

public sealed class ItemEditorDialog : Form
{
    private sealed class FieldRowState
    {
        public bool Revealed { get; set; }
        // 既存アイテムから読み込んだ行の元データ。新規追加行では null。
        public VaultField? Source { get; set; }
    }

    private sealed record FieldTypeOption(int Value, string Label);

    private readonly List<FieldTypeOption> _fieldTypeOptions =
    [
        new(0, "0: 標準"),
        new(1, "1: 非表示")
    ];

    private readonly Func<string, bool> _copyToClipboard;
    // (タイトル, 現在値, owner) → 新しいパスワード。キャンセル時は null。
    private readonly Func<string, string, IWin32Window, string?>? _requestNewPassword;
    private readonly Func<IWin32Window, bool>? _commit;
    private readonly List<VaultField> _deletedSecretFields = [];
    private readonly Label _pendingNotice = new()
    {
        Text = "パスワードの変更はまだ保存されていません。「保存」で確定します。",
        AutoSize = true,
        ForeColor = Color.Firebrick,
        Visible = false
    };
    private readonly bool _clipboardAutoClearEnabled;
    private readonly int _clipboardClearSeconds;
    private PasswordGeneratorSettings? _passwordGeneratorSettings;
    private readonly Action<PasswordGeneratorSettings>? _savePasswordGeneratorSettings;
    private readonly TextBox _name = new() { Width = 360 };
    private readonly ComboBox _type = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly TextBox _username = new() { Width = 360 };
    private readonly TextBox _password = new() { Width = 360, UseSystemPasswordChar = true };
    private readonly DataGridView _uris = new()
    {
        Width = 650,
        Height = 135,
        AllowUserToAddRows = true,
        AllowUserToDeleteRows = true,
        AllowUserToResizeRows = false,
        AutoGenerateColumns = false,
        RowHeadersVisible = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.CellSelect
    };
    private readonly TextBox _notes = new() { Width = 360, Height = 100, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly DataGridView _fields = new()
    {
        Width = 710,
        Height = 190,
        AllowUserToAddRows = true,
        AllowUserToDeleteRows = true,
        AllowUserToResizeRows = false,
        AutoGenerateColumns = false,
        RowHeadersVisible = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.CellSelect
    };
    private readonly CheckBox _favorite = new() { Text = "お気に入り", AutoSize = true };

    public VaultItem Item { get; }

    public ItemEditorDialog(
        VaultItem item,
        string title,
        Func<string, bool> copyToClipboard,
        bool allowPasswordEdit = false,
        PasswordGeneratorSettings? passwordGeneratorSettings = null,
        Action<PasswordGeneratorSettings>? savePasswordGeneratorSettings = null,
        Func<string, string, IWin32Window, string?>? requestNewPassword = null,
        bool clipboardAutoClearEnabled = true,
        int clipboardClearSeconds = 30,
        Func<IWin32Window, bool>? commit = null)
    {
        AppBranding.Apply(this);
        Item = item;
        _copyToClipboard = copyToClipboard;
        _requestNewPassword = requestNewPassword;
        _commit = commit;
        _clipboardAutoClearEnabled = clipboardAutoClearEnabled;
        _clipboardClearSeconds = Math.Clamp(clipboardClearSeconds, 1, 3600);
        _passwordGeneratorSettings = passwordGeneratorSettings;
        _savePasswordGeneratorSettings = savePasswordGeneratorSettings;
        _password.ReadOnly = !allowPasswordEdit;
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14);

        _type.Items.AddRange(["Login", "Secure Note", "Card", "Identity", "SSH Key"]);
        ConfigureUrisGrid();
        ConfigureFieldsGrid();

        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddRow(table, "名前", _name);
        AddRow(table, "種類", _type);
        AddRow(table, "ユーザー名", UsernamePanel());
        AddRow(table, "パスワード", PasswordPanel());
        AddRow(table, "URI", _uris);
        AddRow(table, "メモ", _notes);
        AddRow(table, "追加項目", _fields);
        AddRow(table, string.Empty, _favorite);
        AddRow(table, string.Empty, _pendingNotice);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var save = new Button { Text = "保存", AutoSize = true };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true };
        save.Click += (_, _) => SaveAndClose();
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        AddRow(table, string.Empty, buttons);

        Controls.Add(table);
        LoadItem();
        _fields.UserDeletingRow += (_, e) =>
        {
            if (e.Row is not null && IsLockedSecretRow(e.Row))
                _deletedSecretFields.Add(EnsureFieldRowState(e.Row).Source!);
        };
        _fields.RowsRemoved += (_, _) => UpdatePendingNotice();
        AcceptButton = save;
        CancelButton = cancel;
    }

    private void ConfigureUrisGrid()
    {
        _uris.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "UriValue",
            HeaderText = "URI",
            Width = 560,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _uris.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "OpenUri",
            HeaderText = string.Empty,
            Text = "開く",
            Width = 70,
            UseColumnTextForButtonValue = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        _uris.CellContentClick += Uris_CellContentClick;
    }

    private void Uris_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != 1 || e.RowIndex >= _uris.Rows.Count)
            return;

        var row = _uris.Rows[e.RowIndex];
        if (row.IsNewRow)
            return;

        _uris.EndEdit();
        var value = row.Cells[0].Value?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!System.Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            MessageBox.Show("ブラウザで開けるURIは http:// または https:// で始まる必要があります。", "URIを開けません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"URIを開けませんでした。\n{ex.Message}", "URIを開けません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ConfigureFieldsGrid()
    {
        _fields.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FieldName",
            HeaderText = "Name",
            Width = 150,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _fields.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FieldValue",
            HeaderText = "Value",
            Width = 240,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _fields.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "FieldType",
            HeaderText = "Type",
            Width = 115,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DisplayMember = nameof(FieldTypeOption.Label),
            ValueMember = nameof(FieldTypeOption.Value),
            DataSource = _fieldTypeOptions
        });
        _fields.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "Reveal",
            HeaderText = string.Empty,
            Width = 60,
            UseColumnTextForButtonValue = false,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _fields.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "CopyFieldValue",
            HeaderText = string.Empty,
            Width = 60,
            UseColumnTextForButtonValue = false,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _fields.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "UpdateFieldValue",
            HeaderText = string.Empty,
            Width = 60,
            UseColumnTextForButtonValue = false,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        _fields.CellFormatting += Fields_CellFormatting;
        _fields.CellBeginEdit += Fields_CellBeginEdit;
        _fields.CellContentClick += Fields_CellContentClick;
        _fields.CellValueChanged += Fields_CellValueChanged;
        _fields.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_fields.IsCurrentCellDirty && _fields.CurrentCell?.ColumnIndex == 2)
                _fields.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _fields.DefaultValuesNeeded += (_, e) => e.Row.Cells[2].Value = 0;
        _fields.RowsAdded += (_, e) =>
        {
            for (var i = e.RowIndex; i < e.RowIndex + e.RowCount && i < _fields.Rows.Count; i++)
                EnsureFieldRowState(_fields.Rows[i]);
        };
    }

    private void Fields_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _fields.Rows.Count)
            return;

        var row = _fields.Rows[e.RowIndex];
        if (row.IsNewRow)
            return;

        var type = ReadFieldType(row);
        var state = EnsureFieldRowState(row);

        if (e.ColumnIndex == 1 && type == 1 && !state.Revealed)
        {
            e.Value = string.IsNullOrEmpty(row.Cells[1].Value?.ToString()) ? string.Empty : "••••••••";
            e.FormattingApplied = true;
        }
        else if (e.ColumnIndex == 3)
        {
            e.Value = type == 1 ? (state.Revealed ? "隠す" : "表示") : string.Empty;
            e.FormattingApplied = true;
        }
        else if (e.ColumnIndex == 4)
        {
            e.Value = type == 1 ? "コピー" : string.Empty;
            e.FormattingApplied = true;
        }
        else if (e.ColumnIndex == 5)
        {
            e.Value = IsLockedSecretRow(row) ? "更新" : string.Empty;
            e.FormattingApplied = true;
        }
    }

    private void Fields_CellBeginEdit(object? sender, DataGridViewCellCancelEventArgs e)
    {
        if (e.RowIndex < 0 || (e.ColumnIndex != 1 && e.ColumnIndex != 2))
            return;

        var row = _fields.Rows[e.RowIndex];

        // 既存の非表示項目は「更新」画面経由でのみ変更する。
        if (IsLockedSecretRow(row))
        {
            e.Cancel = true;
            return;
        }

        if (e.ColumnIndex == 1 && ReadFieldType(row) == 1 && !EnsureFieldRowState(row).Revealed)
            e.Cancel = true;
    }

    private void Fields_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 3 || e.ColumnIndex > 5)
            return;

        var row = _fields.Rows[e.RowIndex];
        if (row.IsNewRow || ReadFieldType(row) != 1)
            return;

        _fields.EndEdit();

        if (e.ColumnIndex == 5)
        {
            UpdateFieldPasswordFromEditor(row);
            return;
        }

        if (e.ColumnIndex == 3)
        {
            var state = EnsureFieldRowState(row);
            state.Revealed = !state.Revealed;
            _fields.InvalidateRow(e.RowIndex);
            return;
        }

        CopyText(row.Cells[1].Value?.ToString() ?? string.Empty);
    }

    private void Fields_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != 2 || e.RowIndex >= _fields.Rows.Count)
            return;

        var row = _fields.Rows[e.RowIndex];
        if (row.IsNewRow)
            return;

        if (ReadFieldType(row) == 1)
            EnsureFieldRowState(row).Revealed = false;

        _fields.InvalidateRow(e.RowIndex);
    }

    private void EnsureFieldTypeOption(int type)
    {
        if (_fieldTypeOptions.Any(x => x.Value == type))
            return;

        _fieldTypeOptions.Add(new FieldTypeOption(type, $"{type}: その他"));
        if (_fields.Columns["FieldType"] is DataGridViewComboBoxColumn column)
            column.DataSource = null;
        if (_fields.Columns["FieldType"] is DataGridViewComboBoxColumn refreshedColumn)
        {
            refreshedColumn.DisplayMember = nameof(FieldTypeOption.Label);
            refreshedColumn.ValueMember = nameof(FieldTypeOption.Value);
            refreshedColumn.DataSource = _fieldTypeOptions;
        }
    }

    private static FieldRowState EnsureFieldRowState(DataGridViewRow row)
    {
        if (row.Tag is FieldRowState state)
            return state;

        state = new FieldRowState();
        row.Tag = state;
        return state;
    }

    private bool IsLockedSecretRow(DataGridViewRow row) =>
        _requestNewPassword is not null
        && !row.IsNewRow
        && EnsureFieldRowState(row).Source is { Type: 1 };

    private static int ReadFieldType(DataGridViewRow row)
    {
        var value = row.Cells[2].Value;
        if (value is int numeric)
            return numeric;
        return int.TryParse(value?.ToString(), out var parsed) ? parsed : 0;
    }

    private Control UsernamePanel()
    {
        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        var copy = new Button { Text = "コピー", AutoSize = true };
        copy.Click += (_, _) => CopyText(_username.Text);
        panel.Controls.Add(_username);
        panel.Controls.Add(copy);
        return panel;
    }

    private Control PasswordPanel()
    {
        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        var copy = new Button { Text = "コピー", AutoSize = true };
        copy.Click += (_, _) => CopyText(_password.Text);
        var reveal = new CheckBox { Text = "表示", AutoSize = true };
        reveal.CheckedChanged += (_, _) => _password.UseSystemPasswordChar = !reveal.Checked;
        panel.Controls.Add(_password);

        if (!_password.ReadOnly && _passwordGeneratorSettings is not null)
        {
            var generate = new Button { Text = "生成", AutoSize = true };
            generate.Click += (_, _) => GeneratePasswordForNewItem();
            panel.Controls.Add(generate);
        }

        panel.Controls.Add(copy);
        panel.Controls.Add(reveal);

        if (_requestNewPassword is not null)
        {
            var update = new Button { Text = "パスワード更新", AutoSize = true };
            update.Click += (_, _) => UpdatePasswordFromEditor();
            panel.Controls.Add(update);

            var history = new Button { Text = "変更履歴", AutoSize = true };
            history.Click += (_, _) => ShowPasswordHistory();
            panel.Controls.Add(history);
        }
        return panel;
    }

    private void GeneratePasswordForNewItem()
    {
        if (_passwordGeneratorSettings is null)
            return;

        using var dialog = new PasswordGeneratorDialog(
            _passwordGeneratorSettings,
            _copyToClipboard,
            _clipboardAutoClearEnabled,
            _clipboardClearSeconds);

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _password.Text = dialog.GeneratedPassword;
        _passwordGeneratorSettings = dialog.Settings;
        if (dialog.SaveSettingsAsDefault)
            _savePasswordGeneratorSettings?.Invoke(dialog.Settings);
    }


    private void UpdatePasswordFromEditor()
    {
        if (_requestNewPassword is null)
            return;

        var newPassword = _requestNewPassword(Item.Name, _password.Text, this);
        if (newPassword is null)
            return;

        _password.Text = newPassword;
        UpdatePendingNotice();
    }

    private void UpdateFieldPasswordFromEditor(DataGridViewRow row)
    {
        if (_requestNewPassword is null || !IsLockedSecretRow(row))
            return;

        var state = EnsureFieldRowState(row);
        var current = row.Cells[1].Value?.ToString() ?? string.Empty;
        var newValue = _requestNewPassword($"{Item.Name} / {state.Source!.Name}", current, this);
        if (newValue is null)
            return;

        row.Cells[1].Value = newValue;
        state.Revealed = false;
        _fields.InvalidateRow(row.Index);
        UpdatePendingNotice();
    }

    private IEnumerable<(VaultField Source, string NewValue)> PendingFieldChanges()
    {
        foreach (DataGridViewRow row in _fields.Rows)
        {
            if (!IsLockedSecretRow(row))
                continue;

            var source = EnsureFieldRowState(row).Source!;
            var value = row.Cells[1].Value?.ToString() ?? string.Empty;
            if (!string.Equals(value, source.Value, StringComparison.Ordinal))
                yield return (source, value);
        }
    }

    // 未保存のパスワード変更で、保存時に履歴へ移る旧値。Item はまだ変更前の状態であること。
    private IEnumerable<PasswordHistoryEntry> PendingHistoryEntries(DateTimeOffset? recordedAtUtc)
    {
        if (_requestNewPassword is not null
            && !string.Equals(_password.Text, Item.Password, StringComparison.Ordinal)
            && !string.IsNullOrEmpty(Item.Password))
        {
            yield return new PasswordHistoryEntry
            {
                Username = Item.Username,
                Password = Item.Password,
                RecordedAtUtc = recordedAtUtc
            };
        }

        // 削除した既存の非表示項目も、値を失わないよう履歴へ移す。
        var changedOrDeleted = PendingFieldChanges()
            .Select(x => x.Source)
            .Concat(_deletedSecretFields);
        foreach (var source in changedOrDeleted)
        {
            if (!string.IsNullOrEmpty(source.Value))
            {
                yield return new PasswordHistoryEntry
                {
                    FieldName = source.Name,
                    Password = source.Value,
                    RecordedAtUtc = recordedAtUtc
                };
            }
        }
    }

    private bool HasPendingPasswordChanges() =>
        (_requestNewPassword is not null && !string.Equals(_password.Text, Item.Password, StringComparison.Ordinal))
        || PendingFieldChanges().Any();

    // 新規作成で、パスワードまたは非表示項目の値が入力済みか。
    private bool HasUnsavedNewSecrets()
    {
        if (_requestNewPassword is not null)
            return false;

        if (!_password.ReadOnly && !string.IsNullOrEmpty(_password.Text))
            return true;

        return _fields.Rows
            .Cast<DataGridViewRow>()
            .Any(row => !row.IsNewRow
                        && ReadFieldType(row) == 1
                        && !string.IsNullOrEmpty(row.Cells[1].Value?.ToString()));
    }

    private void UpdatePendingNotice() => _pendingNotice.Visible = HasPendingPasswordChanges();

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK)
        {
            string? message = null;
            if (HasPendingPasswordChanges())
                message = "変更したパスワードがまだ保存されていません。\n保存せずに閉じると、新しいパスワードは失われます。\n\n保存せずに閉じますか？";
            else if (HasUnsavedNewSecrets())
                message = "入力したパスワードはまだ保存されていません。\n保存せずに閉じると、このアイテムは作成されず、パスワードは失われます。\n\n保存せずに閉じますか？";

            if (message is not null
                && MessageBox.Show(
                    this,
                    message,
                    "未保存のパスワード",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                e.Cancel = true;
            }
        }

        base.OnFormClosing(e);
    }

    private void ShowPasswordHistory()
    {
        _fields.EndEdit();
        using var dialog = new PasswordHistoryDialog(Item, _copyToClipboard, PendingHistoryEntries(null).ToList());
        dialog.ShowDialog(this);
    }

    private void CopyText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        if (!_copyToClipboard(text))
            MessageBox.Show("クリップボードを使用できませんでした。", "コピー失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static void AddRow(TableLayoutPanel table, string label, Control control)
    {
        var row = table.RowCount++;
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        table.Controls.Add(control, 1, row);
    }

    private void LoadItem()
    {
        _name.Text = Item.Name;
        _type.Text = Item.Type;
        _username.Text = Item.Username;
        _password.Text = Item.Password;
        foreach (var uri in Item.Uris)
            _uris.Rows.Add(uri);
        _notes.Text = Item.Notes;
        _favorite.Checked = Item.Favorite;

        foreach (var field in Item.Fields)
        {
            EnsureFieldTypeOption(field.Type);
            var index = _fields.Rows.Add(field.Name, field.Value, field.Type, string.Empty);
            var row = _fields.Rows[index];
            var state = EnsureFieldRowState(row);
            state.Revealed = false;
            state.Source = field;
        }
    }

    private void SaveAndClose()
    {
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            MessageBox.Show("名前は必須です。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!_uris.EndEdit() || !_fields.EndEdit())
            return;

        var uris = new List<string>();
        foreach (DataGridViewRow row in _uris.Rows)
        {
            if (row.IsNewRow)
                continue;

            var value = row.Cells[0].Value?.ToString()?.Trim();
            if (!string.IsNullOrEmpty(value))
                uris.Add(value);
        }

        var fields = new List<VaultField>();
        foreach (DataGridViewRow row in _fields.Rows)
        {
            if (row.IsNewRow)
                continue;

            var name = row.Cells[0].Value?.ToString() ?? string.Empty;
            var value = row.Cells[1].Value?.ToString() ?? string.Empty;
            var fieldType = ReadFieldType(row);

            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrEmpty(value) && row.Cells[2].Value is null)
                continue;

            fields.Add(new VaultField
            {
                Name = name,
                Value = value,
                Type = fieldType
            });
        }

        // 保存に失敗したら Item を元に戻し、画面は開いたままにする。
        var snapshot = (Item.Name, Item.Type, Item.Username, Item.Password,
            Item.Uris, Item.Notes, Item.Favorite, Item.Fields, History: Item.PasswordHistory.ToList());

        foreach (var entry in PendingHistoryEntries(DateTimeOffset.UtcNow))
            Item.AddPasswordHistory(entry);

        Item.Name = _name.Text.Trim();
        Item.Type = string.IsNullOrWhiteSpace(_type.Text) ? "Login" : _type.Text.Trim();
        Item.Username = _username.Text;
        Item.Password = _password.Text;
        Item.Uris = uris;
        Item.Notes = _notes.Text;
        Item.Favorite = _favorite.Checked;
        Item.Fields = fields;

        if (_commit is not null && !_commit(this))
        {
            (Item.Name, Item.Type, Item.Username, Item.Password,
                Item.Uris, Item.Notes, Item.Favorite, Item.Fields, Item.PasswordHistory) = snapshot;
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
