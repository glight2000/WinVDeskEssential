namespace WinVDeskEssential.Models;

/// <summary>
/// A quick-access window binding. Driven by a persistent process name in settings;
/// the QuickWindowService scanner rebinds Hwnd to whichever instance of that process
/// is currently running.
/// </summary>
public class QuickWindow
{
    public IntPtr Hwnd { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
}
