using System.Runtime.InteropServices;
using System.Text.Json;
using LocalVault.Import;
using LocalVault.Models;
using LocalVault.Storage;

namespace LocalVault.UI;

public enum MainFormExitMode
{
    Exit,
    Lock
}

public sealed class MainForm : Form, IMessageFilter
{
    private readonly VaultSession _session;
    private readonly string _backupSettingsPath;
    private BackupSettings _backupSettings;
    private readonly TextBox _search = new() { PlaceholderText = "検索", Dock = DockStyle.Fill };
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        MultiSelect = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoGenerateColumns = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        RowHeadersVisible = false
    };
    private readonly Label _status = new() { AutoSize = true };
    private readonly System.Windows.Forms.Timer _autoLockTimer = new() { Interval = 5_000 };
    private readonly System.Windows.Forms.Timer _clipboardTimer = new() { Interval = 30_000 };
    private DateTime _lastActivityUtc = DateTime.UtcNow;
    private string? _lastClipboardText;

    public MainFormExitMode ExitMode { get; private set; } = MainFormExitMode.Exit;

    public MainForm(VaultSession session, string backupSettingsPath)
    {
        AppBranding.Apply(this);
        _session = session;
        _backupSettingsPath = backupSettingsPath;
        _backupSettings = BackupSettingsStore.Load(_backupSettingsPath);
        Text = "LocalVault";
        Width = 1000;
        Height = 650;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 450);

        BuildUi();
        RefreshGrid();

        _search.TextChanged += (_, _) => RefreshGrid();
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditSelected(); };
        _autoLockTimer.Tick += (_, _) => CheckAutoLock();
        _clipboardTimer.Tick += (_, _) => ClearClipboardIfOwned();
        _autoLockTimer.Start();
        Application.AddMessageFilter(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Application.RemoveMessageFilter(this);
            _autoLockTimer.Dispose();
            _clipboardTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    public bool PreFilterMessage(ref Message m)
    {
        const int WM_KEYDOWN = 0x0100;
        const int WM_LBUTTONDOWN = 0x0201;
        const int WM_RBUTTONDOWN = 0x0204;
        const int WM_MOUSEWHEEL = 0x020A;
        if (m.Msg is WM_KEYDOWN or WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MOUSEWHEEL)
            _lastActivityUtc = DateTime.UtcNow;
        return false;
    }

    private void BuildUi()
    {
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(6),
            WrapContents = false
        };

        toolbar.Controls.Add(MakeButton("新規", NewItem));
        toolbar.Controls.Add(MakeButton("編集", EditSelected));
        toolbar.Controls.Add(MakeButton("削除", DeleteSelected));
        toolbar.Controls.Add(MakeButton("ユーザー名コピー", () => CopySelected(x => x.Username)));
        toolbar.Controls.Add(MakeButton("パスワードコピー", () => CopySelected(x => x.Password)));
        toolbar.Controls.Add(MakeButton("全体設定", ConfigureGeneralSettings));

        var searchPanel = new Panel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(8, 4, 8, 4) };
        searchPanel.Controls.Add(_search);

        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "名前", DataPropertyName = "Name", FillWeight = 280 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ユーザー名", DataPropertyName = "Username", FillWeight = 220 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "種類", DataPropertyName = "Type", FillWeight = 120 });

        var statusPanel = new Panel { Dock = DockStyle.Bottom, Height = 30, Padding = new Padding(8, 6, 8, 4) };
        statusPanel.Controls.Add(_status);

        Controls.Add(_grid);
        Controls.Add(statusPanel);
        Controls.Add(searchPanel);
        Controls.Add(toolbar);
    }

    private static Button MakeButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Margin = new Padding(3) };
        button.Click += (_, _) => action();
        return button;
    }

    private void RefreshGrid()
    {
        var query = _search.Text.Trim();
        var items = _session.Data.Items
            .Where(item => Matches(item, query))
            .OrderByDescending(item => item.Favorite)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _grid.DataSource = items;
        var backupStatus = _backupSettings.Enabled ? "外部Backup: ON" : "外部Backup: OFF";
        var clipboardStatus = _backupSettings.ClipboardAutoClearEnabled
            ? $"Clipboard {_backupSettings.ClipboardClearSeconds}秒"
            : "Clipboard自動消去 OFF";
        _status.Text = $"{items.Count} / {_session.Data.Items.Count} 件  |  自動ロック {_backupSettings.AutoLockMinutes}分  |  {clipboardStatus}  |  {backupStatus}";
    }

    private static bool Matches(VaultItem item, string query)
    {
        if (query.Length == 0) return true;
        return Contains(item.Name, query)
               || Contains(item.Username, query)
               || item.Uris.Any(x => Contains(x, query));
    }

    private static bool Contains(string? value, string query) =>
        value?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true;

    private VaultItem? SelectedItem => _grid.CurrentRow?.DataBoundItem as VaultItem;

    private void NewItem()
    {
        var item = new VaultItem();
        using var dialog = new ItemEditorDialog(
            item,
            "新規アイテム",
            TrySetClipboardWithExpiry,
            allowPasswordEdit: true,
            passwordGeneratorSettings: _backupSettings.PasswordGenerator,
            savePasswordGeneratorSettings: SavePasswordGeneratorSettings,
            clipboardAutoClearEnabled: _backupSettings.ClipboardAutoClearEnabled,
            clipboardClearSeconds: _backupSettings.ClipboardClearSeconds,
            commit: owner =>
            {
                _session.Data.Items.Add(item);
                if (TrySaveVault(owner))
                    return true;
                _session.Data.Items.Remove(item);
                return false;
            });
        ShowEditorWithoutAutoLock(dialog);
    }

    private void EditSelected()
    {
        var item = SelectedItem;
        if (item is null) return;
        using var dialog = new ItemEditorDialog(
            item,
            "アイテム編集",
            TrySetClipboardWithExpiry,
            requestNewPassword: RequestNewPassword,
            clipboardAutoClearEnabled: _backupSettings.ClipboardAutoClearEnabled,
            clipboardClearSeconds: _backupSettings.ClipboardClearSeconds,
            commit: TrySaveVault);
        ShowEditorWithoutAutoLock(dialog);
    }

    // 編集中は更新先サイトでの作業（LocalVault外の操作）が無操作扱いになるため、自動ロックを止める。
    // 編集を終えた時点から無操作時間の測定を再開する。
    private void ShowEditorWithoutAutoLock(Form dialog)
    {
        _autoLockTimer.Stop();
        try
        {
            dialog.ShowDialog(this);
        }
        finally
        {
            _lastActivityUtc = DateTime.UtcNow;
            _autoLockTimer.Start();
        }
    }

    private void DeleteSelected()
    {
        var items = _grid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.DataBoundItem as VaultItem)
            .Where(item => item is not null)
            .Cast<VaultItem>()
            .Distinct()
            .ToList();

        if (items.Count == 0) return;

        var message = items.Count == 1
            ? $"「{items[0].Name}」を削除しますか？"
            : $"選択した{items.Count}件を削除しますか？";

        if (MessageBox.Show(message, "削除確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        foreach (var item in items)
            _session.Data.Items.Remove(item);

        SaveAndRefresh();
    }

    private void ImportBitwarden(IWin32Window owner)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Bitwarden JSON (*.json)|*.json|JSON (*.json)|*.json",
            CheckFileExists = true,
            Multiselect = false,
            Title = "Bitwarden通常JSONを選択"
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;

        try
        {
            var result = BitwardenJsonImporter.Import(dialog.FileName);

            // 再インポートは不可。LocalVault側の更新や履歴を上書きしないよう、取り込み済みの id はスキップする。
            var existingIds = _session.Data.Items
                .Select(x => x.SourceId)
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);
            var newItems = result.Items
                .Where(x => x.SourceId is null || !existingIds.Contains(x.SourceId))
                .ToList();
            var skipped = result.Items.Count - newItems.Count;

            if (newItems.Count > 0)
            {
                _session.Data.Items.AddRange(newItems);
                if (!TrySaveVault(owner))
                {
                    foreach (var item in newItems)
                        _session.Data.Items.Remove(item);
                    RefreshGrid();
                    return;
                }
            }

            MessageBox.Show(
                owner,
                $"インポート完了\n追加: {newItems.Count}件\n取り込み済みのためスキップ: {skipped}件\nフォルダ: {result.FolderCount}件\n\n元のBitwarden JSONは平文なので、安全を確認した上で削除してください。",
                "Bitwardenインポート",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
        {
            MessageBox.Show(ex.Message, "インポート失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }


    // 新しいパスワードを決めるだけで、Vaultへの確定は編集画面の「保存」で行う。
    private string? RequestNewPassword(string title, string currentValue, IWin32Window owner)
    {
        using var dialog = new PasswordUpdateDialog(
            _backupSettings.PasswordGenerator,
            title,
            currentValue,
            TrySetClipboardWithExpiry,
            _backupSettings.ClipboardAutoClearEnabled,
            _backupSettings.ClipboardClearSeconds);
        if (dialog.ShowDialog(owner) != DialogResult.OK)
            return null;

        var newPassword = dialog.GeneratedPassword;
        if (string.IsNullOrEmpty(newPassword))
            return null;

        if (dialog.SaveSettingsAsDefault)
            SavePasswordGeneratorSettings(dialog.Settings);
        return newPassword;
    }

    private bool TrySaveVault(IWin32Window owner)
    {
        try
        {
            SaveAndRefresh();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(
                owner,
                $"Vaultへの保存に失敗しました。編集内容は未保存のままです。\n\n{ex.Message}",
                "保存失敗",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
    }

    private void SavePasswordGeneratorSettings(PasswordGeneratorSettings settings)
    {
        _backupSettings.PasswordGenerator = settings;
        try
        {
            BackupSettingsStore.Save(_backupSettingsPath, _backupSettings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                $"パスワード生成ポリシーの設定保存に失敗しました。\n\n{ex.Message}",
                "設定保存失敗",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void CopySelected(Func<VaultItem, string> selector)
    {
        var item = SelectedItem;
        if (item is null) return;
        var text = selector(item);
        if (string.IsNullOrEmpty(text)) return;

        if (!TrySetClipboardWithExpiry(text))
            MessageBox.Show("クリップボードを使用できませんでした。", "コピー失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private bool TrySetClipboardWithExpiry(string text)
    {
        try
        {
            Clipboard.SetText(text);
            _clipboardTimer.Stop();

            if (_backupSettings.ClipboardAutoClearEnabled)
            {
                _lastClipboardText = text;
                _clipboardTimer.Interval = Math.Clamp(_backupSettings.ClipboardClearSeconds, 1, 3600) * 1000;
                _clipboardTimer.Start();
            }
            else
            {
                _lastClipboardText = null;
            }

            return true;
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    private void ClearClipboardIfOwned()
    {
        _clipboardTimer.Stop();
        if (_lastClipboardText is null) return;
        try
        {
            if (Clipboard.ContainsText() && Clipboard.GetText() == _lastClipboardText)
                Clipboard.Clear();
        }
        catch (ExternalException)
        {
            // 他プロセスがClipboardを保持中なら無理に触らない。
        }
        finally
        {
            _lastClipboardText = null;
        }
    }

    private void SaveAndRefresh()
    {
        _session.Save();
        var backupResult = BackupService.CreateBackup(_session.Path, _backupSettings);
        RefreshGrid();

        if (backupResult.Attempted && !backupResult.Succeeded)
        {
            _status.Text += "  |  Backup失敗";
            MessageBox.Show(
                $"Vault本体の保存には成功しましたが、外部バックアップに失敗しました。\n\n{backupResult.ErrorMessage}",
                "バックアップ失敗",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void ConfigureGeneralSettings()
    {
        using var dialog = new GeneralSettingsDialog(_backupSettings, ImportBitwarden, ConfigureBackup);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _backupSettings.AutoLockMinutes = dialog.AutoLockMinutes;
        _backupSettings.ClipboardAutoClearEnabled = dialog.ClipboardAutoClearEnabled;
        _backupSettings.ClipboardClearSeconds = dialog.ClipboardClearSeconds;

        try
        {
            BackupSettingsStore.Save(_backupSettingsPath, _backupSettings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                $"全体設定を保存できませんでした。\n\n{ex.Message}",
                "設定保存失敗",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            _backupSettings = BackupSettingsStore.Load(_backupSettingsPath);
            RefreshGrid();
            return;
        }

        _lastActivityUtc = DateTime.UtcNow;
        if (!_backupSettings.ClipboardAutoClearEnabled)
        {
            _clipboardTimer.Stop();
            _lastClipboardText = null;
        }

        RefreshGrid();
    }

    private void ConfigureBackup(IWin32Window owner)
    {
        using var dialog = new BackupSettingsDialog(_backupSettings);
        if (dialog.ShowDialog(owner) != DialogResult.OK)
            return;

        _backupSettings = dialog.Settings;
        try
        {
            BackupSettingsStore.Save(_backupSettingsPath, _backupSettings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                $"バックアップ設定を保存できませんでした。\n\n{ex.Message}",
                "設定保存失敗",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            _backupSettings = BackupSettingsStore.Load(_backupSettingsPath);
            RefreshGrid();
            return;
        }

        RefreshGrid();

        if (!_backupSettings.Enabled)
            return;

        var result = BackupService.CreateBackup(_session.Path, _backupSettings);
        if (result.Succeeded)
        {
            MessageBox.Show(
                $"設定を保存し、現在のVaultをバックアップしました。\n\n{result.Path}",
                "バックアップ設定",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        else if (result.Attempted)
        {
            MessageBox.Show(
                $"設定は保存しましたが、バックアップに失敗しました。\n\n{result.ErrorMessage}",
                "バックアップ失敗",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void CheckAutoLock()
    {
        if (DateTime.UtcNow - _lastActivityUtc < TimeSpan.FromMinutes(Math.Clamp(_backupSettings.AutoLockMinutes, 1, 1440))) return;
        LockVault();
    }

    private void LockVault()
    {
        ClearClipboardIfOwned();
        ExitMode = MainFormExitMode.Lock;
        Close();
    }
}
