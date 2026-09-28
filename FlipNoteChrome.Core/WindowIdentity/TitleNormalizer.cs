using System.Text.RegularExpressions;

namespace FlipNoteChrome.Core.WindowIdentity;

public static class TitleNormalizer
{
    // Strip common Chrome suffixes and doc noise
    private static readonly Regex ChromeSuffix = new(@"(\s[-–—]\sGoogle Chrome|\s-\sChrome)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MultiSpace = new(@"\s{2,}", RegexOptions.Compiled);

    public static string Normalize(string title, string className, string exePath)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        var t = title.Trim();
        // Chrome specific: titles like "GitHub - Google Chrome"
        t = ChromeSuffix.Replace(t, "").Trim();
        // Strip leading glyphs like "● " or "• "
        t = t.TrimStart('●', '•', '*', ' ', '\uFEFF');
        // Collapse whitespace
        t = MultiSpace.Replace(t, " ");
        // Truncate very long titles to 220 chars for stable hash
        if (t.Length > 220) t = t[..220];
        return t.ToLowerInvariant();
    }

    public static string Hash(string input)
    {
        if (string.IsNullOrEmpty(input)) return "empty";
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
