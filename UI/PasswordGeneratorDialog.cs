using LocalVault.Storage;

namespace LocalVault.UI;

public sealed class PasswordGeneratorDialog : PasswordComposerDialog
{
    public PasswordGeneratorDialog(
        PasswordGeneratorSettings current,
        Func<string, bool> copyToClipboard,
        bool clipboardAutoClearEnabled = true,
        int clipboardClearSeconds = 30)
        : base(
            current,
            "パスワード生成",
            "生成結果は自由に編集できます。生成ポリシーは生成時だけ適用され、編集後の値には再適用しません。",
            "生成パスワード",
            "このパスワードを使用",
            string.Empty,
            copyToClipboard,
            clipboardAutoClearEnabled,
            clipboardClearSeconds)
    {
    }
}
