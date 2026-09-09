using System.Diagnostics;
using FastAction.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FastAction.Services;

public sealed class ConfigService : IDisposable
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    private FileSystemWatcher? _watcher;
    private readonly object _lock = new();
    private AppConfig _config = new();
    private DateTime _ignoreWatcherUntilUtc;

    public string ConfigDirectory { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FastAction");

    public string ConfigPath => Path.Combine(ConfigDirectory, "config.yaml");

    public AppConfig Config
    {
        get
        {
            lock (_lock)
            {
                return _config;
            }
        }
    }

    public event EventHandler? ConfigChanged;

    public void Initialize()
    {
        Directory.CreateDirectory(ConfigDirectory);
        EnsureDefaultConfig();
        try
        {
            Reload();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Initial config load failed, using shipped example: {ex.Message}");
            lock (_lock)
            {
                _config = LoadShippedExample();
            }
        }

        StartWatching();
    }

    public void Reload()
    {
        try
        {
            var yaml = File.ReadAllText(ConfigPath);
            var loaded = _deserializer.Deserialize<AppConfig>(yaml) ?? new AppConfig();
            Validate(loaded);

            lock (_lock)
            {
                _config = loaded;
            }

            ConfigChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load config: {ex.Message}");
            throw;
        }
    }

    public GridConfig? GetGrid(string id)
    {
        lock (_lock)
        {
            return _config.Grids.FirstOrDefault(g =>
                string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Returns an existing grid, or creates and persists an empty one so it can be edited in the UI.
    /// </summary>
    public GridConfig GetOrCreateGrid(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Grid id is required.", nameof(id));
        }

        id = id.Trim();
        var created = false;
        GridConfig grid;
        lock (_lock)
        {
            var existing = _config.Grids.FirstOrDefault(g =>
                string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return existing;
            }

            grid = new GridConfig
            {
                Id = id,
                Title = id,
                Items = [],
            };
            _config.Grids.Add(grid);
            Validate(_config);
            SaveUnlocked();
            created = true;
        }

        if (created)
        {
            ConfigChanged?.Invoke(this, EventArgs.Empty);
        }

        return grid;
    }

    public GridConfig? GetRootGrid()
    {
        lock (_lock)
        {
            return GetGrid(_config.RootGridId)
                ?? _config.Grids.FirstOrDefault();
        }
    }

    public void OpenConfigFolder()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = ConfigDirectory,
            UseShellExecute = true,
        });
    }

    public void UpsertItem(string gridId, ActionItemConfig item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Key = KeyboardLayout.NormalizeKey(item.Key);

        lock (_lock)
        {
            var layout = KeyboardLayout.From(_config.Layout);
            if (!layout.IsValidKey(item.Key))
            {
                throw new InvalidOperationException($"Unsupported key '{item.Key}'.");
            }

            var grid = _config.Grids.FirstOrDefault(g =>
                string.Equals(g.Id, gridId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Grid '{gridId}' was not found.");

            grid.Items ??= [];
            var existing = grid.Items.FindIndex(i =>
                string.Equals(i.Key, item.Key, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                grid.Items[existing] = item;
            }
            else
            {
                grid.Items.Add(item);
            }

            Validate(_config);
            SaveUnlocked();
        }

        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetRunOnStartup(bool enabled)
    {
        lock (_lock)
        {
            _config.RunOnStartup = enabled;
            SaveUnlocked();
        }

        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    public KeyboardLayout GetLayout()
    {
        lock (_lock)
        {
            return KeyboardLayout.From(_config.Layout);
        }
    }

    public void UpdateLayout(string startKey, int columns, int rows)
    {
        var built = KeyboardLayout.Build(startKey, columns, rows);
        lock (_lock)
        {
            _config.Layout ??= new LayoutConfig();
            _config.Layout.StartKey = built.StartKey;
            _config.Layout.Columns = built.RequestedColumns;
            _config.Layout.Rows = built.RequestedRows;
            SaveUnlocked();
        }

        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateAppearance(
        string? theme = null,
        int? tileSize = null,
        int? cornerRadius = null,
        bool? acrylic = null,
        int? opacity = null,
        string? acrylicBlur = null,
        string? lucideColor = null)
    {
        lock (_lock)
        {
            _config.Appearance ??= new AppearanceConfig();
            if (theme is not null)
            {
                _config.Appearance.Theme = AppearanceConfig.NormalizeTheme(theme);
            }

            if (tileSize is not null)
            {
                _config.Appearance.TileSize = AppearanceConfig.NormalizeTileSize(tileSize.Value);
            }

            if (cornerRadius is not null)
            {
                _config.Appearance.CornerRadius = AppearanceConfig.NormalizeCornerRadius(cornerRadius.Value);
            }

            if (acrylic is not null)
            {
                _config.Appearance.Acrylic = acrylic.Value;
            }

            if (opacity is not null)
            {
                _config.Appearance.Opacity = AppearanceConfig.NormalizeOpacity(opacity.Value);
            }

            if (acrylicBlur is not null)
            {
                _config.Appearance.AcrylicBlur = AppearanceConfig.NormalizeAcrylicBlur(acrylicBlur);
            }

            if (lucideColor is not null)
            {
                _config.Appearance.LucideColor = LucidePalette.Normalize(lucideColor);
            }

            SaveUnlocked();
        }

        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearItem(string gridId, string key)
    {
        key = KeyboardLayout.NormalizeKey(key);
        lock (_lock)
        {
            var grid = _config.Grids.FirstOrDefault(g =>
                string.Equals(g.Id, gridId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Grid '{gridId}' was not found.");

            grid.Items.RemoveAll(i =>
                string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase));
            SaveUnlocked();
        }

        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SaveUnlocked()
    {
        // Ignore watcher long enough for atomic replace + late FS events.
        _ignoreWatcherUntilUtc = DateTime.UtcNow.AddSeconds(2);
        var yaml = _serializer.Serialize(_config);
        var tempPath = ConfigPath + ".tmp";
        File.WriteAllText(tempPath, yaml);
        File.Copy(tempPath, ConfigPath, overwrite: true);
        File.Delete(tempPath);
    }

    private static string ShippedExamplePath =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "config.example.yaml");

    private static void EnsureShippedExampleExists()
    {
        if (!File.Exists(ShippedExamplePath))
        {
            throw new InvalidOperationException(
                $"Shipped example config was not found at '{ShippedExamplePath}'.");
        }
    }

    private AppConfig LoadShippedExample()
    {
        EnsureShippedExampleExists();
        var yaml = File.ReadAllText(ShippedExamplePath);
        var loaded = _deserializer.Deserialize<AppConfig>(yaml)
            ?? throw new InvalidOperationException("Shipped example config was empty.");
        Validate(loaded);
        return loaded;
    }

    private void EnsureDefaultConfig()
    {
        if (File.Exists(ConfigPath))
        {
            return;
        }

        EnsureShippedExampleExists();
        File.Copy(ShippedExamplePath, ConfigPath);
    }

    private void StartWatching()
    {
        _watcher = new FileSystemWatcher(ConfigDirectory, "config.yaml")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };

        void OnChanged(object sender, FileSystemEventArgs e)
        {
            // Editors often write via temp + rename; debounce briefly.
            Task.Run(async () =>
            {
                await Task.Delay(200);
                if (DateTime.UtcNow < _ignoreWatcherUntilUtc)
                {
                    return;
                }

                try
                {
                    Reload();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Config reload failed: {ex.Message}");
                }
            });
        }

        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Renamed += (_, _) => OnChanged(_watcher, new FileSystemEventArgs(WatcherChangeTypes.Changed, ConfigDirectory, "config.yaml"));
    }

    private static void Validate(AppConfig config)
    {
        if (config.Grids.Count == 0)
        {
            throw new InvalidOperationException("Config must define at least one grid.");
        }

        config.Layout ??= new LayoutConfig();
        config.Appearance ??= new AppearanceConfig();
        var resolved = KeyboardLayout.From(config.Layout);
        config.Layout.StartKey = resolved.StartKey;
        config.Layout.Columns = Math.Clamp(config.Layout.Columns, KeyboardLayout.MinSize, KeyboardLayout.MaxColumns);
        config.Layout.Rows = Math.Clamp(config.Layout.Rows, KeyboardLayout.MinSize, KeyboardLayout.MaxRows);
        config.Appearance.Theme = AppearanceConfig.NormalizeTheme(config.Appearance.Theme);
        config.Appearance.TileSize = AppearanceConfig.NormalizeTileSize(config.Appearance.TileSize);
        config.Appearance.CornerRadius = AppearanceConfig.NormalizeCornerRadius(config.Appearance.CornerRadius);
        config.Appearance.Opacity = AppearanceConfig.NormalizeOpacity(config.Appearance.Opacity);
        config.Appearance.AcrylicBlur = AppearanceConfig.NormalizeAcrylicBlur(config.Appearance.AcrylicBlur);
        config.Appearance.LucideColor = LucidePalette.Normalize(config.Appearance.LucideColor);

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var grid in config.Grids)
        {
            if (string.IsNullOrWhiteSpace(grid.Id))
            {
                throw new InvalidOperationException("Every grid needs an id.");
            }

            if (!ids.Add(grid.Id))
            {
                throw new InvalidOperationException($"Duplicate grid id: {grid.Id}");
            }

            grid.Items ??= [];
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in grid.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Key))
                {
                    throw new InvalidOperationException($"Item in grid '{grid.Id}' is missing a key.");
                }

                var key = KeyboardLayout.NormalizeKey(item.Key);
                item.Key = key;
                item.Icon ??= new IconConfig();
                item.Action ??= new ActionConfig();
                if (string.IsNullOrWhiteSpace(item.Icon.Type))
                {
                    item.Icon.Type = "lucide";
                }

                if (!string.IsNullOrWhiteSpace(item.Icon.Color))
                {
                    item.Icon.Color = LucidePalette.Normalize(item.Icon.Color);
                }

                if (!resolved.IsValidKey(key))
                {
                    Debug.WriteLine($"Ignoring key '{key}' in grid '{grid.Id}' (outside current layout).");
                    continue;
                }

                if (!keys.Add(key))
                {
                    throw new InvalidOperationException($"Duplicate key '{key}' in grid '{grid.Id}'.");
                }
            }
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
