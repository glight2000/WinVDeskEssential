namespace WinVDeskEssential.Models;

public class MonitorInfo
{
    public IntPtr Handle { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public System.Windows.Rect Bounds { get; set; }
    public System.Windows.Rect WorkArea { get; set; }
    public bool IsPrimary { get; set; }
}
