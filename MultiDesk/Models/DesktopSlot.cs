namespace VDesk.Models;

public class DesktopSlot
{
    public Guid SystemDesktopId { get; set; }
    public string Name { get; set; } = "Desktop";
    public DesktopBackground Background { get; set; } = new();
    public WatermarkSettings Watermark { get; set; } = new();
    public BorderSettings Border { get; set; } = new();
}
