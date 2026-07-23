using FastAction.Models;
using FastAction.Services;
using FastAction.ViewModels;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using WinRT;
using WinRT.Interop;

namespace FastAction.Overlay;

public sealed partial class OverlayWindow : Window
{
    private readonly ConfigService _configService;
    private readonly ActionRunner _actionRunner;
    private readonly IconResolver _iconResolver;
    private readonly ThemeService _themeService;
    private readonly WindowsSystemDispatcherQueueHelper _dispatcherQueueHelper = new();
    private readonly Stack<string> _gridStack = new();

    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _configurationSource;
    private bool _isVisible;
    private bool _isDragging;
    private bool _isEditing;
    private bool _awaitingActivation;
    private int _bindGeneration;
    private DateTime _shownAtUtc;
    private PointInt32 _dragStartCursor;
    private PointInt32 _dragStartWindow;
    private DispatcherTimer? _statusTimer;

    public event EventHandler<FeedbackEventArgs>? FeedbackRequested;

    public OverlayWindow(
        ConfigService configService,
        ActionRunner actionRunner,
        IconResolver iconResolver,
        ThemeService themeService)
    {
        _configService = configService;
        _actionRunner = actionRunner;
        _iconResolver = iconResolver;
        _themeService = themeService;

        InitializeComponent();
        ConfigureWindow();
        TrySetAcrylicBackdrop();
        _themeService.Attach(this);
        RootGrid.Loaded += (_, _) => TryFocusOverlay();
        RootGrid.PointerPressed += RootGrid_PointerPressed;
        RootGrid.PointerMoved += RootGrid_PointerMoved;
        RootGrid.PointerReleased += RootGrid_PointerReleased;
        RootGrid.PointerCaptureLost += RootGrid_PointerCaptureLost;
        Activated += OnWindowActivated;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(RootGrid).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (IsInteractiveElement(e.OriginalSource))
        {
            return;
        }

        NativeMethods.GetCursorPos(out var cursor);
        var pos = AppWindow.Position;
        _dragStartCursor = new PointInt32(cursor.X, cursor.Y);
        _dragStartWindow = new PointInt32(pos.X, pos.Y);
        _isDragging = true;
        RootGrid.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        NativeMethods.GetCursorPos(out var cursor);
        var x = _dragStartWindow.X + (cursor.X - _dragStartCursor.X);
        var y = _dragStartWindow.Y + (cursor.Y - _dragStartCursor.Y);
        AppWindow.Move(new PointInt32(x, y));
        e.Handled = true;
    }

    private void RootGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        EndDrag(e.Pointer);
        e.Handled = true;
    }

    private void RootGrid_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            EndDrag(e.Pointer);
        }
    }

    private void EndDrag(Pointer pointer)
    {
        _isDragging = false;
        _shownAtUtc = DateTime.UtcNow;
        try
        {
            RootGrid.ReleasePointerCapture(pointer);
        }
        catch
        {
            // Capture may already be released.
        }

        TryFocusOverlay();
    }

    private static bool IsInteractiveElement(object? source)
    {
        for (var current = source as DependencyObject;
             current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is Button)
            {
                return true;
            }
        }

        return false;
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        var isActive = args.WindowActivationState != WindowActivationState.Deactivated;

        if (_configurationSource is not null)
        {
            _configurationSource.IsInputActive = isActive;
        }

        if (isActive)
        {
            _awaitingActivation = false;
            TryFocusOverlay();
            return;
        }

        // Close when focus is lost (unless still opening, mid-drag, or editing).
        if (_isVisible
            && !_awaitingActivation
            && !_isDragging
            && !_isEditing
            && DateTime.UtcNow - _shownAtUtc > TimeSpan.FromMilliseconds(100))
        {
            HideOverlay();
        }
    }

    public bool IsOverlayVisible => _isVisible;

    public void ShowOverlay()
    {
        _gridStack.Clear();
        var root = _configService.GetRootGrid();
        if (root is null)
        {
            NotifyFeedback(
                "No actions configured",
                "Add a root grid in config.yaml, then reload.",
                InfoBarSeverity.Error,
                forceTray: true);
            return;
        }

        _gridStack.Push(root.Id);
        _ = BindGridAsync(root);
        ClearStatus();

        PositionOnCursorMonitor();
        _shownAtUtc = DateTime.UtcNow;
        _awaitingActivation = true;
        if (_configurationSource is not null)
        {
            _configurationSource.IsInputActive = true;
        }

        AppWindow.Show();
        _isVisible = true;
        // Hidden windows often report stale DPI; correct size after show on the target monitor.
        PositionOnCursorMonitor();
        ForceForeground();
        TryFocusOverlay();

        // Re-assert focus and size after the window is fully shown / DPI settles.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isVisible)
            {
                return;
            }

            PositionOnCursorMonitor();
            ForceForeground();
            TryFocusOverlay();
        });
    }

    public void HideOverlay()
    {
        _bindGeneration++;
        ClearStatus();
        AppWindow.Hide();
        _isVisible = false;
        _awaitingActivation = false;
        _gridStack.Clear();
    }

    public void ShowStatus(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Error)
    {
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;

        _statusTimer?.Stop();
        if (severity is InfoBarSeverity.Success or InfoBarSeverity.Informational)
        {
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _statusTimer.Tick += (_, _) =>
            {
                _statusTimer.Stop();
                StatusBar.IsOpen = false;
            };
            _statusTimer.Start();
        }
    }

    private void ClearStatus()
    {
        _statusTimer?.Stop();
        StatusBar.IsOpen = false;
        StatusBar.Title = string.Empty;
        StatusBar.Message = string.Empty;
    }

    private void NotifyFeedback(
        string title,
        string message,
        InfoBarSeverity severity,
        bool forceTray = false)
    {
        if (_isVisible && !forceTray)
        {
            ShowStatus(title, message, severity);
            return;
        }

        FeedbackRequested?.Invoke(this, new FeedbackEventArgs(title, message, severity));
    }

    private void TryFocusOverlay()
    {
        if (!_isVisible)
        {
            return;
        }

        RootGrid.Focus(FocusState.Programmatic);
    }

    private void ForceForeground()
    {
        if (!_isVisible)
        {
            return;
        }

        var hwnd = WindowNative.GetWindowHandle(this);
        Activate();

        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == hwnd)
        {
            return;
        }

        var foreThread = NativeMethods.GetWindowThreadProcessId(foreground, IntPtr.Zero);
        var appThread = NativeMethods.GetCurrentThreadId();
        if (foreThread != appThread)
        {
            _ = NativeMethods.AttachThreadInput(foreThread, appThread, true);
        }

        _ = NativeMethods.SetForegroundWindow(hwnd);
        _ = NativeMethods.BringWindowToTop(hwnd);

        if (foreThread != appThread)
        {
            _ = NativeMethods.AttachThreadInput(foreThread, appThread, false);
        }

        Activate();
    }

    public void ToggleOverlay()
    {
        if (_isVisible)
        {
            HideOverlay();
        }
        else
        {
            ShowOverlay();
        }
    }

    private void ConfigureWindow()
    {
        ExtendsContentIntoTitleBar = true;
        OverlappedPresenter presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Hide();

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Title = "Fast Action";

        Closed += OnClosed;
    }

    private bool TrySetAcrylicBackdrop()
    {
        if (!DesktopAcrylicController.IsSupported())
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
            return false;
        }

        _dispatcherQueueHelper.EnsureWindowsSystemDispatcherQueueController();

        _configurationSource = new SystemBackdropConfiguration
        {
            IsInputActive = true,
        };
        SetConfigurationSourceTheme();

        if (Content is FrameworkElement root)
        {
            root.ActualThemeChanged += (_, _) => SetConfigurationSourceTheme();
        }

        _acrylicController = new DesktopAcrylicController();
        _acrylicController.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
        _acrylicController.SetSystemBackdropConfiguration(_configurationSource);
        return true;
    }

    private void SetConfigurationSourceTheme()
    {
        if (_configurationSource is null || Content is not FrameworkElement root)
        {
            return;
        }

        _configurationSource.Theme = root.ActualTheme switch
        {
            ElementTheme.Dark => SystemBackdropTheme.Dark,
            ElementTheme.Light => SystemBackdropTheme.Light,
            _ => SystemBackdropTheme.Default,
        };
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _acrylicController?.Dispose();
        _acrylicController = null;
        _configurationSource = null;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // Monitor plug/unplug: re-place while visible, or next show will also recompute.
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_isVisible)
            {
                PositionOnCursorMonitor();
            }
        });
    }

    private const int TileSize = 88;
    private const int TileSpacing = 8;
    private const int WindowPadX = 24;
    private const int WindowPadTop = 44;
    private const int WindowPadBottom = 24;

    private void PositionOnCursorMonitor()
    {
        var cursor = GetCursorPoint();
        var displayArea = DisplayArea.Primary;
        try
        {
            displayArea = DisplayArea.GetFromPoint(cursor, DisplayAreaFallback.Primary);
        }
        catch
        {
            // Fall back to primary.
        }

        var cols = KeyboardLayout.Rows[0].Length;
        var rows = KeyboardLayout.Rows.Length;
        var contentWidthDip = (cols * TileSize) + ((cols - 1) * TileSpacing);
        var contentHeightDip = (rows * TileSize) + ((rows - 1) * TileSpacing);
        var widthDip = contentWidthDip + (WindowPadX * 2);
        var heightDip = contentHeightDip + WindowPadTop + WindowPadBottom;

        // Size for the monitor under the cursor — not the (often stale) HWND/XamlRoot DPI
        // after dock/undock or while the overlay is still hidden.
        var scale = GetScaleForPoint(cursor);
        var width = (int)Math.Ceiling(widthDip * scale);
        var height = (int)Math.Ceiling(heightDip * scale);

        var work = displayArea.WorkArea;
        if (work.Width <= 0 || work.Height <= 0)
        {
            return;
        }

        // Clamp so a bad DPI reading cannot create a tiny or huge window.
        width = Math.Clamp(width, 200, work.Width);
        height = Math.Clamp(height, 160, work.Height);

        var x = work.X + (work.Width - width) / 2;
        var y = work.Y + (work.Height - height) / 2;
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private static double GetScaleForPoint(PointInt32 point)
    {
        var monitor = NativeMethods.MonitorFromPoint(
            new NativeMethods.Point { X = point.X, Y = point.Y },
            NativeMethods.MonitorDefaultToNearest);
        if (monitor != IntPtr.Zero
            && NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MdtEffectiveDpi, out var dpiX, out _) == 0
            && dpiX > 0)
        {
            return dpiX / 96.0;
        }

        return 1.0;
    }

    private static PointInt32 GetCursorPoint()
    {
        NativeMethods.GetCursorPos(out var point);
        return new PointInt32(point.X, point.Y);
    }

    private async Task BindGridAsync(GridConfig grid)
    {
        var generation = ++_bindGeneration;
        var itemMap = (grid.Items ?? [])
            .Where(i => !string.IsNullOrWhiteSpace(i.Key) && KeyboardLayout.IsValidKey(i.Key))
            .GroupBy(i => KeyboardLayout.NormalizeKey(i.Key))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var rows = new List<GridRowViewModel>();

        for (var r = 0; r < KeyboardLayout.Rows.Length; r++)
        {
            var tiles = new List<ActionTileViewModel>();
            foreach (var key in KeyboardLayout.Rows[r])
            {
                itemMap.TryGetValue(key, out var item);
                var tile = new ActionTileViewModel
                {
                    Key = key,
                    Name = item?.Name,
                    Item = item,
                };

                if (item is not null)
                {
                    tile.Icon = await _iconResolver.ResolveAsync(item.Icon);
                }

                if (generation != _bindGeneration)
                {
                    return;
                }

                tiles.Add(tile);
            }

            rows.Add(new GridRowViewModel
            {
                Tiles = tiles,
                LeadingOffset = 0,
            });
        }

        if (generation != _bindGeneration)
        {
            return;
        }

        BuildRows(rows);
    }

    private void BuildRows(IReadOnlyList<GridRowViewModel> rows)
    {
        RowsHost.Children.Clear();

        foreach (var row in rows)
        {
            var rowPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = TileSpacing,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            foreach (var tile in row.Tiles)
            {
                rowPanel.Children.Add(CreateTileButton(tile));
            }

            RowsHost.Children.Add(rowPanel);
        }
    }

    private FrameworkElement CreateTileButton(ActionTileViewModel tile)
    {
        var wrapper = new Grid
        {
            Width = TileSize,
            Height = TileSize,
        };

        var button = new Button
        {
            Width = TileSize,
            Height = TileSize,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Tag = tile,
            IsEnabled = true,
            Opacity = tile.IsEmpty ? 0.35 : 1,
            CornerRadius = new CornerRadius(12),
            Content = new Image
            {
                Width = 36,
                Height = 36,
                Stretch = Stretch.Uniform,
                Source = tile.Icon,
            },
        };
        button.Click += Tile_Click;
        button.RightTapped += Tile_RightTapped;

        // Letter sits on the tile chrome, not inside the centered button content.
        var keyLabel = new TextBlock
        {
            Text = tile.Key,
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Opacity = tile.IsEmpty ? 0.35 : 0.7,
            Margin = new Thickness(8, 5, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        };

        wrapper.Children.Add(button);
        wrapper.Children.Add(keyLabel);
        return wrapper;
    }

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ActionTileViewModel tile })
        {
            ActivateTile(tile);
        }
    }

    private async void Tile_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: ActionTileViewModel tile })
        {
            return;
        }

        if (!_configService.Config.EditOnRightClick)
        {
            ShowStatus(
                "Editing disabled",
                "Set editOnRightClick: true in config.yaml to edit tiles.",
                InfoBarSeverity.Informational);
            return;
        }

        if (_gridStack.Count == 0)
        {
            return;
        }

        var gridId = _gridStack.Peek();
        var key = tile.Key;
        var existing = tile.Item;
        _isEditing = true;
        _shownAtUtc = DateTime.UtcNow;
        try
        {
            ErrorLog.Write($"edit-open key={key} grid={gridId}");
            var near = AppWindow.Position;
            var (result, item) = await TileEditWindow.ShowAsync(
                _iconResolver,
                key,
                existing,
                near);
            ErrorLog.Write($"edit-closed result={result} itemNull={item is null}");

            await ApplyEditResultAsync(gridId, key, result, item);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("edit-failed", ex);
            ShowStatus("Couldn’t save", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _isEditing = false;
            _shownAtUtc = DateTime.UtcNow;
            try
            {
                ForceForeground();
                TryFocusOverlay();
            }
            catch (Exception ex)
            {
                ErrorLog.Write("edit-finally-focus", ex);
            }
        }
    }

    private async Task ApplyEditResultAsync(
        string gridId,
        string key,
        TileEditWindow.EditResult result,
        ActionItemConfig? item)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            var tcs = new TaskCompletionSource();
            if (!DispatcherQueue.TryEnqueue(() => tcs.SetResult()))
            {
                throw new InvalidOperationException("Failed to marshal to UI thread.");
            }

            await tcs.Task;
        }

        switch (result)
        {
            case TileEditWindow.EditResult.Saved when item is not null:
                ErrorLog.Write("edit-upsert-begin");
                _configService.UpsertItem(gridId, item);
                ErrorLog.Write("edit-upsert-done");
                _iconResolver.ClearCache();
                ErrorLog.Write("edit-refresh-begin");
                await RefreshCurrentGridAsync();
                ErrorLog.Write("edit-refresh-done");
                ShowStatus("Saved", $"Updated key {key}.", InfoBarSeverity.Success);
                break;
            case TileEditWindow.EditResult.Cleared:
                ErrorLog.Write("edit-clear-begin");
                _configService.ClearItem(gridId, key);
                _iconResolver.ClearCache();
                await RefreshCurrentGridAsync();
                ShowStatus("Cleared", $"Removed action on key {key}.", InfoBarSeverity.Success);
                break;
            default:
                ErrorLog.Write("edit-cancelled");
                break;
        }
    }

    /// <summary>Agent/self-test: resolve every lucide icon used by the root grid.</summary>
    public async Task SmokeTestIconsAsync()
    {
        ErrorLog.Reset();
        ErrorLog.Write("smoke-icons-start");
        if (!_isVisible)
        {
            ShowOverlay();
            await Task.Delay(300);
        }

        var grid = _configService.GetRootGrid()
            ?? throw new InvalidOperationException("No root grid.");
        var missing = new List<string>();
        foreach (var item in grid.Items ?? [])
        {
            if (!string.Equals(item.Icon?.Type, "lucide", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var source = await _iconResolver.ResolveAsync(item.Icon);
            if (source is null)
            {
                missing.Add($"{item.Key}:{item.Icon?.Name}");
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException("Missing lucide icons: " + string.Join(", ", missing));
        }

        ErrorLog.Write($"smoke-icons-pass count={grid.Items?.Count}");
    }

    /// <summary>Agent/self-test: show overlay, save via editor window, refresh.</summary>
    public async Task SmokeTestEditAsync()
    {
        ErrorLog.Reset();
        ErrorLog.Write("smoke-start");
        if (!_isVisible)
        {
            ShowOverlay();
            await Task.Delay(300);
        }

        if (_gridStack.Count == 0)
        {
            throw new InvalidOperationException("Overlay has no grid stack after ShowOverlay.");
        }

        var gridId = _gridStack.Peek();
        const string key = "1";
        var existing = _configService.GetGrid(gridId)?.Items?
            .FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase));

        ErrorLog.Write("smoke-open-editor");
        var editTask = TileEditWindow.ShowAsync(
            _iconResolver,
            key,
            existing,
            AppWindow.Position,
            onReady: window =>
            {
                _ = DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        await Task.Delay(200);
                        ErrorLog.Write("smoke-commit-save");
                        window.CommitSaveForTest();
                    }
                    catch (Exception ex)
                    {
                        ErrorLog.Write("smoke-commit-failed", ex);
                    }
                });
            });

        var (result, item) = await editTask;
        ErrorLog.Write($"smoke-editor-done result={result}");
        await ApplyEditResultAsync(gridId, key, result, item);
        ErrorLog.Write("smoke-pass");
    }

    private async Task RefreshCurrentGridAsync()
    {
        if (_gridStack.Count == 0)
        {
            return;
        }

        var grid = _configService.GetGrid(_gridStack.Peek());
        if (grid is not null)
        {
            await BindGridAsync(grid);
        }
    }

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            GoBackOrHide();
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.Back)
        {
            if (_gridStack.Count > 1)
            {
                GoBackOrHide();
                e.Handled = true;
            }

            return;
        }

        var key = VirtualKeyToGridKey(e.Key);
        if (key is null)
        {
            return;
        }

        foreach (var row in RowsHost.Children.OfType<StackPanel>())
        {
            foreach (var wrapper in row.Children.OfType<Grid>())
            {
                var button = wrapper.Children.OfType<Button>().FirstOrDefault();
                if (button?.Tag is ActionTileViewModel tile
                    && string.Equals(tile.Key, key, StringComparison.OrdinalIgnoreCase)
                    && !tile.IsEmpty)
                {
                    ActivateTile(tile);
                    e.Handled = true;
                    return;
                }
            }
        }
    }

    private void ActivateTile(ActionTileViewModel tile)
    {
        if (tile.Item is null)
        {
            ShowStatus("Empty slot", $"No action bound to key {tile.Key}.", InfoBarSeverity.Informational);
            return;
        }

        var action = tile.Item.Action;
        if (string.Equals(action.Type, "grid", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(action.GridId))
            {
                ShowStatus(
                    "Invalid action",
                    $"“{tile.Item.Name}” is a grid action but has no gridId.",
                    InfoBarSeverity.Error);
                return;
            }

            var existed = _configService.GetGrid(action.GridId) is not null;
            var next = _configService.GetOrCreateGrid(action.GridId);
            ClearStatus();
            _gridStack.Push(next.Id);
            _ = BindGridAsync(next);
            if (!existed)
            {
                ShowStatus(
                    "New grid",
                    $"Created empty grid “{next.Id}”. Right-click tiles to add actions.",
                    InfoBarSeverity.Informational);
            }

            return;
        }

        if (string.Equals(action.Type, "shell", StringComparison.OrdinalIgnoreCase))
        {
            if (_actionRunner.TryRunShell(action, out var error))
            {
                HideOverlay();
                return;
            }

            var detail = string.IsNullOrWhiteSpace(error)
                ? $"Could not start “{action.Command}”."
                : error;
            ShowStatus($"Couldn’t run {tile.Item.Name}", detail, InfoBarSeverity.Error);
            return;
        }

        if (string.Equals(action.Type, "hotkey", StringComparison.OrdinalIgnoreCase))
        {
            // Hide first so the previously focused app receives the chord.
            HideOverlay();
            _ = SendHotkeyAfterHideAsync(tile.Item.Name, action);
            return;
        }

        ShowStatus(
            "Unknown action",
            $"Action type “{action.Type}” is not supported.",
            InfoBarSeverity.Error);
    }

    private async Task SendHotkeyAfterHideAsync(string name, ActionConfig action)
    {
        await Task.Delay(80);
        if (_actionRunner.TryRunHotkey(action, out var error))
        {
            return;
        }

        var detail = string.IsNullOrWhiteSpace(error)
            ? "Could not send the hotkey."
            : error;
        NotifyFeedback($"Couldn’t run {name}", detail, InfoBarSeverity.Error, forceTray: true);
    }

    private void GoBackOrHide()
    {
        if (_gridStack.Count > 1)
        {
            _gridStack.Pop();
            var parentId = _gridStack.Peek();
            var parent = _configService.GetGrid(parentId);
            if (parent is not null)
            {
                _ = BindGridAsync(parent);
                return;
            }
        }

        HideOverlay();
    }

    private static string? VirtualKeyToGridKey(VirtualKey key)
    {
        if (key is >= VirtualKey.A and <= VirtualKey.Z)
        {
            return ((char)('A' + (key - VirtualKey.A))).ToString();
        }

        if (key is >= VirtualKey.Number1 and <= VirtualKey.Number4)
        {
            return ((char)('1' + (key - VirtualKey.Number1))).ToString();
        }

        if (key is >= VirtualKey.NumberPad1 and <= VirtualKey.NumberPad4)
        {
            return ((char)('1' + (key - VirtualKey.NumberPad1))).ToString();
        }

        return null;
    }

    private static class NativeMethods
    {
        public const uint MonitorDefaultToNearest = 2;
        public const int MdtEffectiveDpi = 0;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool GetCursorPos(out Point point);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(Point pt, uint dwFlags);

        [System.Runtime.InteropServices.DllImport("Shcore.dll")]
        public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct Point
        {
            public int X;
            public int Y;
        }
    }
}
