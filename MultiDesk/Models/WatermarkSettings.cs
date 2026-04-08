using System.Windows;
using System.Windows.Media;

namespace VDesk.Models;

[Flags]
public enum CornerPosition
{
    None = 0, TopLeft = 1, TopRight = 2, BottomLeft = 4, BottomRight = 8
}

public class WatermarkSettings
{
    public bool IsEnabled { get; set; } = false;
    public CornerPosition Position { get; set; } = CornerPosition.BottomLeft;
    public string? CustomText { get; set; }
    public double FontSize { get; set; } = 24;
    public string FontFamily { get; set; } = "Microsoft YaHei";
    public Color TextColor { get; set; } = Colors.White;
    public double Opacity { get; set; } = 0.35;
    public double Margin { get; set; } = 40;
}
