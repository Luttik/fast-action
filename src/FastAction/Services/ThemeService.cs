using FastAction.Models;
using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace FastAction.Services;

public sealed class ThemeService : IDisposable
{
    private readonly UISettings _uiSettings = new();
    private Window? _window;
    private Func<string>? _themeProvider;

    public void Attach(Window window, Func<string>? themeProvider = null)
    {
        _window = window;
        _themeProvider = themeProvider;
        Apply();
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;
    }

    public void Apply()
    {
        if (_window?.Content is not FrameworkElement root)
        {
            return;
        }

        var preference = AppearanceConfigTheme();
        root.RequestedTheme = preference switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => DetectSystemTheme(),
        };
    }

    private string AppearanceConfigTheme() =>
        AppearanceConfig.NormalizeTheme(_themeProvider?.Invoke());

    private ElementTheme DetectSystemTheme()
    {
        var background = _uiSettings.GetColorValue(UIColorType.Background);
        var isDark = background.R < 128 && background.G < 128 && background.B < 128;
        return isDark ? ElementTheme.Dark : ElementTheme.Light;
    }

    private void OnColorValuesChanged(UISettings sender, object args)
    {
        _window?.DispatcherQueue.TryEnqueue(Apply);
    }

    public void Dispose()
    {
        _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
    }
}
