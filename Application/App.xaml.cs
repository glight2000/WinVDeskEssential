using Hardcodet.Wpf.TaskbarNotification;
using WinVDeskEssential.Services;
using WinVDeskEssential.Services.Interop;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace WinVDeskEssential;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private AppOrchestrator? _orchestrator;
    private Window? _hiddenWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global exception handlers to prevent silent crashes
        DispatcherUnhandledException += (_, args) =>
        {
            Services.Logger.Log($"[CRASH] Unhandled UI exception: {args.Exception}");
            MessageBox.Show($"WinVDeskEssential error:\n{args.Exception.Message}", "WinVDeskEssential", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            Services.Logger.Log($"[CRASH] Fatal exception: {ex}");
        };

        // Prevent multiple instances
        var mutex = new System.Threading.Mutex(true, "WinVDeskEssential_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("WinVDeskEssential is already running.", "WinVDeskEssential", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        GC.KeepAlive(mutex);

        // Create hidden window for hotkey message handling
        _hiddenWindow = new Window
        {
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Visibility = Visibility.Hidden,
        };
        _hiddenWindow.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(_hiddenWindow).Handle;
            var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
            exStyle &= ~NativeMethods.WS_EX_APPWINDOW;
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
        };
        _hiddenWindow.Show();
        _hiddenWindow.Hide();

        // Initialize services
        _orchestrator = new AppOrchestrator();
        try
        {
            _orchestrator.Initialize(_hiddenWindow);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize WinVDeskEssential:\n{ex.Message}",
                "WinVDeskEssential Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // Setup tray icon
        SetupTrayIcon();

        // Defer keyboard hook installation to AFTER the WPF message loop is running.
        // LL keyboard hooks require an active message pump on the installing thread.
        // If installed too early (before Application.Run), Windows may timeout and remove the hook.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            _orchestrator.InstallKeyboardHook();
        });
    }

    private void SetupTrayIcon()
    {
        var contextMenu = new ContextMenu();

        var toggleItem = new MenuItem { Header = "Toggle Panel" };
        toggleItem.Click += (_, _) => _orchestrator?.TogglePanelForCurrentMonitor();
        contextMenu.Items.Add(toggleItem);

        // Watermark submenu
        var watermarkMenu = new MenuItem { Header = "Watermark" };

        var wmEnabled = new MenuItem { Header = "Show", IsCheckable = true, IsChecked = true };
        wmEnabled.Click += (_, _) => _orchestrator?.SetWatermarkEnabled(wmEnabled.IsChecked);
        watermarkMenu.Items.Add(wmEnabled);

        watermarkMenu.Items.Add(new Separator());

        // Position
        var wmPositions = new[] {
            ("Bottom Right", Models.CornerPosition.BottomRight),
            ("Bottom Left", Models.CornerPosition.BottomLeft),
            ("Top Right", Models.CornerPosition.TopRight),
            ("Top Left", Models.CornerPosition.TopLeft),
        };
        foreach (var (label, pos) in wmPositions)
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = pos == Models.CornerPosition.BottomRight };
            var capturedPos = pos;
            item.Click += (_, _) =>
            {
                foreach (MenuItem mi in watermarkMenu.Items)
                    if (mi.IsCheckable && mi != wmEnabled) mi.IsChecked = false;
                item.IsChecked = true;
                _orchestrator?.SetWatermarkPosition(capturedPos);
            };
            watermarkMenu.Items.Add(item);
        }

        watermarkMenu.Items.Add(new Separator());

        // Opacity
        var opacityMenu = new MenuItem { Header = "Opacity" };
        foreach (var op in new[] { 0.05, 0.10, 0.15, 0.25, 0.40, 0.60 })
        {
            var item = new MenuItem { Header = $"{(int)(op * 100)}%", IsCheckable = true, IsChecked = Math.Abs(op - 0.15) < 0.01 };
            var capturedOp = op;
            item.Click += (_, _) =>
            {
                foreach (MenuItem mi in opacityMenu.Items) mi.IsChecked = false;
                item.IsChecked = true;
                _orchestrator?.SetWatermarkOpacity(capturedOp);
            };
            opacityMenu.Items.Add(item);
        }
        watermarkMenu.Items.Add(opacityMenu);

        // Font size
        var sizeMenu = new MenuItem { Header = "Size" };
        foreach (var sz in new[] { 16.0, 20.0, 24.0, 32.0, 48.0, 64.0 })
        {
            var item = new MenuItem { Header = $"{(int)sz}px", IsCheckable = true, IsChecked = Math.Abs(sz - 24) < 0.1 };
            var capturedSz = sz;
            item.Click += (_, _) =>
            {
                foreach (MenuItem mi in sizeMenu.Items) mi.IsChecked = false;
                item.IsChecked = true;
                _orchestrator?.SetWatermarkFontSize(capturedSz);
            };
            sizeMenu.Items.Add(item);
        }
        watermarkMenu.Items.Add(sizeMenu);

        // Margin
        var marginMenu = new MenuItem { Header = "Margin" };
        foreach (var mg in new[] { 10.0, 20.0, 40.0, 60.0, 100.0 })
        {
            var item = new MenuItem { Header = $"{(int)mg}px", IsCheckable = true, IsChecked = Math.Abs(mg - 40) < 0.1 };
            var capturedMg = mg;
            item.Click += (_, _) =>
            {
                foreach (MenuItem mi in marginMenu.Items) mi.IsChecked = false;
                item.IsChecked = true;
                _orchestrator?.SetWatermarkMargin(capturedMg);
            };
            marginMenu.Items.Add(item);
        }
        watermarkMenu.Items.Add(marginMenu);

        contextMenu.Items.Add(watermarkMenu);

        contextMenu.Items.Add(new Separator());

        // AltDrag toggle
        var altDragItem = new MenuItem { Header = "Alt+Drag (Move/Resize)", IsCheckable = true, IsChecked = true };
        altDragItem.Click += (_, _) => _orchestrator?.SetAltDragEnabled(altDragItem.IsChecked);
        contextMenu.Items.Add(altDragItem);

        contextMenu.Items.Add(new Separator());

        var settingsItem = new MenuItem { Header = "Settings" };
        settingsItem.Click += (_, _) => _orchestrator?.ShowSettingsWindow();
        contextMenu.Items.Add(settingsItem);

        contextMenu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => Shutdown();
        contextMenu.Items.Add(exitItem);

        _trayIcon = new TaskbarIcon
        {
            Icon = CreateDefaultIcon(),
            ToolTipText = "WinVDeskEssential - Virtual Desktop Manager",
            ContextMenu = contextMenu,
        };

        _trayIcon.TrayMouseDoubleClick += (_, _) => _orchestrator?.TogglePanelForCurrentMonitor();
    }

    private static Icon CreateDefaultIcon()
    {
        // Programmatically create a simple 16x16 icon with "M" letter
        var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(59, 130, 246)); // Blue
            using var font = new Font("Segoe UI", 9, System.Drawing.FontStyle.Bold);
            using var brush = new SolidBrush(Color.White);
            g.DrawString("M", font, brush, 0, 1);
        }
        var hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _orchestrator?.Dispose();
        _trayIcon?.Dispose();
        _hiddenWindow?.Close();
        base.OnExit(e);
    }
}
