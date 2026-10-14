using FastAction.Models;
using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace FastAction.Services;

public sealed class ThemeService : IDisposable
{
    private readonly UISettings _uiSettings = new();
    private readonly List<WeakReference<Window>> _windows = [];
    private Func<string>? _themeProvider;
    private bool _isDark;
    private bool _listening;

    public bool IsDark => _isDark;

    public event EventHandler<bool>? DarkModeChanged;

    public void Attach(Window window, Func<string>? themeProvider = null)
    {
        _windows.RemoveAll(w => !w.TryGetTarget(out _));
        _windows.Add(new WeakReference<Window>(window));
        if (themeProvider is not null)
        {
            _themeProvider = themeProvider;
        }

        if (!_listening)
        {
            _uiSettings.ColorValuesChanged += OnColorValuesChanged;
            _listening = true;
        }

        Apply();
    }

    public void Apply()
    {
        var systemDark = DetectSystemDark();
        if (systemDark != _isDark)
        {
            _isDark = systemDark;
            DarkModeChanged?.Invoke(this, systemDark);
        }

        foreach (var weak in _windows.ToArray())
        {
            if (!weak.TryGetTarget(out var window) || window.Content is not FrameworkElement root)
            {
                continue;
            }

            root.RequestedTheme = RequestedTheme();
        }
    }

    private ElementTheme RequestedTheme()
    {
        var preference = AppearanceConfig.NormalizeTheme(_themeProvider?.Invoke());
        return preference switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => _isDark ? ElementTheme.Dark : ElementTheme.Light,
        };
    }

    private bool DetectSystemDark()
    {
        var background = _uiSettings.GetColorValue(UIColorType.Background);
        return background.R < 128 && background.G < 128 && background.B < 128;
    }

    private void OnColorValuesChanged(UISettings sender, object args)
    {
        Window? dispatcherHost = null;
        foreach (var weak in _windows)
        {
            if (weak.TryGetTarget(out dispatcherHost))
            {
                break;
            }
        }

        dispatcherHost?.DispatcherQueue.TryEnqueue(Apply);
    }

    public void Dispose()
    {
        if (_listening)
        {
            _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
            _listening = false;
        }

        _windows.Clear();
    }
}
