namespace FastAction.Models;

public sealed class LucideSwatch
{
    public required string Id { get; init; }

    public required string Label { get; init; }

    /// <summary>Empty for <see cref="LucidePalette.Auto"/> (theme-dependent).</summary>
    public required string Hex { get; init; }
}

/// <summary>Small palette for tinting Lucide stroke icons.</summary>
public static class LucidePalette
{
    public const string Auto = "auto";

    public static readonly LucideSwatch[] Swatches =
    [
        new() { Id = Auto, Label = "Auto", Hex = "" },
        new() { Id = "white", Label = "White", Hex = "#F5F5F5" },
        new() { Id = "slate", Label = "Slate", Hex = "#9AA4B2" },
        new() { Id = "blue", Label = "Blue", Hex = "#60CDFF" },
        new() { Id = "green", Label = "Green", Hex = "#6CCB5F" },
        new() { Id = "amber", Label = "Amber", Hex = "#F8D347" },
        new() { Id = "orange", Label = "Orange", Hex = "#FF8C42" },
        new() { Id = "pink", Label = "Pink", Hex = "#FF7AA2" },
        new() { Id = "purple", Label = "Purple", Hex = "#B4A0FF" },
        new() { Id = "red", Label = "Red", Hex = "#FF6B6B" },
    ];

    public static string Normalize(string? id)
    {
        var trimmed = (id ?? Auto).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Auto;
        }

        return Swatches.Any(s => s.Id == trimmed) ? trimmed : Auto;
    }

    public static string ResolveHex(string? id, bool darkTheme)
    {
        var normalized = Normalize(id);
        if (normalized == Auto)
        {
            return darkTheme ? "#F2F2F2" : "#2B2B2B";
        }

        var swatch = Swatches.First(s => s.Id == normalized);
        return swatch.Hex;
    }

    public static bool TryParseRgb(string hex, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        var value = (hex ?? string.Empty).Trim().TrimStart('#');
        if (value.Length != 6)
        {
            return false;
        }

        try
        {
            r = Convert.ToByte(value[..2], 16);
            g = Convert.ToByte(value[2..4], 16);
            b = Convert.ToByte(value[4..6], 16);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
