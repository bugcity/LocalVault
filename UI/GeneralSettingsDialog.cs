using LocalVault.Storage;

namespace LocalVault.UI;

public sealed class GeneralSettingsDialog : Form
{
    private readonly NumericUpDown _autoLockMinutes = new()
    {
        Minimum = 1,
        Maximum = 1440,
        Width = 90
    };

    private readonly CheckBox _clipboardAutoClear = new()
    {
        Text = "コピーした内容を自動消去する",
        AutoSize = true
    };

    private readonly NumericUpDown _clipboardClearSeconds = new()
    {
        Minimum = 1,
        Maximum = 3600,
        Width = 90
    };

    public int AutoLockMinutes { get; private set; }
    public bool ClipboardAutoClearEnabled { get; private set; }
    public int ClipboardClearSeconds { get; private set; }

    public GeneralSettingsDialog(
        BackupSettings current,
        Action<IWin32Window> importBitwarden,
        Action<IWin32Window> configureBackup)
    {
        AppBranding.Apply(this);
        Text = "全体設定";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(500, 250);

        _autoLockMinutes.Value = Math.Clamp(current.AutoLockMinutes, 1, 1440);
        _clipboardAutoClear.Checked = current.ClipboardAutoClearEnabled;
        _clipboardClearSeconds.Value = Math.Clamp(current.ClipboardClearSeconds, 1, 3600);
        _clipboardClearSeconds.Enabled = _clipboardAutoClear.Checked;
        _clipboardAutoClear.CheckedChanged += (_, _) => _clipboardClearSeconds.Enabled = _clipboardAutoClear.Checked;

        var ok = new Button { Text = "OK", AutoSize = true };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true };
        ok.Click += (_, _) => ApplyAndClose();

        var importButton = new Button { Text = "Bitwarden JSON取込", AutoSize = true };
        importButton.Click += (_, _) => importBitwarden(this);
        var backupButton = new Button { Text = "バックアップ設定", AutoSize = true };
        backupButton.Click += (_, _) => configureBackup(this);

        var toolsPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        toolsPanel.Controls.Add(importButton);
        toolsPanel.Controls.Add(backupButton);

        var clipboardPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        clipboardPanel.Controls.Add(_clipboardClearSeconds);
        clipboardPanel.Controls.Add(new Label { Text = "秒", AutoSize = true, Margin = new Padding(4, 7, 0, 0) });

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 16, 0, 0)
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 2,
            RowCount = 5
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(layout, "ロックまでの時間（分）", _autoLockMinutes, 0);
        layout.Controls.Add(_clipboardAutoClear, 0, 1);
        layout.SetColumnSpan(_clipboardAutoClear, 2);
        AddRow(layout, "Clipboard消去まで", clipboardPanel, 2);
        AddRow(layout, "データ", toolsPanel, 3);
        layout.Controls.Add(buttons, 0, 4);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void ApplyAndClose()
    {
        AutoLockMinutes = (int)_autoLockMinutes.Value;
        ClipboardAutoClearEnabled = _clipboardAutoClear.Checked;
        ClipboardClearSeconds = (int)_clipboardClearSeconds.Value;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static void AddRow(TableLayoutPanel table, string label, Control control, int row)
    {
        table.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 7, 8, 7)
        }, 0, row);
        table.Controls.Add(control, 1, row);
    }
}
