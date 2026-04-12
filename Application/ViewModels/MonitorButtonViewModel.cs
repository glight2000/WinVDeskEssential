namespace WinVDeskEssential.ViewModels;

public class MonitorButtonViewModel : ViewModelBase
{
    private bool _isSelected;

    public string DeviceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
