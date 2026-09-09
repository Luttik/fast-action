namespace FastAction.Models;

public sealed class AppConfig
{
    public HotkeyConfig Hotkey { get; set; } = new();

    public string RootGridId { get; set; } = "home";

    /// <summary>When true, right-click a tile to edit or clear its action.</summary>
    public bool EditOnRightClick { get; set; } = true;

    /// <summary>When true, register the app to launch automatically at Windows sign-in.</summary>
    public bool RunOnStartup { get; set; } = true;

    public LayoutConfig Layout { get; set; } = new();

    public AppearanceConfig Appearance { get; set; } = new();

    public List<GridConfig> Grids { get; set; } = [];
}

public sealed class LayoutConfig
{
    /// <summary>Physical key at the top-left of the overlay grid (e.g. 1, Q, A).</summary>
    public string StartKey { get; set; } = "1";

    public int Columns { get; set; } = 4;

    public int Rows { get; set; } = 4;
}

public sealed class AppearanceConfig
{
    public const int CompactTileSize = 72;
    public const int DefaultTileSize = 88;
    public const int LargeTileSize = 112;

    public const int SharpCornerRadius = 4;
    public const int RoundedCornerRadius = 12;
    public const int PillCornerRadius = 22;

    /// <summary>system | light | dark</summary>
    public string Theme { get; set; } = "system";

    public int TileSize { get; set; } = DefaultTileSize;

    public int CornerRadius { get; set; } = RoundedCornerRadius;

    /// <summary>When true, use Desktop Acrylic (Windows Terminal-style frosted glass).</summary>
    public bool Acrylic { get; set; } = true;

    /// <summary>Background opacity 20–100, like Windows Terminal. Higher is more solid.</summary>
    public int Opacity { get; set; } = 80;

    /// <summary>standard | soft (thin acrylic).</summary>
    public string AcrylicBlur { get; set; } = "standard";

    public static string NormalizeTheme(string? theme) =>
        (theme ?? "system").Trim().ToLowerInvariant() switch
        {
            "light" => "light",
            "dark" => "dark",
            _ => "system",
        };

    public static int NormalizeTileSize(int tileSize) =>
        tileSize switch
        {
            CompactTileSize => CompactTileSize,
            LargeTileSize => LargeTileSize,
            _ => DefaultTileSize,
        };

    public static int NormalizeCornerRadius(int cornerRadius) =>
        cornerRadius switch
        {
            SharpCornerRadius => SharpCornerRadius,
            PillCornerRadius => PillCornerRadius,
            _ => RoundedCornerRadius,
        };

    public static int NormalizeOpacity(int opacity) => Math.Clamp(opacity, 20, 100);

    public static string NormalizeAcrylicBlur(string? blur) =>
        (blur ?? "standard").Trim().ToLowerInvariant() == "soft" ? "soft" : "standard";
}

public sealed class HotkeyConfig
{
    public List<string> Modifiers { get; set; } = ["Win", "Shift"];

    public string Key { get; set; } = "Space";
}

public sealed class GridConfig
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public List<ActionItemConfig> Items { get; set; } = [];
}

public sealed class ActionItemConfig
{
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public IconConfig Icon { get; set; } = new();

    public ActionConfig Action { get; set; } = new();
}

public sealed class IconConfig
{
    /// <summary>app | lucide | svg</summary>
    public string Type { get; set; } = "lucide";

    public string? Path { get; set; }

    public string? Name { get; set; }

    /// <summary>Lucide palette id (auto, blue, green, …). Ignored for app/svg icons.</summary>
    public string? Color { get; set; }
}

public sealed class ActionConfig
{
    /// <summary>shell | grid | hotkey</summary>
    public string Type { get; set; } = "shell";

    public string? Command { get; set; }

    public List<string>? Args { get; set; }

    public string? WorkingDirectory { get; set; }

    public string? GridId { get; set; }

    /// <summary>For type hotkey: Alt | Ctrl | Shift | Win.</summary>
    public List<string>? Modifiers { get; set; }

    /// <summary>For type hotkey: same key vocabulary as the app hotkey (e.g. S, Space, F5).</summary>
    public string? Key { get; set; }
}
