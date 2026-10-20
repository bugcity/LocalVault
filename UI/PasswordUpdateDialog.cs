using LocalVault.Storage;

namespace LocalVault.UI;

public sealed class PasswordUpdateDialog : PasswordComposerDialog
{
    public PasswordUpdateDialog(
        PasswordGeneratorSettings current,
        string itemName,
        string currentPassword,
        Func<string, bool> copyToClipboard,
        bool clipboardAutoClearEnabled,
        int clipboardClearSeconds)
        : base(
            current,
            $"パスワード更新 - {itemName}",
            "現在のパスワードを直接編集するか、『生成』で新しい候補に置き換えられます。\n"
            + "1. 編集または生成 → 2. コピーして更新先へ貼り付け → 3. 更新先で成功を確認 → 4. 『更新成功・確定』\n"
            + "確定した値は編集画面に反映され、編集画面の『保存』でVaultに保存されます。",
            "更新後パスワード",
            "更新成功・確定",
            currentPassword ?? string.Empty,
            copyToClipboard,
            clipboardAutoClearEnabled,
            clipboardClearSeconds)
    {
    }

    protected override bool ConfirmCommit(string value)
    {
        if (string.Equals(value, InitialValue, StringComparison.Ordinal))
        {
            MessageBox.Show(
                "現在のパスワードと同じです。新しいパスワードを入力または生成してください。",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return false;
        }

        return MessageBox.Show(
            "更新先で新しいパスワードによる更新が成功したことを確認しましたか？\n\n確定すると編集画面に反映されます。編集画面で『保存』すると、現在のパスワードは履歴へ移動し、新しいパスワードが現在値になります。",
            "パスワード更新の確定",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question) == DialogResult.Yes;
    }
}
