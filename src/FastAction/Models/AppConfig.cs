namespace FastAction.Models;

public sealed class AppConfig
{
    public HotkeyConfig Hotkey { get; set; } = new();

    public string RootGridId { get; set; } = "home";

    /// <summary>When true, right-click a tile to edit or clear its action.</summary>
    public bool EditOnRightClick { get; set; } = true;

    /// <summary>When true, register the app to launch automatically at Windows sign-in.</summary>
    public bool RunOnStartup { get; set; } = true;

    public List<GridConfig> Grids { get; set; } = [];
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
