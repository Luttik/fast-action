using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace FastAction.Services;

public sealed class ThemeService : IDisposable
{
    private readonly UISettings _uiSettings = new();
    private Window? _window;

    public void Attach(Window window)
    {
        _window = window;
        Apply();
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;
    }

    public void Apply()
    {
        if (_window?.Content is not FrameworkElement root)
        {
            return;
        }

        var background = _uiSettings.GetColorValue(UIColorType.Background);
        var isDark = background.R < 128 && background.G < 128 && background.B < 128;
        root.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;
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
