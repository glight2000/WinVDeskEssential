using WinVDeskEssential.Models;
using WinVDeskEssential.Services.Desktop;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WinVDeskEssential.ViewModels;

public class SwitcherPanelViewModel : ViewModelBase
{
    private readonly DesktopSwitchEngine _switchEngine;
    private string _currentMonitorId = string.Empty;
    private string _monitorDisplayName = string.Empty;
    private Orientation _panelOrientation = Orientation.Vertical;
    private Transform _itemTextTransform = Transform.Identity;

    public ObservableCollection<DesktopSlotViewModel> Desktops { get; } = new();

    public string MonitorDisplayName
    {
        get => _monitorDisplayName;
        set => SetProperty(ref _monitorDisplayName, value);
    }

    public Orientation PanelOrientation
    {
        get => _panelOrientation;
        set => SetProperty(ref _panelOrientation, value);
    }

    public Transform ItemTextTransform
    {
        get => _itemTextTransform;
        set => SetProperty(ref _itemTextTransform, value);
    }

    public ICommand SwitchToDesktopCommand { get; }
    public ICommand AddDesktopCommand { get; }
    public ICommand RemoveDesktopCommand { get; }
    public ICommand RenameDesktopCommand { get; }
    public ICommand OpenSettingsCommand { get; }

    public event Action? SettingsRequested;

    public SwitcherPanelViewModel(DesktopSwitchEngine switchEngine)
    {
        _switchEngine = switchEngine;

        SwitchToDesktopCommand = new RelayCommand(param =>
        {
            if (param is DesktopSlotViewModel vm)
            {
                _switchEngine.SwitchDesktop(_currentMonitorId, vm.Index);
                RefreshDesktops();
            }
        });

        AddDesktopCommand = new RelayCommand(() =>
        {
            _switchEngine.AddDesktop(_currentMonitorId);
            RefreshDesktops();
        });

        RemoveDesktopCommand = new RelayCommand(param =>
        {
            if (param is DesktopSlotViewModel vm)
            {
                _switchEngine.RemoveDesktop(_currentMonitorId, vm.Index);
                RefreshDesktops();
            }
        }, param =>
        {
            var state = _switchEngine.GetStateForMonitor(_currentMonitorId);
            return state != null && state.Desktops.Count > 1;
        });

        RenameDesktopCommand = new RelayCommand(param => { });

        OpenSettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke());
    }

    public void SetDockPosition(DockPosition dock)
    {
        bool isHorizontal = dock == DockPosition.Top || dock == DockPosition.Bottom;
        PanelOrientation = isHorizontal ? Orientation.Horizontal : Orientation.Vertical;
        // Vertical text when items are laid out horizontally
        ItemTextTransform = isHorizontal ? new RotateTransform(90) : Transform.Identity;
    }

    public void SetMonitor(string monitorDeviceId, string displayName)
    {
        _currentMonitorId = monitorDeviceId;
        MonitorDisplayName = displayName;
        RefreshDesktops();
    }

    public void RefreshDesktops()
    {
        var state = _switchEngine.GetStateForMonitor(_currentMonitorId);
        if (state == null) return;

        Desktops.Clear();
        for (int i = 0; i < state.Desktops.Count; i++)
        {
            Desktops.Add(new DesktopSlotViewModel(state.Desktops[i], i, i == state.CurrentIndex));
        }
    }
}
