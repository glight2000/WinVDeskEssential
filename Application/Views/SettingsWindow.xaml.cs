using WinVDeskEssential.Models;
using WinVDeskEssential.Services.Hotkey;
using System.Windows;

namespace WinVDeskEssential.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly HotkeyService? _hotkeyService;

    public SettingsWindow(AppSettings settings, HotkeyService? hotkeyService = null)
    {
        InitializeComponent();
        _settings = settings;
        _hotkeyService = hotkeyService;
        LoadSettings();
    }

    private void LoadSettings()
    {
        DockPositionCombo.SelectedIndex = (int)_settings.PanelDockPosition;
        AnimationCheckbox.IsChecked = _settings.PanelAnimationEnabled;
        AutoStartCheckbox.IsChecked = _settings.StartWithWindows;
        ExcludedProcessesBox.Text = string.Join("\n", _settings.ExcludedProcessNames);
        ExcludedClassesBox.Text = string.Join("\n", _settings.ExcludedWindowClasses);

        if (_hotkeyService != null)
        {
            HotkeyGrid.ItemsSource = _hotkeyService.GetBindings();
        }
    }

    public AppSettings GetUpdatedSettings()
    {
        _settings.PanelDockPosition = (DockPosition)DockPositionCombo.SelectedIndex;
        _settings.PanelAnimationEnabled = AnimationCheckbox.IsChecked ?? true;
        _settings.StartWithWindows = AutoStartCheckbox.IsChecked ?? false;
        _settings.ExcludedProcessNames = ExcludedProcessesBox.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        _settings.ExcludedWindowClasses = ExcludedClassesBox.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        return _settings;
    }
}
