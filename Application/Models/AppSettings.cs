namespace WinVDeskEssential.Models;

public enum DockPosition { Top, Bottom, Left, Right }

public class AppSettings
{
    public DockPosition PanelDockPosition { get; set; } = DockPosition.Top;
    public double PanelLeft { get; set; } = double.NaN;
    public double PanelTop { get; set; } = double.NaN;
    public bool PanelPinned { get; set; } = false;
    public bool PanelAnimationEnabled { get; set; } = true;
    public int PanelAnimationDurationMs { get; set; } = 150;
    public bool WatermarkAlwaysOn { get; set; } = true;
    public CornerPosition WatermarkPosition { get; set; } = CornerPosition.BottomRight;
    public double WatermarkFontSize { get; set; } = 24;
    public double WatermarkOpacity { get; set; } = 0.35;
    public double WatermarkMargin { get; set; } = 40;
    public bool AltDragEnabled { get; set; } = true;
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
