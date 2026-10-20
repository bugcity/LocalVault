namespace LocalVault.UI;

public sealed class PasswordDialog : Form
{
    private readonly TextBox _password = new() { UseSystemPasswordChar = true, Width = 300 };
    private readonly TextBox? _confirmation;

    public string Password => _password.Text;

    private PasswordDialog(bool setup)
    {
        AppBranding.Apply(this);
        Text = setup ? "LocalVault - 初期設定" : "LocalVault - ロック解除";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(18);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            RowCount = setup ? 4 : 3,
            Dock = DockStyle.Fill
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label { Text = "マスターパスワード", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(_password, 1, 0);

        var nextRow = 1;
        if (setup)
        {
            _confirmation = new TextBox { UseSystemPasswordChar = true, Width = 300 };
            layout.Controls.Add(new Label { Text = "確認", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            layout.Controls.Add(_confirmation, 1, 1);
            nextRow = 2;

            layout.Controls.Add(new Label
            {
                Text = "Argon2id: 64 MiB / 3 iterations。マスターパスワードは復旧できません。",
                AutoSize = true,
                MaximumSize = new Size(380, 0)
            }, 0, nextRow);
            layout.SetColumnSpan(layout.GetControlFromPosition(0, nextRow)!, 2);
            nextRow++;
        }

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill
        };
        var ok = new Button { Text = setup ? "作成" : "解除", DialogResult = DialogResult.None, AutoSize = true };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true };
        ok.Click += (_, _) => ValidateAndClose(setup);
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, nextRow);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public static PasswordDialog CreateSetupDialog() => new(true);
    public static PasswordDialog CreateUnlockDialog() => new(false);

    private void ValidateAndClose(bool setup)
    {
        if (string.IsNullOrEmpty(_password.Text))
        {
            MessageBox.Show("マスターパスワードを入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (setup)
        {
            if (_password.Text.Length < 12)
            {
                MessageBox.Show("マスターパスワードは12文字以上にしてください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_confirmation is null || _password.Text != _confirmation.Text)
            {
                MessageBox.Show("確認用パスワードが一致しません。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
