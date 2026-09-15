using System.Text;
using FastAction.Overlay;
using FastAction.Services;
using Microsoft.UI.Xaml;

namespace FastAction;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private OverlayWindow? _overlayWindow;
    private ConfigService? _configService;
    private HotkeyService? _hotkeyService;
    private ThemeService? _themeService;
    private IconResolver? _iconResolver;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FastAction",
                "crash.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(
                path,
                $"[{DateTime.Now:O}] {e.Message}\n{e.Exception}\n\n",
                Encoding.UTF8);
        }
        catch
        {
            // Ignore logging failures.
        }

        e.Handled = true;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _configService = new ConfigService();
            _configService.Initialize();

            _hotkeyService = new HotkeyService(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            _themeService = new ThemeService();
            _iconResolver = new IconResolver(_configService.ConfigDirectory);

            _mainWindow = new MainWindow(_configService, _hotkeyService, _themeService);
            _overlayWindow = new OverlayWindow(_configService, _iconResolver, _themeService);

            _mainWindow.AttachOverlay(_overlayWindow);
            _mainWindow.Activate();
            _mainWindow.InitializeHotkeys();
            _mainWindow.InitializeStartup();
            _overlayWindow.AppWindow.Hide();

            if (Environment.GetCommandLineArgs()
                .Any(a => string.Equals(a, "--smoke-edit", StringComparison.OrdinalIgnoreCase)))
            {
                _ = RunSmokeEditAsync();
            }
            else if (Environment.GetCommandLineArgs()
                .Any(a => string.Equals(a, "--smoke-icons", StringComparison.OrdinalIgnoreCase)))
            {
                _ = RunSmokeIconsAsync();
            }
        }
        catch (Exception ex)
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FastAction",
                "crash.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, ex.ToString());
            _mainWindow ??= new MainWindow(
                _configService ?? new ConfigService(),
                _hotkeyService ?? new HotkeyService(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()),
                _themeService);
            _mainWindow.Activate();
        }
    }

    private async Task RunSmokeIconsAsync()
    {
        var resultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FastAction",
            "smoke-icons-result.txt");

        try
        {
            await Task.Delay(400);
            if (_overlayWindow is null)
            {
                throw new InvalidOperationException("Overlay window was not created.");
            }

            await _overlayWindow.SmokeTestIconsAsync();
            await File.WriteAllTextAsync(resultPath, "PASS\n");
        }
        catch (Exception ex)
        {
            ErrorLog.Write("smoke-icons-app-failed", ex);
            await File.WriteAllTextAsync(resultPath, "FAIL\n" + ex);
        }
        finally
        {
            await Task.Delay(200);
            Exit();
        }
    }

    private async Task RunSmokeEditAsync()
    {
        var resultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FastAction",
            "smoke-edit-result.txt");

        try
        {
            await Task.Delay(400);
            if (_overlayWindow is null)
            {
                throw new InvalidOperationException("Overlay window was not created.");
            }

            await _overlayWindow.SmokeTestEditAsync();
            await File.WriteAllTextAsync(resultPath, "PASS\n");
        }
        catch (Exception ex)
        {
            ErrorLog.Write("smoke-app-failed", ex);
            await File.WriteAllTextAsync(resultPath, "FAIL\n" + ex);
        }
        finally
        {
            await Task.Delay(200);
            Exit();
        }
    }
}
