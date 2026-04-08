using VDesk.Models;
using System.Windows.Media;

namespace VDesk.ViewModels;

public class DesktopSlotViewModel : ViewModelBase
{
    private readonly DesktopSlot _slot;
    private bool _isActive;

    public DesktopSlotViewModel(DesktopSlot slot, int index, bool isActive)
    {
        _slot = slot;
        Index = index;
        _isActive = isActive;
    }

    public DesktopSlot Slot => _slot;
    public int Index { get; }
    public Guid SystemDesktopId => _slot.SystemDesktopId;

    public string Name
    {
        get => _slot.Name;
        set
        {
            _slot.Name = value;
            OnPropertyChanged();
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public Brush BackgroundBrush
    {
        get
        {
            var bg = _slot.Background;
            return bg.Type switch
            {
                BackgroundType.SolidColor => new SolidColorBrush(bg.PrimaryColor),
                BackgroundType.Gradient => new LinearGradientBrush(bg.PrimaryColor, bg.SecondaryColor, bg.GradientAngle),
                BackgroundType.Image => new SolidColorBrush(bg.PrimaryColor), // Simplified for list item
                _ => new SolidColorBrush(Colors.DarkSlateBlue)
            };
        }
    }

    public Brush ActiveBorderBrush =>
        new SolidColorBrush(_slot.Border.IsEnabled ? _slot.Border.BorderColor : Colors.DodgerBlue);

    public Color LabelColor => _slot.Border.BorderColor;
}
