using System.ComponentModel.DataAnnotations;

namespace VDesk.Data;

public class DesktopConfigEntity
{
    [Key]
    public int Id { get; set; }
    public string MonitorDeviceId { get; set; } = string.Empty;
    public Guid SystemDesktopId { get; set; }
    public int SortOrder { get; set; }
    public string Name { get; set; } = "Desktop";

    // Background
    public int BackgroundType { get; set; } // 0=Solid, 1=Gradient, 2=Image
    public string PrimaryColor { get; set; } = "#FF483D8B"; // DarkSlateBlue
    public string SecondaryColor { get; set; } = "#FF191970"; // MidnightBlue
    public double GradientAngle { get; set; } = 135;
    public string? ImagePath { get; set; }
    public int ImageFillMode { get; set; } = 1; // Fill

    // Watermark
    public bool WatermarkEnabled { get; set; } = false;
    public int WatermarkPosition { get; set; } = 4; // BottomLeft
    public string? WatermarkCustomText { get; set; }
    public double WatermarkFontSize { get; set; } = 24;
    public string WatermarkFontFamily { get; set; } = "Microsoft YaHei";
    public string WatermarkTextColor { get; set; } = "#CCFFFFFF";
    public double WatermarkOpacity { get; set; } = 0.15;
    public double WatermarkMargin { get; set; } = 40;

    // Border
    public bool BorderEnabled { get; set; } = false;
    public string BorderColor { get; set; } = "#FF1E90FF"; // DodgerBlue
    public double BorderWidth { get; set; } = 16;
    public bool BorderShowTop { get; set; } = true;
    public bool BorderShowBottom { get; set; } = true;
    public bool BorderShowLeft { get; set; } = true;
    public bool BorderShowRight { get; set; } = true;
}
