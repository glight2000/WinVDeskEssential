namespace WinVDeskEssential.Models;

/// <summary>
/// Runtime binding of a MonitorPinConfig to an actual live window.
/// Hwnd may be IntPtr.Zero when the target process isn't running;
/// the scanner will rebind when the app launches.
/// </summary>
public class PinnedWindow
{
    public string MonitorDeviceId { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string TitleKey { get; set; } = string.Empty;
    public string DisplayTitle { get; set; } = string.Empty;
    public IntPtr Hwnd { get; set; }
    public bool IsBound => Hwnd != IntPtr.Zero;
}
