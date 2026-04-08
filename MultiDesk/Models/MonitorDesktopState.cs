namespace VDesk.Models;

public class MonitorDesktopState
{
    public MonitorInfo Monitor { get; set; } = null!;
    public List<DesktopSlot> Desktops { get; set; } = new();
    public int CurrentIndex { get; set; } = 0;

    public DesktopSlot? CurrentDesktop =>
        CurrentIndex >= 0 && CurrentIndex < Desktops.Count ? Desktops[CurrentIndex] : null;
}
