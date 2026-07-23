using FastAction.Models;
using FastAction.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;

namespace FastAction.Overlay;

public sealed partial class TileEditWindow : Window
{
    public enum EditResult
    {
        Cancel,
        Saved,
        Cleared,
    }

    private readonly IconResolver _iconResolver;
    private readonly string _key;
    private readonly bool _canClear;
    private readonly TaskCompletionSource<(EditResult Result, ActionItemConfig? Item)> _completion;
    private readonly DispatcherTimer _previewTimer;
    private readonly TypedEventHandler<object, WindowActivatedEventArgs> _activatedHandler;
    private readonly HotkeyCapture _hotkeyCapture;
    private bool _completed;
    private bool _isClosed;
    private bool _suppressPreview;
    private int _previewGeneration;
    private HotkeyConfig? _recordedHotkey;

    private TileEditWindow(
        IconResolver iconResolver,
        string key,
        ActionItemConfig? existing,
        TaskCompletionSource<(EditResult Result, ActionItemConfig? Item)> completion)
    {
        _iconResolver = iconResolver;
        _key = key;
        _canClear = existing is not null;
        _completion = completion;
        _activatedHandler = (_, _) => ApplySystemTheme();
        _hotkeyCapture = new HotkeyCapture(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        _hotkeyCapture.Captured += OnHotkeyCaptured;
        _hotkeyCapture.Cancelled += OnHotkeyCaptureCancelled;

        InitializeComponent();
        ConfigureWindow();
        ApplySystemTheme();

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _previewTimer.Tick += async (_, _) =>
        {
            _previewTimer.Stop();
            if (_isClosed)
            {
                return;
            }

            await RefreshPreviewAsync();
        };

        Populate(existing);

        Closed += OnClosed;
        Activated += _activatedHandler;

        _ = RefreshPreviewAsync();
    }

    public static Task<(EditResult Result, ActionItemConfig? Item)> ShowAsync(
        IconResolver iconResolver,
        string key,
        ActionItemConfig? existing,
        PointInt32? nearPoint = null,
        Action<TileEditWindow>? onReady = null)
    {
        // Must NOT use RunContinuationsAsynchronously: the overlay awaits this and
        // then mutates WinUI controls, which requires the UI dispatcher thread.
        var completion = new TaskCompletionSource<(EditResult, ActionItemConfig?)>();

        var window = new TileEditWindow(iconResolver, key, existing, completion);
        window.PositionNear(nearPoint);
        onReady?.Invoke(window);
        window.Activate();
        return completion.Task;
    }

    /// <summary>Test helper: build current form values and save.</summary>
    public void CommitSaveForTest()
    {
        Complete(EditResult.Saved, BuildItem());
    }

    private void ConfigureWindow()
    {
        ExtendsContentIntoTitleBar = false;
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = true;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = true;
        AppWindow.Title = _canClear ? "Edit action" : "Add action";

        try
        {
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        }
        catch
        {
            // Optional.
        }

        // ~DIP size; Resize uses physical pixels — convert via DPI after show.
        ResizeDips(440, 620);
    }

    private void PositionNear(PointInt32? nearPoint)
    {
        ResizeDips(440, 620);
        if (nearPoint is null)
        {
            return;
        }

        var size = AppWindow.Size;
        AppWindow.Move(new PointInt32(
            nearPoint.Value.X + 24,
            Math.Max(24, nearPoint.Value.Y - (size.Height / 4))));
    }

    private void ResizeDips(int widthDip, int heightDip)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(hwnd);
        var scale = dpi / 96.0;
        AppWindow.Resize(new SizeInt32(
            (int)Math.Round(widthDip * scale),
            (int)Math.Round(heightDip * scale)));
    }

    private void ApplySystemTheme()
    {
        if (_isClosed)
        {
            return;
        }

        FrameworkElement? root;
        try
        {
            root = Content as FrameworkElement;
        }
        catch (Exception)
        {
            // Window may already be closed when Activated fires during teardown.
            return;
        }

        if (root is null)
        {
            return;
        }

        var settings = new Windows.UI.ViewManagement.UISettings();
        var background = settings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
        var isDark = background.R < 128 && background.G < 128 && background.B < 128;
        root.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;
    }

    private void Populate(ActionItemConfig? existing)
    {
        _suppressPreview = true;
        try
        {
            TitleText.Text = existing is null ? "Add action" : "Edit action";
            KeyText.Text = $"Key {_key}";

            NameBox.Text = existing?.Name ?? string.Empty;

            var iconType = existing?.Icon.Type?.ToLowerInvariant() ?? "lucide";
            IconTypeBox.SelectedItem = iconType;
            IconValueBox.Text = iconType is "app" or "svg"
                ? existing?.Icon.Path ?? string.Empty
                : existing?.Icon.Name ?? "circle";

            var actionType = existing?.Action.Type?.ToLowerInvariant() ?? "shell";
            ActionTypeBox.SelectedItem = actionType;
            if (string.Equals(actionType, "grid", StringComparison.OrdinalIgnoreCase))
            {
                _recordedHotkey = null;
                HotkeyChordBox.Text = string.Empty;
                CommandBox.Text = existing?.Action.GridId ?? string.Empty;
                ArgsBox.Text = string.Empty;
            }
            else if (string.Equals(actionType, "hotkey", StringComparison.OrdinalIgnoreCase))
            {
                _recordedHotkey = new HotkeyConfig
                {
                    Key = existing?.Action.Key ?? string.Empty,
                    Modifiers = existing?.Action.Modifiers?.ToList() ?? [],
                };
                HotkeyChordBox.Text = HotkeyCapture.FormatChord(_recordedHotkey);
                CommandBox.Text = string.Empty;
                ArgsBox.Text = string.Empty;
            }
            else
            {
                _recordedHotkey = null;
                HotkeyChordBox.Text = string.Empty;
                CommandBox.Text = existing?.Action.Command ?? string.Empty;
                ArgsBox.Text = existing?.Action.Args is { Count: > 0 }
                    ? string.Join(' ', existing.Action.Args)
                    : string.Empty;
            }

            CwdBox.Text = existing?.Action.WorkingDirectory ?? string.Empty;

            ClearButton.Visibility = _canClear ? Visibility.Visible : Visibility.Collapsed;

            SyncActionFields();
            SyncBrowseVisibility();
        }
        finally
        {
            _suppressPreview = false;
        }
    }

    private void SyncActionFields()
    {
        var actionType = (ActionTypeBox.SelectedItem as string ?? "shell").ToLowerInvariant();
        if (actionType != "hotkey")
        {
            StopHotkeyCapture();
        }

        switch (actionType)
        {
            case "grid":
                HotkeyPanel.Visibility = Visibility.Collapsed;
                CommandBox.Visibility = Visibility.Visible;
                CommandBox.Header = "Grid id";
                CommandBox.PlaceholderText = "devtools";
                ArgsBox.Visibility = Visibility.Collapsed;
                CwdBox.Visibility = Visibility.Collapsed;
                break;
            case "hotkey":
                HotkeyPanel.Visibility = Visibility.Visible;
                CommandBox.Visibility = Visibility.Collapsed;
                ArgsBox.Visibility = Visibility.Collapsed;
                CwdBox.Visibility = Visibility.Collapsed;
                UpdateRecordButtonUi();
                break;
            default:
                HotkeyPanel.Visibility = Visibility.Collapsed;
                CommandBox.Visibility = Visibility.Visible;
                CommandBox.Header = "Command";
                CommandBox.PlaceholderText = "wt.exe · cursor · https://…";
                ArgsBox.Header = "Args (space-separated, optional)";
                ArgsBox.PlaceholderText = @"C:\workspace";
                ArgsBox.Visibility = Visibility.Visible;
                CwdBox.Visibility = Visibility.Visible;
                break;
        }
    }

    private void RecordHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_hotkeyCapture.IsListening)
        {
            StopHotkeyCapture();
            return;
        }

        ErrorBar.IsOpen = false;
        _hotkeyCapture.Start();
        UpdateRecordButtonUi();
    }

    private void ClearHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        StopHotkeyCapture();
        _recordedHotkey = null;
        HotkeyChordBox.Text = string.Empty;
    }

    private void OnHotkeyCaptured(object? sender, HotkeyConfig config)
    {
        if (_isClosed)
        {
            return;
        }

        _recordedHotkey = config;
        HotkeyChordBox.Text = HotkeyCapture.FormatChord(config);
        UpdateRecordButtonUi();
    }

    private void OnHotkeyCaptureCancelled(object? sender, EventArgs e)
    {
        if (_isClosed)
        {
            return;
        }

        UpdateRecordButtonUi();
    }

    private void StopHotkeyCapture()
    {
        if (_hotkeyCapture.IsListening)
        {
            _hotkeyCapture.Stop();
        }

        UpdateRecordButtonUi();
    }

    private void UpdateRecordButtonUi()
    {
        if (RecordHotkeyButton is null)
        {
            return;
        }

        if (_hotkeyCapture.IsListening)
        {
            RecordHotkeyButton.Content = "Listening… (Esc cancels)";
            HotkeyHintText.Text = "Hold modifiers, then press the key. Esc cancels.";
        }
        else
        {
            RecordHotkeyButton.Content = string.IsNullOrWhiteSpace(HotkeyChordBox.Text)
                ? "Record shortcut"
                : "Re-record shortcut";
            HotkeyHintText.Text = "Press Record, then the combination once. Esc cancels recording.";
        }
    }

    private void SyncBrowseVisibility()
    {
        var type = (IconTypeBox.SelectedItem as string ?? "lucide").ToLowerInvariant();
        BrowseButton.Visibility = type is "app" or "svg" ? Visibility.Visible : Visibility.Collapsed;
        IconValueBox.Header = type switch
        {
            "app" => "App path / executable",
            "svg" => "SVG path",
            _ => "Lucide icon name",
        };
        IconValueBox.PlaceholderText = type switch
        {
            "app" => "notepad.exe or C:\\…\\app.exe",
            "svg" => "icons/my.svg",
            _ => "wrench · terminal · settings",
        };
    }

    private void ActionTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncActionFields();
    }

    private void IconTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncBrowseVisibility();
        SchedulePreviewRefresh();
    }

    private void IconValueBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SchedulePreviewRefresh();
    }

    private void SchedulePreviewRefresh()
    {
        if (_suppressPreview || _isClosed)
        {
            return;
        }

        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private async Task RefreshPreviewAsync()
    {
        if (_isClosed)
        {
            return;
        }

        var generation = ++_previewGeneration;
        var icon = BuildIconConfig();
        PreviewStatus.Text = "Loading preview…";

        ImageSource? source = null;
        try
        {
            source = await _iconResolver.ResolveAsync(icon);
        }
        catch
        {
            source = null;
        }

        if (_isClosed || generation != _previewGeneration)
        {
            return;
        }

        PreviewImage.Source = source;
        var hasSource = source is not null;
        PreviewImage.Visibility = hasSource ? Visibility.Visible : Visibility.Collapsed;
        PreviewPlaceholder.Visibility = hasSource ? Visibility.Collapsed : Visibility.Visible;
        PreviewStatus.Text = hasSource
            ? $"Preview · {icon.Type}"
            : "Icon not found — check name or path";
    }

    private IconConfig BuildIconConfig()
    {
        var iconType = (IconTypeBox.SelectedItem as string ?? "lucide").Trim().ToLowerInvariant();
        var iconValue = IconValueBox.Text.Trim();
        return new IconConfig
        {
            Type = iconType,
            Name = iconType == "lucide"
                ? (string.IsNullOrWhiteSpace(iconValue) ? "circle" : iconValue)
                : null,
            Path = iconType is "app" or "svg" ? iconValue : null,
        };
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var type = (IconTypeBox.SelectedItem as string ?? "lucide").ToLowerInvariant();
        var presenter = AppWindow.Presenter as OverlappedPresenter;
        var wasAlwaysOnTop = presenter?.IsAlwaysOnTop ?? false;

        try
        {
            ErrorLog.Write($"browse-open type={type}");

            // Always-on-top parents can hide the shell picker behind the edit window,
            // which looks like a freeze/crash.
            if (presenter is not null)
            {
                presenter.IsAlwaysOnTop = false;
            }

            var picker = new FileOpenPicker(AppWindow.Id)
            {
                ViewMode = PickerViewMode.List,
            };

            if (type == "svg")
            {
                picker.FileTypeFilter.Add(".svg");
            }
            else
            {
                // Do not add "*" — the WinAppSDK picker rejects some wildcard filters.
                picker.FileTypeFilter.Add(".exe");
                picker.FileTypeFilter.Add(".lnk");

                var startMenuPrograms = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                    "Programs");
                if (Directory.Exists(startMenuPrograms))
                {
                    picker.SuggestedFolder = startMenuPrograms;
                }
            }

            var file = await picker.PickSingleFileAsync();
            ErrorLog.Write($"browse-done cancelled={file is null}");
            if (file is null || _isClosed || _completed)
            {
                return;
            }

            IconValueBox.Text = file.Path;
            await RefreshPreviewAsync();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("browse-failed", ex);
            if (!_isClosed && !_completed)
            {
                ErrorBar.Title = "Couldn’t browse";
                ErrorBar.Message = ex.Message;
                ErrorBar.IsOpen = true;
            }
        }
        finally
        {
            if (presenter is not null && !_isClosed)
            {
                presenter.IsAlwaysOnTop = wasAlwaysOnTop;
            }
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorBar.IsOpen = false;
        try
        {
            var item = BuildItem();
            Complete(EditResult.Saved, item);
        }
        catch (Exception ex)
        {
            ErrorBar.Title = "Can’t save";
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        Complete(EditResult.Cleared, null);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Complete(EditResult.Cancel, null);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        MarkClosed();

        if (!_completed)
        {
            _completed = true;
            _completion.TrySetResult((EditResult.Cancel, null));
        }
    }

    private void MarkClosed()
    {
        _isClosed = true;
        _previewTimer.Stop();
        _previewGeneration++;
        Activated -= _activatedHandler;
        _hotkeyCapture.Captured -= OnHotkeyCaptured;
        _hotkeyCapture.Cancelled -= OnHotkeyCaptureCancelled;
        _hotkeyCapture.Dispose();
    }

    private void Complete(EditResult result, ActionItemConfig? item)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        MarkClosed();

        // Close first so the overlay can reclaim focus, then complete the awaiter.
        // Completing before Close would run the overlay continuation while this
        // window is still tearing down.
        try
        {
            Close();
        }
        finally
        {
            _completion.TrySetResult((result, item));
        }
    }

    private ActionItemConfig BuildItem()
    {
        var actionType = (ActionTypeBox.SelectedItem as string ?? "shell").Trim().ToLowerInvariant();
        var primary = CommandBox.Text.Trim();
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = _key;
        }

        var action = new ActionConfig { Type = actionType };
        switch (actionType)
        {
            case "grid":
                if (string.IsNullOrWhiteSpace(primary))
                {
                    throw new InvalidOperationException("Grid id is required.");
                }

                action.GridId = primary;
                break;
            case "hotkey":
                StopHotkeyCapture();
                if (_recordedHotkey is null || string.IsNullOrWhiteSpace(_recordedHotkey.Key))
                {
                    throw new InvalidOperationException("Record a shortcut before saving.");
                }

                action.Key = _recordedHotkey.Key;
                action.Modifiers = _recordedHotkey.Modifiers is { Count: > 0 }
                    ? _recordedHotkey.Modifiers.ToList()
                    : null;
                if (!HotkeyService.TryParseHotkey(
                        new HotkeyConfig { Modifiers = action.Modifiers ?? [], Key = action.Key },
                        out _,
                        out _))
                {
                    throw new InvalidOperationException(
                        "Invalid hotkey — use modifiers Alt/Ctrl/Shift/Win and a key like S, Space, or F5.");
                }

                break;
            default:
                if (string.IsNullOrWhiteSpace(primary))
                {
                    throw new InvalidOperationException("Command is required.");
                }

                action.Command = primary;
                action.Args = ParseArgs(ArgsBox.Text);
                action.WorkingDirectory = !string.IsNullOrWhiteSpace(CwdBox.Text)
                    ? CwdBox.Text.Trim()
                    : null;
                break;
        }

        return new ActionItemConfig
        {
            Key = _key,
            Name = name,
            Icon = BuildIconConfig(),
            Action = action,
        };
    }

    private static List<string>? ParseArgs(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        return parts.Count == 0 ? null : parts;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
