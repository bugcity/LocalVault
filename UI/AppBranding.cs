using System.Reflection;

namespace LocalVault.UI;

internal static class AppBranding
{
    private static readonly Lazy<Icon> AppIcon = new(LoadIcon);

    public static void Apply(Form form)
    {
        form.Icon = AppIcon.Value;
    }

    private static Icon LoadIcon()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("LocalVault.AppIcon.ico");

        if (stream is null)
            return SystemIcons.Shield;

        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
}
