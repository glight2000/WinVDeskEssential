using VDesk.Models;
using VDesk.Services.Interop;

namespace VDesk.Services.Overlay;

public class OverlayManager : IDisposable
{
    private readonly MonitorService _monitorService;
    private readonly Dictionary<string, OverlayWindow> _overlays = new();

    public OverlayManager(MonitorService monitorService)
    {
        _monitorService = monitorService;
    }

    public void Initialize()
    {
        var monitors = _monitorService.GetAllMonitors();
        foreach (var monitor in monitors)
        {
            if (_overlays.ContainsKey(monitor.DeviceId)) continue;

            var overlay = new OverlayWindow(monitor);
            _overlays[monitor.DeviceId] = overlay;
        }
    }

    public void UpdateOverlay(string monitorDeviceId, DesktopSlot? desktop)
    {
        if (_overlays.TryGetValue(monitorDeviceId, out var overlay))
        {
            overlay.UpdateOverlay(desktop);
        }
    }

    public void ShowAll()
    {
        foreach (var overlay in _overlays.Values)
        {
            overlay.Show();
        }
    }

    public void HideAll()
    {
        foreach (var overlay in _overlays.Values)
        {
            overlay.Hide();
        }
    }

    public void RefreshMonitors()
    {
        // Close existing overlays
        foreach (var overlay in _overlays.Values)
        {
            overlay.Close();
        }
        _overlays.Clear();

        // Recreate
        Initialize();
    }

    public void Dispose()
    {
        foreach (var overlay in _overlays.Values)
        {
            overlay.Close();
        }
        _overlays.Clear();
    }
}
