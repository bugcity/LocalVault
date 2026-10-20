using LocalVault.Storage;

namespace LocalVault.UI;

public sealed class BackupSettingsDialog : Form
{
    private readonly CheckBox _enabled = new()
    {
        Text = "Vault保存成功後にバックアップする",
        AutoSize = true,
        Margin = new Padding(0, 0, 0, 10)
    };

    private readonly TextBox _destination = new()
    {
        Dock = DockStyle.Fill,
        Margin = new Padding(0, 3, 8, 3)
    };

    private readonly NumericUpDown _retention = new()
    {
        Minimum = 1,
        Maximum = 1000,
        Value = 30,
        Width = 90,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 3, 0, 3)
    };

    public BackupSettings Settings { get; }

    public BackupSettingsDialog(BackupSettings current)
    {
        AppBranding.Apply(this);
        Settings = new BackupSettings
        {
            Enabled = current.Enabled,
            DestinationDirectory = current.DestinationDirectory,
            RetentionCount = current.RetentionCount,
            AutoLockMinutes = current.AutoLockMinutes,
            ClipboardAutoClearEnabled = current.ClipboardAutoClearEnabled,
            ClipboardClearSeconds = current.ClipboardClearSeconds,
            PasswordGenerator = new PasswordGeneratorSettings
            {
                Length = current.PasswordGenerator.Length,
                UseUppercase = current.PasswordGenerator.UseUppercase,
                UseLowercase = current.PasswordGenerator.UseLowercase,
                UseNumbers = current.PasswordGenerator.UseNumbers,
                UseSymbols = current.PasswordGenerator.UseSymbols,
                ExcludeAmbiguous = current.PasswordGenerator.ExcludeAmbiguous,
                MinimumNumbers = current.PasswordGenerator.MinimumNumbers,
                MinimumSymbols = current.PasswordGenerator.MinimumSymbols
            }
        };

        Text = "OneDriveバックアップ設定";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(680, 210);
        MinimumSize = Size;

        _enabled.Checked = Settings.Enabled;
        _destination.Text = Settings.DestinationDirectory;
        _retention.Value = Math.Clamp(Settings.RetentionCount, 1, 1000);

        var browse = new Button
        {
            Text = "参照...",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 0, 0)
        };
        browse.Click += (_, _) => BrowseDestination();

        var destinationPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        destinationPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        destinationPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        destinationPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        destinationPanel.Controls.Add(_destination, 0, 0);
        destinationPanel.Controls.Add(browse, 1, 0);

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Margin = new Padding(8, 0, 0, 0)
        };
        var cancel = new Button
        {
            Text = "キャンセル",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            Margin = Padding.Empty
        };
        ok.Click += (_, _) => ApplyAndValidate();

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 16, 0, 0),
            Padding = Padding.Empty
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 2,
            RowCount = 4,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(_enabled, 0, 0);
        layout.SetColumnSpan(_enabled, 2);

        var destinationLabel = new Label
        {
            Text = "バックアップ先",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 7, 8, 7)
        };
        layout.Controls.Add(destinationLabel, 0, 1);
        layout.Controls.Add(destinationPanel, 1, 1);

        var retentionLabel = new Label
        {
            Text = "保持世代数",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 7, 8, 7)
        };
        layout.Controls.Add(retentionLabel, 0, 2);
        layout.Controls.Add(_retention, 1, 2);

        layout.Controls.Add(buttons, 0, 3);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void BrowseDestination()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "OneDrive内のLocalVaultバックアップ先フォルダを選択してください。",
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true,
            InitialDirectory = GetSuggestedOneDriveDirectory()
        };

        if (Directory.Exists(_destination.Text))
            dialog.InitialDirectory = _destination.Text;

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _destination.Text = dialog.SelectedPath;
    }

    private void ApplyAndValidate()
    {
        var destination = _destination.Text.Trim();
        if (_enabled.Checked && string.IsNullOrWhiteSpace(destination))
        {
            MessageBox.Show("バックアップ先を指定してください。", "設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        Settings.Enabled = _enabled.Checked;
        Settings.DestinationDirectory = destination;
        Settings.RetentionCount = (int)_retention.Value;
    }

    private static string GetSuggestedOneDriveDirectory()
    {
        foreach (var variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
                return value;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
