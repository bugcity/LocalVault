using System.Security.Cryptography;
using LocalVault.Storage;

namespace LocalVault.Security;

public static class PasswordGenerator
{
    private const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lowercase = "abcdefghijklmnopqrstuvwxyz";
    private const string Numbers = "0123456789";
    private const string Symbols = "!@#$%^&*()-_=+[]{};:,.?/";
    private const string Ambiguous = "Il1O0o";

    public static string Generate(PasswordGeneratorSettings settings)
    {
        Validate(settings);

        var upper = Filter(Uppercase, settings.ExcludeAmbiguous);
        var lower = Filter(Lowercase, settings.ExcludeAmbiguous);
        var numbers = Filter(Numbers, settings.ExcludeAmbiguous);
        var symbols = Symbols;

        var all = string.Concat(
            settings.UseUppercase ? upper : string.Empty,
            settings.UseLowercase ? lower : string.Empty,
            settings.UseNumbers ? numbers : string.Empty,
            settings.UseSymbols ? symbols : string.Empty);

        var chars = new List<char>(settings.Length);

        // 英字は選択時に最低1文字を保証する。数字・記号は指定した最低数どおり（0なら保証なし）。
        if (settings.UseUppercase) chars.Add(Pick(upper));
        if (settings.UseLowercase) chars.Add(Pick(lower));

        var minimumNumbers = settings.UseNumbers ? settings.MinimumNumbers : 0;
        var minimumSymbols = settings.UseSymbols ? settings.MinimumSymbols : 0;
        for (var i = 0; i < minimumNumbers; i++) chars.Add(Pick(numbers));
        for (var i = 0; i < minimumSymbols; i++) chars.Add(Pick(symbols));

        while (chars.Count < settings.Length)
            chars.Add(Pick(all));

        Shuffle(chars);
        return new string(chars.ToArray());
    }

    public static void Validate(PasswordGeneratorSettings settings)
    {
        if (settings.Length is < 4 or > 256)
            throw new ArgumentException("パスワード長は4～256文字で指定してください。");

        if (!settings.UseUppercase && !settings.UseLowercase && !settings.UseNumbers && !settings.UseSymbols)
            throw new ArgumentException("少なくとも1種類の文字種を選択してください。");

        if (settings.MinimumNumbers < 0 || settings.MinimumSymbols < 0)
            throw new ArgumentException("最低文字数は0以上で指定してください。");

        // 使用しない文字種の最低数は無視する（画面上は入力不可になるだけで値は残るため）。
        var required = (settings.UseUppercase ? 1 : 0)
                     + (settings.UseLowercase ? 1 : 0)
                     + (settings.UseNumbers ? settings.MinimumNumbers : 0)
                     + (settings.UseSymbols ? settings.MinimumSymbols : 0);

        if (required > settings.Length)
            throw new ArgumentException($"必須文字数({required})がパスワード長({settings.Length})を超えています。");
    }

    private static string Filter(string source, bool excludeAmbiguous) =>
        excludeAmbiguous
            ? new string(source.Where(c => !Ambiguous.Contains(c)).ToArray())
            : source;

    private static char Pick(string chars) => chars[RandomNumberGenerator.GetInt32(chars.Length)];

    private static void Shuffle(List<char> chars)
    {
        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
    }
}
