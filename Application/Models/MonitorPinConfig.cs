namespace WinVDeskEssential.Models;

/// <summary>
/// Persistent identity for a pinned window.
///   - ProcessName: matches any running process with this name (case-insensitive)
///   - TitleKey: first 30 chars of the window title at pin time; empty means "any main window"
/// On rebind, the scanner finds a matching top-level window by process + title-contains.
/// </summary>
public class MonitorPinConfig
{
    public string ProcessName { get; set; } = string.Empty;
    public string TitleKey { get; set; } = string.Empty;
}
