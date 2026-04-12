using WinVDeskEssential.Models;
using WinVDeskEssential.Services.Desktop;
using WinVDeskEssential.Services.Interop;
using WinVDeskEssential.Services.MonitorPin;
using WinVDeskEssential.Services.QuickWindow;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WinVDeskEssential.ViewModels;

public class SwitcherPanelViewModel : ViewModelBase
{
    private readonly DesktopSwitchEngine _switchEngine;
    private readonly QuickWindowService _quickWindows;
    private readonly MonitorPinService _monitorPins;
    private string _currentMonitorId = string.Empty;
    private string _monitorDisplayName = string.Empty;
    private Orientation _panelOrientation = Orientation.Vertical;
    private Transform _itemTextTransform = Transform.Identity;
    private bool _isPickingWindow;
    private bool _isPickingMonitorPin;
    private bool _isExpanded;
    private string _selectedMonitorId = string.Empty;
    private ObservableCollection<PinnedWindow> _selectedMonitorPins = new();

    public ObservableCollection<DesktopSlotViewModel> Desktops { get; } = new();
    public ObservableCollection<Models.QuickWindow> QuickWindows => _quickWindows.Windows;
    public ObservableCollection<MonitorButtonViewModel> Monitors { get; } = new();

    public string MonitorDisplayName
    {
        get => _monitorDisplayName;
        set => SetProperty(ref _monitorDisplayName, value);
    }

    public Orientation PanelOrientation
    {
        get => _panelOrientation;
        set
        {
            if (SetProperty(ref _panelOrientation, value))
                OnPropertyChanged(nameof(CrossOrientation));
        }
    }

    /// <summary>
    /// Orientation perpendicular to the main panel flow.
    /// Used by the outer wrapper that stacks [panel, expand-button, expand-area].
    /// </summary>
    public Orientation CrossOrientation =>
        PanelOrientation == Orientation.Horizontal ? Orientation.Vertical : Orientation.Horizontal;

    public Transform ItemTextTransform
    {
        get => _itemTextTransform;
        set => SetProperty(ref _itemTextTransform, value);
    }

    public bool IsPickingWindow
    {
        get => _isPickingWindow;
        private set => SetProperty(ref _isPickingWindow, value);
    }

    public bool IsPickingMonitorPin
    {
        get => _isPickingMonitorPin;
        private set => SetProperty(ref _isPickingMonitorPin, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public string SelectedMonitorId
    {
        get => _selectedMonitorId;
        private set => SetProperty(ref _selectedMonitorId, value);
    }

    public ObservableCollection<PinnedWindow> SelectedMonitorPins
    {
        get => _selectedMonitorPins;
        private set => SetProperty(ref _selectedMonitorPins, value);
    }

    public ICommand SwitchToDesktopCommand { get; }
    public ICommand AddDesktopCommand { get; }
    public ICommand RemoveDesktopCommand { get; }
    public ICommand RenameDesktopCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand PickQuickWindowCommand { get; }
    public ICommand ActivateQuickWindowCommand { get; }
    public ICommand RemoveQuickWindowCommand { get; }
    public ICommand ToggleExpandCommand { get; }
    public ICommand SelectMonitorCommand { get; }
    public ICommand PickMonitorPinCommand { get; }
    public ICommand ActivateMonitorPinCommand { get; }
    public ICommand RemoveMonitorPinCommand { get; }

    public event Action? SettingsRequested;

    public SwitcherPanelViewModel(
        DesktopSwitchEngine switchEngine,
        QuickWindowService quickWindows,
        MonitorPinService monitorPins)
    {
        _switchEngine = switchEngine;
        _quickWindows = quickWindows;
        _monitorPins = monitorPins;
        _quickWindows.PickingStateChanged += () => IsPickingWindow = _quickWindows.IsPicking;
        _monitorPins.PickingStateChanged += () => IsPickingMonitorPin = _monitorPins.IsPicking;

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

        PickQuickWindowCommand = new RelayCommand(() =>
        {
            if (_quickWindows.IsPicking)
            {
                _quickWindows.CancelPicking();
            }
            else
            {
                _monitorPins.CancelPicking(); // cancel the other picker first
                _quickWindows.StartPicking();
            }
        });

        ActivateQuickWindowCommand = new RelayCommand(param =>
        {
            if (param is Models.QuickWindow qw)
                _quickWindows.ActivateWindow(qw);
        });

        RemoveQuickWindowCommand = new RelayCommand(param =>
        {
            if (param is Models.QuickWindow qw)
                _quickWindows.RemoveWindow(qw);
        });

        ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);

        SelectMonitorCommand = new RelayCommand(param =>
        {
            if (param is MonitorButtonViewModel mb)
                SelectMonitor(mb.DeviceId);
        });

        PickMonitorPinCommand = new RelayCommand(() =>
        {
            if (_monitorPins.IsPicking)
            {
                _monitorPins.CancelPicking();
            }
            else if (!string.IsNullOrEmpty(SelectedMonitorId))
            {
                _quickWindows.CancelPicking(); // cancel the other picker first
                _monitorPins.StartPickingForMonitor(SelectedMonitorId);
            }
        });

        ActivateMonitorPinCommand = new RelayCommand(param =>
        {
            if (param is PinnedWindow pw)
                _monitorPins.ActivatePin(pw);
        });

        RemoveMonitorPinCommand = new RelayCommand(param =>
        {
            if (param is PinnedWindow pw)
                _monitorPins.RemovePin(pw);
        });
    }

    public void SetDockPosition(DockPosition dock)
    {
        bool isHorizontal = dock == DockPosition.Top || dock == DockPosition.Bottom;
        PanelOrientation = isHorizontal ? Orientation.Horizontal : Orientation.Vertical;
        ItemTextTransform = isHorizontal ? new RotateTransform(90) : Transform.Identity;
    }

    public void SetMonitor(string monitorDeviceId, string displayName)
    {
        _currentMonitorId = monitorDeviceId;
        MonitorDisplayName = displayName;
        RefreshDesktops();
    }

    /// <summary>
    /// Populate the monitor button list (called once at startup and on monitor hot-plug).
    /// Auto-selects the primary monitor.
    /// </summary>
    public void SetMonitors(IEnumerable<MonitorInfo> monitors)
    {
        Monitors.Clear();
        foreach (var m in monitors)
        {
            Monitors.Add(new MonitorButtonViewModel
            {
                DeviceId = m.DeviceId,
                DisplayName = ShortenMonitorName(m.DisplayName),
                IsPrimary = m.IsPrimary,
            });
        }

        var primary = Monitors.FirstOrDefault(m => m.IsPrimary) ?? Monitors.FirstOrDefault();
        if (primary != null)
            SelectMonitor(primary.DeviceId);
    }

    private void SelectMonitor(string deviceId)
    {
        SelectedMonitorId = deviceId;
        foreach (var m in Monitors)
            m.IsSelected = m.DeviceId == deviceId;
        SelectedMonitorPins = _monitorPins.GetPinsForMonitor(deviceId);
    }

    private static string ShortenMonitorName(string displayName)
    {
        // "\\.\DISPLAY1" -> "Display 1"
        if (displayName.StartsWith("\\\\.\\DISPLAY"))
            return "Display " + displayName.Substring("\\\\.\\DISPLAY".Length);
        return displayName;
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
