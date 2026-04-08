using VDesk.Models;
using VDesk.Services;
using VDesk.Services.Interop;
using VDesk.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using WindowsDesktop;

namespace VDesk.Views;

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

        // Default vertical layout, centered on left edge
        _viewModel.SetDockPosition(DockPosition.Left);

        // Always visible, never hide on deactivate
        SizeToContent = SizeToContent.WidthAndHeight;

        Loaded += (_, _) => PinToAllDesktops();
    }

    /// <summary>
    /// Position on the primary monitor's left edge, centered vertically.
    /// Called once after first show.
    /// </summary>
    public void SetInitialPosition()
    {
        var bounds = _monitor.Bounds;
        Left = bounds.Left;
        Top = bounds.Top + (bounds.Height - ActualHeight) / 2;
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
                Left = bounds.Left + (bounds.Width - ActualWidth) / 2;
                Top = bounds.Top;
            });
            DockPositionChanged?.Invoke(DockPosition.Top);
        }
        else
        {
            _viewModel.SetDockPosition(DockPosition.Left);
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
