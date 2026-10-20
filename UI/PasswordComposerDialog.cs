using LocalVault.Security;
using LocalVault.Storage;

namespace LocalVault.UI;

// パスワード生成画面と更新画面の共通部分（ポリシー設定・生成・コピー・確定）。
public abstract class PasswordComposerDialog : Form
{
    private readonly Func<string, bool> _copyToClipboard;
    private readonly bool _clipboardAutoClearEnabled;
    private readonly int _clipboardClearSeconds;
    private readonly NumericUpDown _length = new() { Minimum = 4, Maximum = 256, Width = 80 };
    private readonly CheckBox _uppercase = new() { Text = "英大文字 A-Z", AutoSize = true };
    private readonly CheckBox _lowercase = new() { Text = "英小文字 a-z", AutoSize = true };
    private readonly CheckBox _numbers = new() { Text = "数字 0-9", AutoSize = true };
    private readonly CheckBox _symbols = new() { Text = "記号", AutoSize = true };
    private readonly CheckBox _excludeAmbiguous = new() { Text = "紛らわしい文字を除外 (I/l/1/O/0/o)", AutoSize = true };
    private readonly NumericUpDown _minimumNumbers = new() { Minimum = 0, Maximum = 256, Width = 80 };
    private readonly NumericUpDown _minimumSymbols = new() { Minimum = 0, Maximum = 256, Width = 80 };
    private readonly CheckBox _saveAsDefault = new() { Text = "このポリシーを既定として保存する", AutoSize = true };
    private readonly TextBox _generated = new() { Width = 360, UseSystemPasswordChar = true };
    private readonly CheckBox _reveal = new() { Text = "表示", AutoSize = true };
    private readonly Label _state = new() { AutoSize = true };
    private string? _committedPassword;
    private bool _suppressGeneratedTextChanged;

    public string GeneratedPassword => _committedPassword ?? string.Empty;
    public PasswordGeneratorSettings Settings { get; private set; }
    public bool SaveSettingsAsDefault { get; private set; }

    protected string InitialValue { get; }

    protected PasswordComposerDialog(
        PasswordGeneratorSettings current,
        string title,
        string description,
        string passwordLabel,
        string commitText,
        string initialValue,
        Func<string, bool> copyToClipboard,
        bool clipboardAutoClearEnabled,
        int clipboardClearSeconds)
    {
        AppBranding.Apply(this);
        Settings = Clone(current);
        InitialValue = initialValue;
        _copyToClipboard = copyToClipboard;
        _clipboardAutoClearEnabled = clipboardAutoClearEnabled;
        _clipboardClearSeconds = Math.Clamp(clipboardClearSeconds, 1, 3600);
        Text = title;
        // 既定は 650x520。要素が収まらない環境でだけ広げる。
        Size = new Size(650, 520);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowOnly;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        _length.Value = Math.Clamp(Settings.Length, 4, 256);
        _uppercase.Checked = Settings.UseUppercase;
        _lowercase.Checked = Settings.UseLowercase;
        _numbers.Checked = Settings.UseNumbers;
        _symbols.Checked = Settings.UseSymbols;
        _excludeAmbiguous.Checked = Settings.ExcludeAmbiguous;
        _minimumNumbers.Value = Math.Clamp(Settings.MinimumNumbers, 0, 256);
        _minimumSymbols.Value = Math.Clamp(Settings.MinimumSymbols, 0, 256);

        SetGeneratedText(initialValue);
        _state.Text = string.IsNullOrEmpty(initialValue)
            ? "未入力（入力または生成できます）"
            : "現在のパスワード（編集または生成できます）";

        _reveal.CheckedChanged += (_, _) => _generated.UseSystemPasswordChar = !_reveal.Checked;
        _generated.TextChanged += (_, _) =>
        {
            if (_suppressGeneratedTextChanged) return;
            _state.Text = string.IsNullOrEmpty(_generated.Text) ? "未入力" : "編集中";
        };
        _numbers.CheckedChanged += (_, _) => _minimumNumbers.Enabled = _numbers.Checked;
        _symbols.CheckedChanged += (_, _) => _minimumSymbols.Enabled = _symbols.Checked;
        _minimumNumbers.Enabled = _numbers.Checked;
        _minimumSymbols.Enabled = _symbols.Checked;

        var generate = new Button { Text = "生成", AutoSize = true };
        var copy = new Button { Text = "コピー", AutoSize = true };
        var commit = new Button { Text = commitText, AutoSize = true };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true };

        generate.Click += (_, _) => GeneratePassword();
        copy.Click += (_, _) => CopyPassword();
        commit.Click += (_, _) => Commit();

        var generatedPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        generatedPanel.Controls.Add(_generated);
        generatedPanel.Controls.Add(_reveal);
        generatedPanel.Controls.Add(generate);
        generatedPanel.Controls.Add(copy);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 12, 0, 0)
        };
        buttons.Controls.Add(commit);
        buttons.Controls.Add(cancel);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(12), ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddSpanningRow(layout, new Label
        {
            Text = description,
            AutoSize = true,
            MaximumSize = new Size(600, 0),
            Margin = new Padding(0, 0, 0, 8)
        });
        AddRow(layout, "長さ", _length);
        AddRow(layout, string.Empty, _uppercase);
        AddRow(layout, string.Empty, _lowercase);
        AddRow(layout, string.Empty, _numbers);
        AddRow(layout, string.Empty, _symbols);
        AddRow(layout, string.Empty, _excludeAmbiguous);
        AddRow(layout, "最低数字数", _minimumNumbers);
        AddRow(layout, "最低記号数", _minimumSymbols);
        AddRow(layout, string.Empty, _saveAsDefault);
        AddRow(layout, passwordLabel, generatedPanel);
        AddRow(layout, "状態", _state);
        AddSpanningRow(layout, buttons);

        Controls.Add(layout);
        AcceptButton = commit;
        CancelButton = cancel;
    }

    // 確定前の追加チェック。false を返すと画面に留まる。
    protected virtual bool ConfirmCommit(string value) => true;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK
            && !string.Equals(_generated.Text, InitialValue, StringComparison.Ordinal)
            && MessageBox.Show(
                this,
                "入力または生成したパスワードはまだ確定していません。\n閉じると破棄されます。\n\n閉じますか？",
                "未確定のパスワード",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            e.Cancel = true;
        }

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SetGeneratedText(string.Empty);
        base.OnFormClosed(e);
    }

    private void SetGeneratedText(string text)
    {
        _suppressGeneratedTextChanged = true;
        try
        {
            _generated.Text = text;
        }
        finally
        {
            _suppressGeneratedTextChanged = false;
        }
    }

    private void GeneratePassword()
    {
        if (!TryApplySettings())
            return;

        SetGeneratedText(PasswordGenerator.Generate(Settings));
        _state.Text = "生成済み（必要なら編集できます）";
    }

    private void CopyPassword()
    {
        if (string.IsNullOrEmpty(_generated.Text))
        {
            MessageBox.Show("コピーするパスワードがありません。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_copyToClipboard(_generated.Text))
            _state.Text = _clipboardAutoClearEnabled
                ? $"コピー済み（{_clipboardClearSeconds}秒後にClipboardを消去）"
                : "コピー済み（Clipboard自動消去はOFF）";
        else
            MessageBox.Show("クリップボードを使用できませんでした。", "コピー失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void Commit()
    {
        var value = _generated.Text;
        if (string.IsNullOrEmpty(value))
        {
            MessageBox.Show("確定するパスワードがありません。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // ポリシーは生成時にだけ使う。手入力の値は検証しない。既定として保存する場合だけ検証する。
        if (_saveAsDefault.Checked && !TryApplySettings())
            return;

        if (!ConfirmCommit(value))
            return;

        _committedPassword = value;
        SaveSettingsAsDefault = _saveAsDefault.Checked;
        DialogResult = DialogResult.OK;
        Close();
    }

    private bool TryApplySettings()
    {
        var settings = new PasswordGeneratorSettings
        {
            Length = (int)_length.Value,
            UseUppercase = _uppercase.Checked,
            UseLowercase = _lowercase.Checked,
            UseNumbers = _numbers.Checked,
            UseSymbols = _symbols.Checked,
            ExcludeAmbiguous = _excludeAmbiguous.Checked,
            MinimumNumbers = (int)_minimumNumbers.Value,
            MinimumSymbols = (int)_minimumSymbols.Value
        };

        try
        {
            PasswordGenerator.Validate(settings);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message, "パスワードポリシー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        Settings = settings;
        return true;
    }

    private static PasswordGeneratorSettings Clone(PasswordGeneratorSettings source) => new()
    {
        Length = source.Length,
        UseUppercase = source.UseUppercase,
        UseLowercase = source.UseLowercase,
        UseNumbers = source.UseNumbers,
        UseSymbols = source.UseSymbols,
        ExcludeAmbiguous = source.ExcludeAmbiguous,
        MinimumNumbers = source.MinimumNumbers,
        MinimumSymbols = source.MinimumSymbols
    };

    private static void AddRow(TableLayoutPanel table, string label, Control control)
    {
        var row = table.RowCount++;
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        table.Controls.Add(control, 1, row);
    }

    private static void AddSpanningRow(TableLayoutPanel table, Control control)
    {
        var row = table.RowCount++;
        table.Controls.Add(control, 0, row);
        table.SetColumnSpan(control, 2);
    }
}
