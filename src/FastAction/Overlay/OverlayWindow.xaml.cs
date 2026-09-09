using FastAction.Models;
using FastAction.Services;
using FastAction.ViewModels;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
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
    private DispatcherTimer? _opacitySaveTimer;
    private bool _suppressSettingsEvents;
    private bool _settingsOpen;

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
        ApplyBackdrop();
        _themeService.Attach(this, () => _configService.Config.Appearance?.Theme ?? "system");
        RootGrid.Loaded += (_, _) =>
        {
            TryFocusOverlay();
            SyncSettingsPane();
            RebuildMenus();
        };
        RootGrid.ActualThemeChanged += (_, _) =>
        {
            SetConfigurationSourceTheme();
            ApplyBackdrop();
        };
        RootGrid.PointerPressed += RootGrid_PointerPressed;
        RootGrid.PointerMoved += RootGrid_PointerMoved;
        RootGrid.PointerReleased += RootGrid_PointerReleased;
        RootGrid.PointerCaptureLost += RootGrid_PointerCaptureLost;
        Activated += OnWindowActivated;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _configService.ConfigChanged += OnConfigChanged;
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
            if (current is Button
                or MenuBar
                or MenuBarItem
                or ComboBox
                or NumberBox
                or RepeatButton
                or ToggleButton
                or ToggleSwitch
                or Slider
                or TextBox)
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

        // Close when focus is lost (unless still opening, mid-drag, editing, or a flyout is open).
        if (_isVisible
            && !_awaitingActivation
            && !_isDragging
            && !_isEditing
            && DateTime.UtcNow - _shownAtUtc > TimeSpan.FromMilliseconds(100)
            && !HasOpenPopup())
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
        RebuildMenus();
        SyncSettingsPane();

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

    public void ShowOverlaySettings()
    {
        if (!_isVisible)
        {
            ShowOverlay();
        }

        SetSettingsOpen(true);
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

    private void ApplyBackdrop()
    {
        var appearance = _configService.Config.Appearance ?? new AppearanceConfig();
        var dark = IsDarkTheme();
        var baseColor = dark ? Color.FromArgb(255, 32, 32, 32) : Color.FromArgb(255, 243, 243, 243);
        var opacity = AppearanceConfig.NormalizeOpacity(appearance.Opacity) / 100.0;
        var useAcrylic = appearance.Acrylic && DesktopAcrylicController.IsSupported();

        if (!useAcrylic)
        {
            _acrylicController?.Dispose();
            _acrylicController = null;
            _configurationSource = null;
            SystemBackdrop = appearance.Acrylic ? new DesktopAcrylicBackdrop() : null;
            var alpha = appearance.Acrylic
                ? (byte)255
                : (byte)Math.Clamp((int)Math.Round(opacity * 255), 51, 255);
            RootGrid.Background = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
            return;
        }

        RootGrid.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        _dispatcherQueueHelper.EnsureWindowsSystemDispatcherQueueController();

        _configurationSource ??= new SystemBackdropConfiguration { IsInputActive = true };
        SetConfigurationSourceTheme();

        if (_acrylicController is null)
        {
            _acrylicController = new DesktopAcrylicController();
            _acrylicController.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
            _acrylicController.SetSystemBackdropConfiguration(_configurationSource);
        }

        _acrylicController.TintColor = baseColor;
        _acrylicController.FallbackColor = baseColor;
        _acrylicController.TintOpacity = (float)opacity;
        _acrylicController.LuminosityOpacity = (float)Math.Clamp(opacity + 0.05, 0.2, 1.0);
        _acrylicController.Kind = AppearanceConfig.NormalizeAcrylicBlur(appearance.AcrylicBlur) == "soft"
            ? DesktopAcrylicKind.Thin
            : DesktopAcrylicKind.Default;
    }

    private bool IsDarkTheme()
    {
        if (Content is FrameworkElement { ActualTheme: ElementTheme.Light })
        {
            return false;
        }

        if (Content is FrameworkElement { ActualTheme: ElementTheme.Dark })
        {
            return true;
        }

        return AppearanceConfig.NormalizeTheme(_configService.Config.Appearance?.Theme) != "light";
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
        _configService.ConfigChanged -= OnConfigChanged;
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

    private const int TileSpacing = OverlayMetrics.TileSpacing;
    private const int GridPadX = OverlayMetrics.GridPadX;
    private const int GridPadTop = OverlayMetrics.GridPadTop;
    private const int GridPadBottom = OverlayMetrics.GridPadBottom;
    private const int OriginKeySize = 26;

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

        var layout = _configService.GetLayout();
        var appearance = _configService.Config.Appearance ?? new AppearanceConfig();
        var tileSize = AppearanceConfig.NormalizeTileSize(appearance.TileSize);
        var cols = Math.Max(1, layout.ColumnCount);
        var rows = Math.Max(1, layout.RowCount);
        var tilesHeight = OverlayMetrics.TilesHeight(rows, tileSize);
        var menuHeight = MenuRow.ActualHeight > 1 ? MenuRow.ActualHeight : 40;
        var menuWidth = MeasureMenuWidth();
        var settingsHeight = 0.0;
        var settingsWidth = 0.0;
        if (_settingsOpen)
        {
            settingsHeight = MeasureNaturalSize(SettingsPane).Height;
            if (settingsHeight <= 1)
            {
                settingsHeight = 280;
            }

            settingsHeight += 8;
            settingsWidth = MeasureNaturalSize(SettingsPane).Width + 24;
            if (settingsWidth <= 25)
            {
                settingsWidth = 420;
            }
        }

        var widthDip = OverlayMetrics.WindowWidth(cols, tileSize, (int)Math.Ceiling(menuWidth), (int)Math.Ceiling(settingsWidth));
        var heightDip = menuHeight + settingsHeight + GridPadTop + tilesHeight + GridPadBottom;

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
        // Keep the floor low so a 2–3 column grid can still hug the tiles.
        width = Math.Clamp(width, 80, work.Width);
        height = Math.Clamp(height, 120, work.Height);

        var x = work.X + (work.Width - width) / 2;
        var y = work.Y + (work.Height - height) / 2;
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private double MeasureMenuWidth()
    {
        const double pad = 16;
        const double buttons = 32 + 4 + 32;
        var menu = MeasureNaturalSize(OverlayMenu).Width;
        if (menu <= 1)
        {
            menu = 168;
        }

        return pad + menu + 12 + buttons + pad;
    }

    private static Size MeasureNaturalSize(FrameworkElement element)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return element.DesiredSize;
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
        var layout = _configService.GetLayout();
        var appearance = _configService.Config.Appearance ?? new AppearanceConfig();
        var itemMap = (grid.Items ?? [])
            .Where(i => !string.IsNullOrWhiteSpace(i.Key) && layout.IsValidKey(i.Key))
            .GroupBy(i => KeyboardLayout.NormalizeKey(i.Key))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var rows = new List<GridRowViewModel>();

        for (var r = 0; r < layout.Rows.Count; r++)
        {
            var tiles = new List<ActionTileViewModel>();
            foreach (var key in layout.Rows[r])
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
                    tile.Icon = await _iconResolver.ResolveAsync(
                        item.Icon,
                        appearance.LucideColor,
                        IsDarkTheme());
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
        PositionOnCursorMonitor();
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
        var appearance = _configService.Config.Appearance ?? new AppearanceConfig();
        var tileSize = AppearanceConfig.NormalizeTileSize(appearance.TileSize);
        var corner = AppearanceConfig.NormalizeCornerRadius(appearance.CornerRadius);

        var wrapper = new Grid
        {
            Width = tileSize,
            Height = tileSize,
        };

        var content = new StackPanel
        {
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(new Image
        {
            Width = tileSize * 0.34,
            Height = tileSize * 0.34,
            Stretch = Stretch.Uniform,
            Source = tile.Icon,
        });
        if (!string.IsNullOrWhiteSpace(tile.Name))
        {
            content.Children.Add(new TextBlock
            {
                Text = tile.Name,
                FontSize = tileSize <= AppearanceConfig.CompactTileSize ? 9 : 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
                TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                MaxLines = 1,
                Width = tileSize - 10,
                Opacity = 0.92,
            });
        }

        var button = new Button
        {
            Width = tileSize,
            Height = tileSize,
            Padding = new Thickness(4, 16, 4, 6),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Tag = tile,
            IsEnabled = true,
            Opacity = tile.IsEmpty ? 0.35 : 1,
            CornerRadius = new CornerRadius(corner),
            Content = content,
        };
        if (!string.IsNullOrWhiteSpace(tile.Name))
        {
            ToolTipService.SetToolTip(button, tile.Name);
        }
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

    private void OnConfigChanged(object? sender, EventArgs e)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => OnConfigChanged(sender, e));
            return;
        }

        _themeService.Apply();
        _iconResolver.ClearCache();
        ApplyBackdrop();
        RebuildMenus();
        SyncSettingsPane();
        if (_isVisible)
        {
            _ = RefreshCurrentGridAsync();
        }
    }

    private void SettingsToggleButton_Click(object sender, RoutedEventArgs e)
    {
        SetSettingsOpen(!_settingsOpen);
    }

    private void CloseOverlayButton_Click(object sender, RoutedEventArgs e) => HideOverlay();

    private void SetSettingsOpen(bool open)
    {
        _settingsOpen = open;
        SettingsPane.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (open)
        {
            SyncSettingsPane();
            BuildOriginPicker();
        }

        PositionOnCursorMonitor();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isVisible)
            {
                PositionOnCursorMonitor();
            }
        });
        TryFocusOverlay();
    }

    private void SizeBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressSettingsEvents || !_settingsOpen)
        {
            return;
        }

        if (double.IsNaN(ColumnsBox.Value) || double.IsNaN(RowsBox.Value))
        {
            return;
        }

        var columns = (int)Math.Round(ColumnsBox.Value);
        var rows = (int)Math.Round(RowsBox.Value);
        var current = _configService.GetLayout();
        if (columns == current.RequestedColumns && rows == current.RequestedRows)
        {
            return;
        }

        _configService.UpdateLayout(current.StartKey, columns, rows);
    }

    private void AppearanceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettingsEvents)
        {
            return;
        }

        var theme = ThemeBox.SelectedIndex switch
        {
            1 => "light",
            2 => "dark",
            _ => "system",
        };
        var tileSize = TileSizeBox.SelectedIndex switch
        {
            0 => AppearanceConfig.CompactTileSize,
            2 => AppearanceConfig.LargeTileSize,
            _ => AppearanceConfig.DefaultTileSize,
        };
        var corner = CornerBox.SelectedIndex switch
        {
            0 => AppearanceConfig.SharpCornerRadius,
            2 => AppearanceConfig.PillCornerRadius,
            _ => AppearanceConfig.RoundedCornerRadius,
        };
        var blur = BlurBox.SelectedIndex == 1 ? "soft" : "standard";
        var appearance = _configService.Config.Appearance ?? new AppearanceConfig();
        if (theme == AppearanceConfig.NormalizeTheme(appearance.Theme)
            && tileSize == AppearanceConfig.NormalizeTileSize(appearance.TileSize)
            && corner == AppearanceConfig.NormalizeCornerRadius(appearance.CornerRadius)
            && blur == AppearanceConfig.NormalizeAcrylicBlur(appearance.AcrylicBlur))
        {
            return;
        }

        _configService.UpdateAppearance(theme, tileSize, corner, acrylicBlur: blur);
    }

    private void AcrylicSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsEvents)
        {
            return;
        }

        _configService.UpdateAppearance(acrylic: AcrylicSwitch.IsOn);
    }

    private void OpacitySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressSettingsEvents)
        {
            return;
        }

        var opacity = AppearanceConfig.NormalizeOpacity((int)Math.Round(OpacitySlider.Value));
        OpacityLabel.Text = $"Opacity  {opacity}%";
        _opacitySaveTimer?.Stop();
        _opacitySaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _opacitySaveTimer.Tick += (_, _) =>
        {
            _opacitySaveTimer.Stop();
            _configService.UpdateAppearance(opacity: opacity);
        };
        _opacitySaveTimer.Start();
    }

    private void BuildLucideColorSwatches(Panel host, string selectedId, Action<string> apply)
    {
        host.Children.Clear();
        foreach (var swatch in LucidePalette.Swatches)
        {
            var captured = swatch;
            var hex = string.IsNullOrEmpty(swatch.Hex)
                ? (IsDarkTheme() ? "#F2F2F2" : "#2B2B2B")
                : swatch.Hex;
            var color = ParseHex(hex);
            var button = new Button
            {
                Width = 18,
                Height = 18,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(selectedId == swatch.Id ? 2 : 1),
                BorderBrush = new SolidColorBrush(
                    selectedId == swatch.Id
                        ? Color.FromArgb(255, 96, 205, 255)
                        : Color.FromArgb(80, 255, 255, 255)),
                Tag = swatch.Id,
            };
            ToolTipService.SetToolTip(button, swatch.Label);
            button.Click += (_, _) => apply(captured.Id);
            host.Children.Add(button);
        }
    }

    private static Color ParseHex(string hex)
    {
        if (!LucidePalette.TryParseRgb(hex, out var r, out var g, out var b))
        {
            return Color.FromArgb(255, 242, 242, 242);
        }

        return Color.FromArgb(255, r, g, b);
    }

    private void RebuildMenus()
    {
        OverlayMenu.Items.Clear();
        var layout = _configService.GetLayout();
        var appearance = _configService.Config.Appearance ?? new AppearanceConfig();

        var gridMenu = new MenuBarItem { Title = "Grid" };
        var sizeMenu = new MenuFlyoutSubItem { Text = "Size" };
        foreach (var preset in KeyboardLayout.SizePresets)
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = preset.Label,
                GroupName = "GridSize",
                IsChecked = layout.RequestedRows == preset.Rows
                    && layout.RequestedColumns == preset.Columns,
            };
            var captured = preset;
            item.Click += (_, _) =>
                _configService.UpdateLayout(layout.StartKey, captured.Columns, captured.Rows);
            sizeMenu.Items.Add(item);
        }

        sizeMenu.Items.Add(new MenuFlyoutSeparator());
        var customSize = new MenuFlyoutItem { Text = "Custom…" };
        customSize.Click += (_, _) => SetSettingsOpen(true);
        sizeMenu.Items.Add(customSize);
        gridMenu.Items.Add(sizeMenu);

        var startMenu = new MenuFlyoutSubItem { Text = "Start at" };
        foreach (var origin in KeyboardLayout.CommonOrigins)
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = OriginLabel(origin),
                GroupName = "GridOrigin",
                IsChecked = string.Equals(layout.StartKey, origin, StringComparison.OrdinalIgnoreCase),
            };
            var captured = origin;
            item.Click += (_, _) =>
                _configService.UpdateLayout(captured, layout.RequestedColumns, layout.RequestedRows);
            startMenu.Items.Add(item);
        }

        startMenu.Items.Add(new MenuFlyoutSeparator());
        var chooseKey = new MenuFlyoutItem { Text = "Choose key…" };
        chooseKey.Click += (_, _) => SetSettingsOpen(true);
        startMenu.Items.Add(chooseKey);
        gridMenu.Items.Add(startMenu);

        OverlayMenu.Items.Add(gridMenu);

        var appearanceMenu = new MenuBarItem { Title = "Appearance" };
        appearanceMenu.Items.Add(BuildThemeMenu(appearance));
        appearanceMenu.Items.Add(BuildTileSizeMenu(appearance));
        appearanceMenu.Items.Add(BuildCornerMenu(appearance));
        appearanceMenu.Items.Add(BuildAcrylicMenu(appearance));
        appearanceMenu.Items.Add(BuildLucideColorMenu(appearance));
        appearanceMenu.Items.Add(new MenuFlyoutSeparator());
        var more = new MenuFlyoutItem { Text = "More settings…" };
        more.Click += (_, _) => SetSettingsOpen(true);
        appearanceMenu.Items.Add(more);
        OverlayMenu.Items.Add(appearanceMenu);
    }

    private MenuFlyoutSubItem BuildThemeMenu(AppearanceConfig appearance)
    {
        var menu = new MenuFlyoutSubItem { Text = "Theme" };
        AddAppearanceRadio(menu, "System", "ThemeChoice", appearance.Theme == "system", () =>
            _configService.UpdateAppearance(theme: "system"));
        AddAppearanceRadio(menu, "Light", "ThemeChoice", appearance.Theme == "light", () =>
            _configService.UpdateAppearance(theme: "light"));
        AddAppearanceRadio(menu, "Dark", "ThemeChoice", appearance.Theme == "dark", () =>
            _configService.UpdateAppearance(theme: "dark"));
        return menu;
    }

    private MenuFlyoutSubItem BuildTileSizeMenu(AppearanceConfig appearance)
    {
        var menu = new MenuFlyoutSubItem { Text = "Tiles" };
        AddAppearanceRadio(
            menu,
            "Compact",
            "TileSizeChoice",
            appearance.TileSize == AppearanceConfig.CompactTileSize,
            () => _configService.UpdateAppearance(tileSize: AppearanceConfig.CompactTileSize));
        AddAppearanceRadio(
            menu,
            "Default",
            "TileSizeChoice",
            appearance.TileSize == AppearanceConfig.DefaultTileSize,
            () => _configService.UpdateAppearance(tileSize: AppearanceConfig.DefaultTileSize));
        AddAppearanceRadio(
            menu,
            "Large",
            "TileSizeChoice",
            appearance.TileSize == AppearanceConfig.LargeTileSize,
            () => _configService.UpdateAppearance(tileSize: AppearanceConfig.LargeTileSize));
        return menu;
    }

    private MenuFlyoutSubItem BuildCornerMenu(AppearanceConfig appearance)
    {
        var menu = new MenuFlyoutSubItem { Text = "Corners" };
        AddAppearanceRadio(
            menu,
            "Sharp",
            "CornerChoice",
            appearance.CornerRadius == AppearanceConfig.SharpCornerRadius,
            () => _configService.UpdateAppearance(cornerRadius: AppearanceConfig.SharpCornerRadius));
        AddAppearanceRadio(
            menu,
            "Rounded",
            "CornerChoice",
            appearance.CornerRadius == AppearanceConfig.RoundedCornerRadius,
            () => _configService.UpdateAppearance(cornerRadius: AppearanceConfig.RoundedCornerRadius));
        AddAppearanceRadio(
            menu,
            "Pill",
            "CornerChoice",
            appearance.CornerRadius == AppearanceConfig.PillCornerRadius,
            () => _configService.UpdateAppearance(cornerRadius: AppearanceConfig.PillCornerRadius));
        return menu;
    }

    private MenuFlyoutSubItem BuildAcrylicMenu(AppearanceConfig appearance)
    {
        var menu = new MenuFlyoutSubItem { Text = "Acrylic" };
        var enabled = new ToggleMenuFlyoutItem
        {
            Text = "Use acrylic",
            IsChecked = appearance.Acrylic,
        };
        enabled.Click += (_, _) => _configService.UpdateAppearance(acrylic: enabled.IsChecked);
        menu.Items.Add(enabled);
        menu.Items.Add(new MenuFlyoutSeparator());
        AddAppearanceRadio(menu, "Opacity 50%", "AcrylicOpacity", appearance.Opacity <= 55, () =>
            _configService.UpdateAppearance(opacity: 50));
        AddAppearanceRadio(menu, "Opacity 80%", "AcrylicOpacity", appearance.Opacity is > 55 and < 90, () =>
            _configService.UpdateAppearance(opacity: 80));
        AddAppearanceRadio(menu, "Opacity 100%", "AcrylicOpacity", appearance.Opacity >= 90, () =>
            _configService.UpdateAppearance(opacity: 100));
        menu.Items.Add(new MenuFlyoutSeparator());
        AddAppearanceRadio(
            menu,
            "Standard blur",
            "AcrylicBlur",
            AppearanceConfig.NormalizeAcrylicBlur(appearance.AcrylicBlur) != "soft",
            () => _configService.UpdateAppearance(acrylicBlur: "standard"));
        AddAppearanceRadio(
            menu,
            "Soft blur",
            "AcrylicBlur",
            AppearanceConfig.NormalizeAcrylicBlur(appearance.AcrylicBlur) == "soft",
            () => _configService.UpdateAppearance(acrylicBlur: "soft"));
        return menu;
    }

    private MenuFlyoutSubItem BuildLucideColorMenu(AppearanceConfig appearance)
    {
        var menu = new MenuFlyoutSubItem { Text = "Lucide color" };
        var selected = LucidePalette.Normalize(appearance.LucideColor);
        foreach (var swatch in LucidePalette.Swatches)
        {
            var captured = swatch;
            AddAppearanceRadio(
                menu,
                swatch.Label,
                "LucideColorChoice",
                selected == swatch.Id,
                () => _configService.UpdateAppearance(lucideColor: captured.Id));
        }

        return menu;
    }

    private static void AddAppearanceRadio(
        MenuFlyoutSubItem menu,
        string text,
        string group,
        bool isChecked,
        Action apply)
    {
        var item = new RadioMenuFlyoutItem
        {
            Text = text,
            GroupName = group,
            IsChecked = isChecked,
        };
        item.Click += (_, _) => apply();
        menu.Items.Add(item);
    }

    private static string OriginLabel(string key) => key switch
    {
        "1" => "1  ·  number row",
        "Q" => "Q  ·  top letters",
        "A" => "A  ·  home row",
        "Z" => "Z  ·  bottom row",
        _ => key,
    };

    private void SyncSettingsPane()
    {
        _suppressSettingsEvents = true;
        try
        {
            var layout = _configService.GetLayout();
            var appearance = _configService.Config.Appearance ?? new AppearanceConfig();
            ColumnsBox.Value = layout.RequestedColumns;
            RowsBox.Value = layout.RequestedRows;
            ThemeBox.SelectedIndex = appearance.Theme switch
            {
                "light" => 1,
                "dark" => 2,
                _ => 0,
            };
            TileSizeBox.SelectedIndex = appearance.TileSize switch
            {
                AppearanceConfig.CompactTileSize => 0,
                AppearanceConfig.LargeTileSize => 2,
                _ => 1,
            };
            CornerBox.SelectedIndex = appearance.CornerRadius switch
            {
                AppearanceConfig.SharpCornerRadius => 0,
                AppearanceConfig.PillCornerRadius => 2,
                _ => 1,
            };
            AcrylicSwitch.IsOn = appearance.Acrylic;
            OpacitySlider.Value = AppearanceConfig.NormalizeOpacity(appearance.Opacity);
            OpacityLabel.Text = $"Opacity  {AppearanceConfig.NormalizeOpacity(appearance.Opacity)}%";
            BlurBox.IsEnabled = appearance.Acrylic;
            BlurBox.SelectedIndex = AppearanceConfig.NormalizeAcrylicBlur(appearance.AcrylicBlur) == "soft" ? 1 : 0;
            BuildLucideColorSwatches(
                LucideDefaultColorHost,
                LucidePalette.Normalize(appearance.LucideColor),
                id => _configService.UpdateAppearance(lucideColor: id));
            OriginHint.Text = $"Top-left is {layout.StartKey}. Highlight shows the {layout.RequestedColumns}×{layout.RequestedRows} slice.";
            if (_settingsOpen)
            {
                BuildOriginPicker();
            }
        }
        finally
        {
            _suppressSettingsEvents = false;
        }
    }

    private void BuildOriginPicker()
    {
        OriginPickerHost.Children.Clear();
        var layout = _configService.GetLayout();
        for (var r = 0; r < KeyboardLayout.PhysicalRows.Length; r++)
        {
            var rowPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 3,
            };
            var physicalRow = KeyboardLayout.PhysicalRows[r];
            for (var c = 0; c < physicalRow.Length; c++)
            {
                var key = physicalRow[c];
                var inSlice = layout.ContainsPhysicalCell(r, c);
                var isOrigin = string.Equals(key, layout.StartKey, StringComparison.OrdinalIgnoreCase);
                var button = new Button
                {
                    Width = OriginKeySize,
                    Height = OriginKeySize,
                    Padding = new Thickness(0),
                    Content = key,
                    Tag = key,
                    FontSize = 11,
                    CornerRadius = new CornerRadius(4),
                    Opacity = inSlice ? 1 : 0.45,
                };
                if (isOrigin
                    && Application.Current.Resources.TryGetValue("AccentButtonStyle", out var styleObj)
                    && styleObj is Style accentStyle)
                {
                    button.Style = accentStyle;
                }

                button.Click += OriginKey_Click;
                rowPanel.Children.Add(button);
            }

            OriginPickerHost.Children.Add(rowPanel);
        }
    }

    private void OriginKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key })
        {
            return;
        }

        var layout = _configService.GetLayout();
        _configService.UpdateLayout(key, layout.RequestedColumns, layout.RequestedRows);
    }

    private bool HasOpenPopup()
    {
        if (Content is not FrameworkElement { XamlRoot: not null } root)
        {
            return false;
        }

        try
        {
            return VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? VirtualKeyToGridKey(VirtualKey key)
    {
        if (key is >= VirtualKey.A and <= VirtualKey.Z)
        {
            return ((char)('A' + (key - VirtualKey.A))).ToString();
        }

        if (key is >= VirtualKey.Number0 and <= VirtualKey.Number9)
        {
            return ((char)('0' + (key - VirtualKey.Number0))).ToString();
        }

        if (key is >= VirtualKey.NumberPad0 and <= VirtualKey.NumberPad9)
        {
            return ((char)('0' + (key - VirtualKey.NumberPad0))).ToString();
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
