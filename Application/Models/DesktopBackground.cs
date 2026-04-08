using System.Windows.Media;

namespace WinVDeskEssential.Models;

public enum BackgroundType { SolidColor, Gradient, Image }
public enum FillMode { Stretch, Fill, Fit, Center, Tile }

public class DesktopBackground
{
    public BackgroundType Type { get; set; } = BackgroundType.SolidColor;
    public Color PrimaryColor { get; set; } = Colors.DarkSlateBlue;
    public Color SecondaryColor { get; set; } = Colors.MidnightBlue;
    public double GradientAngle { get; set; } = 135;
    public string? ImagePath { get; set; }
    public FillMode ImageFillMode { get; set; } = FillMode.Fill;
}
