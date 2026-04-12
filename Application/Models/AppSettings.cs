namespace WinVDeskEssential.Models;

public enum DockPosition { Top, Bottom, Left, Right }

public class AppSettings
{
    public DockPosition PanelDockPosition { get; set; } = DockPosition.Top;
    public double PanelLeft { get; set; } = double.NaN;
    public double PanelTop { get; set; } = double.NaN;
    public bool PanelPinned { get; set; } = false;
    public bool WatermarkAlwaysOn { get; set; } = true;
    public CornerPosition WatermarkPosition { get; set; } = CornerPosition.BottomRight;
    public double WatermarkFontSize { get; set; } = 24;
    public double WatermarkOpacity { get; set; } = 0.35;
    public double WatermarkMargin { get; set; } = 40;
    public bool AltDragEnabled { get; set; } = true;
    /// <summary>
    /// Process names (without .exe) that should be auto-pinned as quick windows.
    /// The panel scans for running processes with these names and adds their
    /// main window. Entries are removed if the process exits, re-added if it relaunches.
    /// </summary>
    public List<string> AutoQuickWindowProcessNames { get; set; } = new();
    /// <summary>
    /// Per-monitor pinned windows. Key = monitor DeviceId (e.g. "\\.\DISPLAY1"),
    /// Value = list of (ProcessName, TitleKey) configs.
    /// </summary>
    public Dictionary<string, List<MonitorPinConfig>> MonitorPinnedWindows { get; set; } = new();
    public bool StartWithWindows { get; set; } = false;
    public bool SystemHotkeysOverridden { get; set; } = false;
    public List<string> ExcludedProcessNames { get; set; } = new()
    {
        "explorer", "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost",
        "SystemSettings", "TextInputHost", "LockApp", "LogiOverlay", "WinVDeskEssential"
    };
    public List<string> ExcludedWindowClasses { get; set; } = new()
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW",
        "Windows.UI.Core.CoreWindow", "ApplicationFrameWindow"
    };
}
