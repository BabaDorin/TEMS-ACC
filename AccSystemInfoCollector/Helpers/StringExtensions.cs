namespace TEMS.ACC.Helpers;

public static class StringExtensions
{
    public static string? NullIfEmpty(this string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;

    public static string After(this string s, char separator) =>
        s.Contains(separator) ? s[(s.IndexOf(separator) + 1)..] : s;

    public static List<string> Lines(this string s) =>
        [.. s.Split('\n')];

    public static string? Find(this List<string> lines, params string[] keys) =>
        lines.FirstOrDefault(l => keys.Any(k => l.Contains(k)));

    public static List<string> Blocks(this string s) =>
        [.. s.Split("\n\n").Where(b => !string.IsNullOrWhiteSpace(b))];
}