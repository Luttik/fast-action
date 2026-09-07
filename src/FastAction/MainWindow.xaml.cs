using FastAction.Overlay;
using FastAction.Services;
using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FastAction;

public sealed partial class MainWindow : Window
{
    private readonly ConfigService _configService;
    private readonly HotkeyService _hotkeyService;
    private readonly ThemeService? _themeService;
    private OverlayWindow? _overlay;
    private TaskbarIcon? _trayIcon;
    private ToggleMenuFlyoutItem? _startupMenuItem;
    private System.Drawing.Icon? _ownedTrayIcon;

    public MainWindow(
        ConfigService configService,
        HotkeyService hotkeyService,
        ThemeService? themeService = null)
    {
        _configService = configService;
        _hotkeyService = hotkeyService;
        _themeService = themeService;

        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        AppWindow.IsShownInSwitchers = false;

        try
        {
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        }
        catch
        {
            // Icon is optional for the hidden host.
        }

        AppWindow.Resize(new Windows.Graphics.SizeInt32(1, 1));
        Activated += OnActivated;
        SetupTrayIcon();

        if (_themeService is not null)
        {
            _themeService.Attach(this);
            ApplyTrayIconForTheme(_themeService.IsDark);
            _themeService.DarkModeChanged += (_, isDark) =>
                DispatcherQueue.TryEnqueue(() => ApplyTrayIconForTheme(isDark));
        }
    }

    private void SetupTrayIcon()
    {
        var flyout = new MenuFlyout();
        flyout.Items.Add(CreateMenuItem("Open overlay", (_, _) => ShowOverlay()));
        flyout.Items.Add(CreateMenuItem("Open config folder", (_, _) => _configService.OpenConfigFolder()));
        flyout.Items.Add(CreateMenuItem("Reload config", (_, _) =>
        {
            try
            {
                _configService.Reload();
                ShowTrayFeedback("Config reloaded", "Changes applied.");
            }
            catch (Exception ex)
            {
                ShowTrayFeedback("Config reload failed", ex.Message);
            }
        }));
        flyout.Items.Add(new MenuFlyoutSeparator());

        _startupMenuItem = new ToggleMenuFlyoutItem
        {
            Text = "Start with Windows",
            IsChecked = _configService.Config.RunOnStartup,
        };
        _startupMenuItem.Click += (_, _) =>
        {
            var enabled = _startupMenuItem.IsChecked;
            try
            {
                StartupService.SetEnabled(enabled);
                _configService.SetRunOnStartup(enabled);
            }
            catch (Exception ex)
            {
                _startupMenuItem.IsChecked = !enabled;
                ShowTrayFeedback("Couldn’t update startup setting", ex.Message);
            }
        };
        flyout.Items.Add(_startupMenuItem);

        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(CreateMenuItem("Exit", (_, _) =>
        {
            _trayIcon?.Dispose();
            _hotkeyService.Dispose();
            _configService.Dispose();
            Application.Current.Exit();
        }));

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Fast Action",
            ContextFlyout = flyout,
            MenuActivation = H.NotifyIcon.Core.PopupActivationMode.LeftOrRightClick,
            NoLeftClickDelay = true,
        };

        ApplyTrayIconForTheme(_themeService?.IsDark ?? false);

        _trayIcon.LeftClickCommand = new SimpleCommand(ShowOverlay);
        _trayIcon.ForceCreate();
        TrayHost.Children.Add(_trayIcon);
    }

    private void ApplyTrayIconForTheme(bool isDark)
    {
        if (_trayIcon is null)
        {
            return;
        }

        var brandDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Brand");
        // Dark Windows theme → light/inverted tray mark; light theme → dark mark.
        var preferredPng = Path.Combine(brandDir, isDark ? "tray-32-light.png" : "tray-32.png");
        var fallbackPng = Path.Combine(brandDir, "tray-32.png");
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");

        System.Drawing.Icon? next = null;
        try
        {
            var png = File.Exists(preferredPng)
                ? preferredPng
                : File.Exists(fallbackPng) ? fallbackPng : null;
            if (png is not null)
            {
                using var bitmap = new System.Drawing.Bitmap(png);
                var handle = bitmap.GetHicon();
                next = System.Drawing.Icon.FromHandle(handle).Clone() as System.Drawing.Icon;
                DestroyIcon(handle);
            }
            else if (File.Exists(iconPath))
            {
                next = new System.Drawing.Icon(iconPath, 32, 32);
            }
        }
        catch
        {
            next = null;
        }

        if (next is null)
        {
            return;
        }

        var previous = _ownedTrayIcon;
        _ownedTrayIcon = next;
        _trayIcon.Icon = next;
        previous?.Dispose();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static MenuFlyoutItem CreateMenuItem(string text, RoutedEventHandler handler)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += handler;
        return item;
    }

    public void AttachOverlay(OverlayWindow overlay)
    {
        _overlay = overlay;
        _overlay.FeedbackRequested += (_, args) =>
        {
            DispatcherQueue.TryEnqueue(() =>
                ShowTrayFeedback(args.Title, args.Message));
        };
    }

    public void InitializeHotkeys()
    {
        _hotkeyService.HotkeyPressed += (_, _) => ShowOverlay();
        ApplyHotkeyFromConfig();
        _configService.ConfigChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    ApplyHotkeyFromConfig();
                }
                catch (Exception ex)
                {
                    ShowTrayFeedback("Hotkey update failed", ex.Message);
                }
            });
        };
    }

    private void ApplyHotkeyFromConfig()
    {
        _hotkeyService.Apply(_configService.Config.Hotkey);
    }

    /// <summary>Syncs the Windows startup registration with config.yaml, in case the
    /// exe path moved (rebuild) or the setting was edited outside the tray menu.</summary>
    public void InitializeStartup()
    {
        ApplyStartupFromConfig();
        _configService.ConfigChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(ApplyStartupFromConfig);
        };
    }

    private void ApplyStartupFromConfig()
    {
        var enabled = _configService.Config.RunOnStartup;
        try
        {
            StartupService.SetEnabled(enabled);
        }
        catch (Exception ex)
        {
            ShowTrayFeedback("Startup registration failed", ex.Message);
        }

        if (_startupMenuItem is not null)
        {
            _startupMenuItem.IsChecked = enabled;
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args) =>
        DispatcherQueue.TryEnqueue(() => AppWindow.Hide());

    private void ShowOverlay() => _overlay?.ToggleOverlay();

    private void ShowTrayFeedback(string title, string message)
    {
        try
        {
            _trayIcon?.ShowNotification(title, message);
        }
        catch
        {
            // Notification APIs can fail on some shells; ignore.
        }
    }
    private sealed class SimpleCommand : System.Windows.Input.ICommand
    {
        private readonly Action _execute;

        public SimpleCommand(Action execute) => _execute = execute;

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _execute();
    }
}
