using VDesk.Data;
using VDesk.Models;
using VDesk.Services.Desktop;
using VDesk.Services.Hotkey;
using VDesk.Services.Interop;
using VDesk.Services.Overlay;
using VDesk.Services.Wallpaper;
using VDesk.ViewModels;
using VDesk.Views;
using System.Diagnostics;
using System.Windows;

namespace VDesk.Services;

public class AppOrchestrator : IDisposable
{
    private readonly MonitorService _monitorService;
    private WindowEnumerator _windowEnumerator;
    private readonly IVirtualDesktopService _vdService;
    private readonly WindowTracker _windowTracker;
    private readonly DesktopSwitchEngine _switchEngine;
    private readonly OverlayManager _overlayManager;
    private readonly WallpaperService _wallpaperService;
    private readonly HotkeyManager _hotkeyManager;
    private readonly KeyboardHookManager _keyboardHook;
    private readonly HotkeyService _hotkeyService;
    private readonly ConfigRepository _configRepo;
    private readonly AppSettings _appSettings;
    private SwitcherPanel? _panel;
    private string _primaryMonitorId = string.Empty;

    public AppOrchestrator()
    {
        _appSettings = new AppSettings();
        IniSettings.Load(_appSettings);
        _configRepo = new ConfigRepository();
        _monitorService = new MonitorService();
        _windowEnumerator = new WindowEnumerator(_appSettings);
        _vdService = new VirtualDesktopService();
        _windowTracker = new WindowTracker(_windowEnumerator, _monitorService);
        _switchEngine = new DesktopSwitchEngine(_vdService, _windowTracker, _monitorService);
        _overlayManager = new OverlayManager(_monitorService);
        _wallpaperService = new WallpaperService();
        _hotkeyManager = new HotkeyManager();
        _keyboardHook = new KeyboardHookManager();
        _hotkeyService = new HotkeyService(_hotkeyManager, _keyboardHook, _switchEngine, _configRepo);
    }

    public void Initialize(Window mainWindow)
    {
        // 1. Discover monitors
        var monitors = _monitorService.GetAllMonitors();
        Logger.Log($"[App] Found {monitors.Count} monitors:");
        foreach (var m in monitors)
            Logger.Log($"  {m.DeviceId} ({m.Bounds.Width}x{m.Bounds.Height}) primary={m.IsPrimary}");

        // 2. Get system virtual desktops
        var systemDesktops = _vdService.GetAllDesktops();
        Logger.Log($"[App] System has {systemDesktops.Count} virtual desktops:");
        foreach (var d in systemDesktops)
            Logger.Log($"  [{d.Index}] {d.Name} (id={d.Id})");

        // 3. Build per-monitor state — always sync with ALL system desktops
        var monitorStates = new Dictionary<string, MonitorDesktopState>();
        foreach (var monitor in monitors)
        {
            var savedDesktops = _configRepo.LoadDesktopsForMonitor(monitor.DeviceId);

            // Always ensure ALL system desktops are mapped.
            // Add any system desktops not yet in the saved list.
            var savedIds = savedDesktops.Select(d => d.SystemDesktopId).ToHashSet();
            foreach (var sd in systemDesktops)
            {
                if (!savedIds.Contains(sd.Id))
                {
                    savedDesktops.Add(new DesktopSlot
                    {
                        SystemDesktopId = sd.Id,
                        Name = sd.Name.Length > 0 ? sd.Name : $"Desktop {savedDesktops.Count + 1}",
                    });
                }
            }

            // Remove desktops that no longer exist in the system
            var systemIds = systemDesktops.Select(d => d.Id).ToHashSet();
            savedDesktops.RemoveAll(d => !systemIds.Contains(d.SystemDesktopId));

            if (savedDesktops.Count == 0)
            {
                savedDesktops = systemDesktops.Select((sd, i) => new DesktopSlot
                {
                    SystemDesktopId = sd.Id,
                    Name = sd.Name.Length > 0 ? sd.Name : $"Desktop {i + 1}",
                }).ToList();
            }

            // Determine current index based on system's active desktop
            var currentSystemDesktop = _vdService.GetCurrentDesktop();
            var currentIndex = 0;
            if (currentSystemDesktop != null)
            {
                var idx = savedDesktops.FindIndex(d => d.SystemDesktopId == currentSystemDesktop.Id);
                if (idx >= 0) currentIndex = idx;
            }

            // Apply global watermark settings to all desktops
            foreach (var slot in savedDesktops)
            {
                slot.Watermark.IsEnabled = _appSettings.WatermarkAlwaysOn;
                slot.Watermark.Position = _appSettings.WatermarkPosition;
                slot.Watermark.FontSize = _appSettings.WatermarkFontSize;
                slot.Watermark.Opacity = _appSettings.WatermarkOpacity;
                slot.Watermark.Margin = _appSettings.WatermarkMargin;
                slot.Watermark.CustomText = null;
            }

            monitorStates[monitor.DeviceId] = new MonitorDesktopState
            {
                Monitor = monitor,
                Desktops = savedDesktops,
                CurrentIndex = currentIndex
            };
        }

        // 4. Start window tracker (must be before switch engine uses it)
        _windowTracker.Start();

        // 5. Initialize switch engine
        _switchEngine.Initialize(monitorStates);
        _switchEngine.DesktopSwitched += OnDesktopSwitched;

        // 6. Initialize overlays
        _overlayManager.Initialize();
        foreach (var state in monitorStates.Values)
            _overlayManager.UpdateOverlay(state.Monitor.DeviceId, state.CurrentDesktop);
        _overlayManager.ShowAll();

        // 7. Create switcher panel on PRIMARY monitor only, always visible
        var primaryState = monitorStates.Values.FirstOrDefault(s => s.Monitor.IsPrimary)
                        ?? monitorStates.Values.First();
        _primaryMonitorId = primaryState.Monitor.DeviceId;
        {
            var vm = new SwitcherPanelViewModel(_switchEngine);
            vm.SetMonitor(primaryState.Monitor.DeviceId, primaryState.Monitor.DisplayName);
            vm.SettingsRequested += OpenSettings;

            _panel = new SwitcherPanel(vm, primaryState.Monitor);
            _panel.Show();

            // Restore saved position or use default
            if (!double.IsNaN(_appSettings.PanelLeft) && !double.IsNaN(_appSettings.PanelTop))
            {
                _panel.Left = _appSettings.PanelLeft;
                _panel.Top = _appSettings.PanelTop;
                vm.SetDockPosition(_appSettings.PanelDockPosition);
            }
            else
            {
                _panel.SetInitialPosition();
            }

            _panel.PinToAllDesktops();

            // Save position when panel is moved
            _panel.LocationChanged += (_, _) => SavePanelPosition();
            _panel.DockPositionChanged += (dock) =>
            {
                _appSettings.PanelDockPosition = dock;
                SaveSettings();
            };
        }

        // 8. Initialize hotkeys (RegisterHotKey only; LL keyboard hook is deferred)
        _hotkeyManager.Initialize(mainWindow);
        _hotkeyService.TogglePanelRequested += TogglePanels;
        _hotkeyService.RegisterDefaultHotkeys();

        // 9. Save initial config
        foreach (var kvp in monitorStates)
            _configRepo.SaveDesktopsForMonitor(kvp.Key, kvp.Value.Desktops);

        Logger.Log("[App] Initialization complete (keyboard hook NOT yet installed)");
    }

    private void OnDesktopSwitched(string monitorDeviceId, DesktopSlot newDesktop)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            _overlayManager.UpdateOverlay(monitorDeviceId, newDesktop);

            // Always refresh panel selection state
            _panel?.RefreshSelection();

            var state = _switchEngine.GetStateForMonitor(monitorDeviceId);
            if (state != null)
            {
                var monitors = _monitorService.GetAllMonitors();
                var monitorIndex = monitors.FindIndex(m => m.DeviceId == monitorDeviceId);
                var wallpaperPath = _wallpaperService.GetMonitorDevicePath(monitorIndex);
                if (wallpaperPath != null)
                    _wallpaperService.ApplyBackground(wallpaperPath, newDesktop.Background);

                _configRepo.SaveDesktopsForMonitor(monitorDeviceId, state.Desktops);
            }
        });
    }

    private void TogglePanels()
    {
        if (_panel == null) return;
        if (_panel.IsVisible)
            _panel.Hide();
        else
        {
            _panel.Show();
            _panel.RefreshSelection();
        }
    }

    private void OpenSettings()
    {
        var settingsWindow = new SettingsWindow(_appSettings, _hotkeyService);
        settingsWindow.ShowDialog();
        var updated = settingsWindow.GetUpdatedSettings();
        _windowEnumerator = new WindowEnumerator(updated);
    }

    /// <summary>
    /// Must be called AFTER the WPF message loop is running.
    /// LL keyboard hooks need an active message pump on the installing thread.
    /// </summary>
    public void InstallKeyboardHook()
    {
        _keyboardHook.Install();
        Logger.Log("[App] Keyboard hook installed (deferred, message loop is now active)");
    }

    private void SavePanelPosition()
    {
        if (_panel == null) return;
        _appSettings.PanelLeft = _panel.Left;
        _appSettings.PanelTop = _panel.Top;
        IniSettings.Save(_appSettings);
    }

    private void SaveSettings()
    {
        IniSettings.Save(_appSettings);
    }

    public void SetPanelPinned(bool pinned)
    {
        _appSettings.PanelPinned = pinned;
        SaveSettings();
    }

    public void SetWatermarkEnabled(bool enabled)
    {
        _appSettings.WatermarkAlwaysOn = enabled;
        foreach (var kvp in _switchEngine.GetMonitorStates())
        {
            foreach (var slot in kvp.Value.Desktops)
                slot.Watermark.IsEnabled = enabled;
            _overlayManager.UpdateOverlay(kvp.Key, kvp.Value.CurrentDesktop);
        }
        SaveSettings();
    }

    public void SetWatermarkPosition(CornerPosition position)
    {
        _appSettings.WatermarkPosition = position;
        ApplyWatermarkToAll();
        SaveSettings();
    }

    public void SetWatermarkOpacity(double opacity)
    {
        _appSettings.WatermarkOpacity = opacity;
        ApplyWatermarkToAll();
        SaveSettings();
    }

    public void SetWatermarkFontSize(double size)
    {
        _appSettings.WatermarkFontSize = size;
        ApplyWatermarkToAll();
        SaveSettings();
    }

    public void SetWatermarkMargin(double margin)
    {
        _appSettings.WatermarkMargin = margin;
        foreach (var kvp in _switchEngine.GetMonitorStates())
        {
            foreach (var slot in kvp.Value.Desktops)
                slot.Watermark.Margin = margin;
            _overlayManager.UpdateOverlay(kvp.Key, kvp.Value.CurrentDesktop);
        }
        SaveSettings();
    }

    private void ApplyWatermarkToAll()
    {
        foreach (var kvp in _switchEngine.GetMonitorStates())
        {
            foreach (var slot in kvp.Value.Desktops)
            {
                slot.Watermark.IsEnabled = _appSettings.WatermarkAlwaysOn;
                slot.Watermark.Position = _appSettings.WatermarkPosition;
                slot.Watermark.FontSize = _appSettings.WatermarkFontSize;
                slot.Watermark.Opacity = _appSettings.WatermarkOpacity;
            }
            _overlayManager.UpdateOverlay(kvp.Key, kvp.Value.CurrentDesktop);
        }
    }

    public void ShowSettingsWindow() => OpenSettings();
    public void TogglePanelForCurrentMonitor() => TogglePanels();

    public void Dispose()
    {
        _hotkeyService.Dispose();
        _switchEngine.Dispose();
        _windowTracker.Dispose();
        _overlayManager.Dispose();
        _wallpaperService.Dispose();

        _panel?.Close();

        if (_vdService is IDisposable vdDisposable)
            vdDisposable.Dispose();
        _configRepo.Dispose();
    }
}
