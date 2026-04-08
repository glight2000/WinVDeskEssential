using WinVDeskEssential.Models;
using WinVDeskEssential.Services.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using WindowsDesktop;

namespace WinVDeskEssential.Services.Overlay;

public partial class OverlayWindow : Window
{
    private MonitorInfo _monitor;

    public OverlayWindow(MonitorInfo monitor)
    {
        InitializeComponent();
        _monitor = monitor;
        PositionOnMonitor();

        Loaded += (_, _) => SetClickThrough();
    }

    private void PositionOnMonitor()
    {
        Left = _monitor.Bounds.Left;
        Top = _monitor.Bounds.Top;
        Width = _monitor.Bounds.Width;
        Height = _monitor.Bounds.Height;
    }

    private void SetClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        // Pin to ALL virtual desktops so it survives desktop switches
        try { VirtualDesktop.PinWindow(hwnd); }
        catch (Exception ex) { Logger.Log($"[Overlay] PinWindow failed: {ex.Message}"); }

        Logger.Log($"[Overlay] Click-through + pinned for {_monitor.DeviceId}");
    }

    public void UpdateOverlay(DesktopSlot? desktop)
    {
        if (desktop == null)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        bool wantWatermark = desktop.Watermark.IsEnabled;
        bool wantBorder = desktop.Border.IsEnabled;

        Logger.Log($"[Overlay] UpdateOverlay: watermark={wantWatermark}, border={wantBorder}, name={desktop.Name}");

        if (wantBorder)
            ApplyBorder(desktop.Border);
        else
            ClearBorder();

        if (wantWatermark)
            ApplyWatermark(desktop);
        else
            ClearWatermark();

        if (wantWatermark || wantBorder)
        {
            Visibility = Visibility.Visible;
            if (!IsVisible)
                Show();
        }
        else
        {
            Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyBorder(BorderSettings border)
    {
        var color = border.BorderColor;
        var gradientWidth = border.Width * 1.5;
        var startColor = Color.FromArgb(color.A, color.R, color.G, color.B);
        var endColor = Color.FromArgb(0, color.R, color.G, color.B);

        if (border.ShowTop)
        {
            TopBorder.Height = gradientWidth;
            TopBorder.Background = new LinearGradientBrush(startColor, endColor, 90);
            TopBorder.Visibility = Visibility.Visible;
        }
        else TopBorder.Visibility = Visibility.Collapsed;

        if (border.ShowBottom)
        {
            BottomBorder.Height = gradientWidth;
            BottomBorder.Background = new LinearGradientBrush(endColor, startColor, 90);
            BottomBorder.Visibility = Visibility.Visible;
        }
        else BottomBorder.Visibility = Visibility.Collapsed;

        if (border.ShowLeft)
        {
            LeftBorder.Width = gradientWidth;
            LeftBorder.Background = new LinearGradientBrush(startColor, endColor, 0);
            LeftBorder.Visibility = Visibility.Visible;
        }
        else LeftBorder.Visibility = Visibility.Collapsed;

        if (border.ShowRight)
        {
            RightBorder.Width = gradientWidth;
            RightBorder.Background = new LinearGradientBrush(endColor, startColor, 0);
            RightBorder.Visibility = Visibility.Visible;
        }
        else RightBorder.Visibility = Visibility.Collapsed;
    }

    private void ClearBorder()
    {
        TopBorder.Visibility = Visibility.Collapsed;
        BottomBorder.Visibility = Visibility.Collapsed;
        LeftBorder.Visibility = Visibility.Collapsed;
        RightBorder.Visibility = Visibility.Collapsed;
    }

    private void ApplyWatermark(DesktopSlot desktop)
    {
        var wm = desktop.Watermark;
        var text = wm.CustomText ?? desktop.Name;
        var fontFamily = new FontFamily(wm.FontFamily);
        var brush = new SolidColorBrush(wm.TextColor) { Opacity = wm.Opacity };
        var margin = new Thickness(wm.Margin);

        Logger.Log($"[Overlay] Watermark: text=\"{text}\", pos={wm.Position}, size={wm.FontSize}, opacity={wm.Opacity}");

        void ConfigureBlock(TextBlock block, CornerPosition pos)
        {
            if (wm.Position.HasFlag(pos))
            {
                block.Text = text;
                block.FontSize = wm.FontSize;
                block.FontFamily = fontFamily;
                block.Foreground = brush;
                block.Margin = margin;
                block.Visibility = Visibility.Visible;
            }
            else
            {
                block.Visibility = Visibility.Collapsed;
            }
        }

        ConfigureBlock(WatermarkTopLeft, CornerPosition.TopLeft);
        ConfigureBlock(WatermarkTopRight, CornerPosition.TopRight);
        ConfigureBlock(WatermarkBottomLeft, CornerPosition.BottomLeft);
        ConfigureBlock(WatermarkBottomRight, CornerPosition.BottomRight);
    }

    private void ClearWatermark()
    {
        WatermarkTopLeft.Visibility = Visibility.Collapsed;
        WatermarkTopRight.Visibility = Visibility.Collapsed;
        WatermarkBottomLeft.Visibility = Visibility.Collapsed;
        WatermarkBottomRight.Visibility = Visibility.Collapsed;
    }
}
