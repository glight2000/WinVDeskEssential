namespace WinVDeskEssential.Models;

public class QuickWindow
{
    public IntPtr Hwnd { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
}
