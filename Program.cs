using LocalVault.Storage;
using LocalVault.UI;

namespace LocalVault;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var appDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LocalVault");
        Directory.CreateDirectory(appDir);
        var vaultPath = Path.Combine(appDir, "vault.lvault");
        var backupSettingsPath = Path.Combine(appDir, "settings.json");

        while (true)
        {
            VaultSession? session = null;

            if (File.Exists(vaultPath))
            {
                using var unlock = PasswordDialog.CreateUnlockDialog();
                if (unlock.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    session = VaultStore.Open(vaultPath, unlock.Password);
                }
                catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or InvalidDataException)
                {
                    MessageBox.Show(
                        "マスターパスワードが違うか、Vaultファイルが破損しています。",
                        "Vaultを開けません",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    continue;
                }
            }
            else
            {
                using var setup = PasswordDialog.CreateSetupDialog();
                if (setup.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                session = VaultStore.Create(vaultPath, setup.Password);
            }

            using (session)
            using (var main = new MainForm(session, backupSettingsPath))
            {
                main.ShowDialog();
                if (main.ExitMode == MainFormExitMode.Exit)
                {
                    return;
                }
            }
        }
    }
}
