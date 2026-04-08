using WinVDeskEssential.Models;
using WinVDeskEssential.Services;
using WinVDeskEssential.Services.Interop;
using WinVDeskEssential.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using WindowsDesktop;

namespace WinVDeskEssential.Views;

public partial class SwitcherPanel : Window
{
    private readonly SwitcherPanelViewModel _viewModel;
    private readonly MonitorInfo _monitor;
    private bool _isDragging;
    private Point _dragStartMouse;
    private double _dragStartLeft, _dragStartTop;

    private const double TopEdgeThreshold = 60;

    public event Action<DockPosition>? DockPositionChanged;

    public SwitcherPanel(SwitcherPanelViewModel viewModel, MonitorInfo monitor)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        _monitor = monitor;

        // Default horizontal layout at top
        _viewModel.SetDockPosition(DockPosition.Top);

        // Always visible, never hide on deactivate
        SizeToContent = SizeToContent.WidthAndHeight;

        // Hide from Alt+Tab / Win+Tab by setting WS_EX_TOOLWINDOW
        SourceInitialized += (_, _) => HideFromTaskSwitcher();
        Loaded += (_, _) => PinToAllDesktops();
    }

    private void HideFromTaskSwitcher()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
        exStyle &= ~NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
    }

    /// <summary>
    /// Position on the primary monitor's left edge, centered vertically.
    /// Called once after first show.
    /// </summary>
    public void SetInitialPosition()
    {
        var bounds = _monitor.Bounds;
        Left = bounds.Left + (bounds.Width - ActualWidth) / 2;
        Top = bounds.Top;
    }

    // --- Drag support ---

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        // Only drag from the background, not from item clicks
        if (e.OriginalSource is Border b && b.Tag is DesktopSlotViewModel)
            return;

        _isDragging = true;
        _dragStartMouse = PointToScreen(e.GetPosition(this));
        _dragStartLeft = Left;
        _dragStartTop = Top;
        CaptureMouse();

        MouseMove += OnDragMove;
        MouseLeftButtonUp += OnDragEnd;
    }

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var current = PointToScreen(e.GetPosition(this));
        Left = _dragStartLeft + (current.X - _dragStartMouse.X);
        Top = _dragStartTop + (current.Y - _dragStartMouse.Y);
    }

    private void OnDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ReleaseMouseCapture();
        MouseMove -= OnDragMove;
        MouseLeftButtonUp -= OnDragEnd;

        // Auto-switch layout based on position
        var bounds = _monitor.Bounds;
        bool nearTop = Top <= bounds.Top + TopEdgeThreshold;

        if (nearTop)
        {
            _viewModel.SetDockPosition(DockPosition.Top);
            Dispatcher.BeginInvoke(() =>
            {
                UpdateLayout();
                Left = bounds.Left + (bounds.Width - ActualWidth) / 2;
                Top = bounds.Top;
            });
            DockPositionChanged?.Invoke(DockPosition.Top);
        }
        else
        {
            _viewModel.SetDockPosition(DockPosition.Left);
            // After layout switch, ensure window stays within screen bounds
            Dispatcher.BeginInvoke(() =>
            {
                UpdateLayout();
                // Clamp to screen
                if (Left < bounds.Left) Left = bounds.Left;
                if (Left + ActualWidth > bounds.Right) Left = bounds.Right - ActualWidth;
                if (Top < bounds.Top) Top = bounds.Top;
                if (Top + ActualHeight > bounds.Bottom) Top = bounds.Bottom - ActualHeight;
            });
            DockPositionChanged?.Invoke(DockPosition.Left);
        }
    }

    // --- Item click to switch desktop ---

    private void OnItemClick(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging) return;
        if (sender is Border b && b.Tag is DesktopSlotViewModel vm)
        {
            _viewModel.SwitchToDesktopCommand.Execute(vm);
        }
    }

    // --- Pin to all desktops ---

    public void PinToAllDesktops()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            if (!VirtualDesktop.IsPinnedWindow(hwnd))
            {
                VirtualDesktop.PinWindow(hwnd);
                Logger.Log($"[Panel] PinWindow OK hwnd=0x{hwnd:X}");
            }
        }
        catch (Exception ex) { Logger.Log($"[Panel] PinWindow failed: {ex.Message}"); }
    }

    // --- Public API ---

    public void RefreshSelection()
    {
        _viewModel.RefreshDesktops();
    }
}
