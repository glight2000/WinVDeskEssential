using System.Windows.Media;

namespace VDesk.Models;

public class BorderSettings
{
    public bool IsEnabled { get; set; } = false;
    public Color BorderColor { get; set; } = Colors.DodgerBlue;
    public double Width { get; set; } = 16;
    public bool ShowTop { get; set; } = true;
    public bool ShowBottom { get; set; } = true;
    public bool ShowLeft { get; set; } = true;
    public bool ShowRight { get; set; } = true;
}
