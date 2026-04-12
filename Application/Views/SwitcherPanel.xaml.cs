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
    /// Position at the top of the primary monitor, horizontally centered.
    /// Only called when no saved position exists.
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
        _isDragging = true;
        _dragStartMouse = PointToScreen(e.GetPosition(this));
        _dragStartLeft = Left;
        _dragStartTop = Top;
        CaptureMouse();

        MouseMove += OnDragMove;
        MouseLeftButtonUp += OnDragEnd;
        e.Handled = true;
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

        // Decide dock orientation based on proximity to top edge.
        // Only the orientation changes — position stays where the user dropped it,
        // clamped to screen bounds.
        var bounds = _monitor.Bounds;
        bool nearTop = Top <= bounds.Top + TopEdgeThreshold;
        var newDock = nearTop ? DockPosition.Top : DockPosition.Left;
        _viewModel.SetDockPosition(newDock);

        // Re-measure after orientation change, then clamp.
        Dispatcher.BeginInvoke(() =>
        {
            UpdateLayout();
            ClampToScreen();
        });

        DockPositionChanged?.Invoke(newDock);
    }

    private void ClampToScreen()
    {
        var bounds = _monitor.Bounds;
        if (Left < bounds.Left) Left = bounds.Left;
        if (Top < bounds.Top) Top = bounds.Top;
        if (Left + ActualWidth > bounds.Right) Left = bounds.Right - ActualWidth;
        if (Top + ActualHeight > bounds.Bottom) Top = bounds.Bottom - ActualHeight;
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

    // --- Quick window click handlers ---

    private void OnQuickWindowClick(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging) return;
        if (sender is Border b && b.Tag is Models.QuickWindow qw)
        {
            _viewModel.ActivateQuickWindowCommand.Execute(qw);
        }
    }

    private void OnQuickWindowRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border b && b.Tag is Models.QuickWindow qw)
        {
            _viewModel.RemoveQuickWindowCommand.Execute(qw);
        }
    }

    private void OnPickerClick(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging) return;
        _viewModel.PickQuickWindowCommand.Execute(null);
    }

    // --- Expand area handlers ---

    private void OnToggleExpand(object sender, MouseButtonEventArgs e)
    {
        _viewModel.ToggleExpandCommand.Execute(null);
    }

    private void OnMonitorClick(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging) return;
        if (sender is Border b && b.Tag is ViewModels.MonitorButtonViewModel mb)
        {
            _viewModel.SelectMonitorCommand.Execute(mb);
        }
    }

    private void OnMonitorPinClick(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging) return;
        if (sender is Border b && b.Tag is Models.PinnedWindow pw)
        {
            _viewModel.ActivateMonitorPinCommand.Execute(pw);
        }
    }

    private void OnMonitorPinRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border b && b.Tag is Models.PinnedWindow pw)
        {
            _viewModel.RemoveMonitorPinCommand.Execute(pw);
        }
    }

    private void OnMonitorPinPickerClick(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging) return;
        _viewModel.PickMonitorPinCommand.Execute(null);
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

    public IntPtr GetHwnd()
    {
        return new WindowInteropHelper(this).Handle;
    }
}
