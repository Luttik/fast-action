namespace FastAction.Models;

/// <summary>
/// Fixed action grid built as: 3x3 letters, plus number row 1-3, plus 4th column (4,R,F,V).
/// <code>
/// 1 2 3 4
/// Q W E R
/// A S D F
/// Z X C V
/// </code>
/// </summary>
public static class KeyboardLayout
{
    public static readonly string[][] Rows =
    [
        ["1", "2", "3", "4"],
        ["Q", "W", "E", "R"],
        ["A", "S", "D", "F"],
        ["Z", "X", "C", "V"],
    ];

    public static IReadOnlyList<string> AllKeys { get; } =
        Rows.SelectMany(row => row).ToArray();

    public static bool IsValidKey(string key) =>
        AllKeys.Contains(key, StringComparer.OrdinalIgnoreCase);

    public static string NormalizeKey(string key)
    {
        var trimmed = key.Trim();
        if (trimmed.Length == 1 && char.IsDigit(trimmed[0]))
        {
            return trimmed;
        }

        return trimmed.ToUpperInvariant();
    }
}
