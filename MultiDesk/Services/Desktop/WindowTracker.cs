using VDesk.Services;
using VDesk.Services.Interop;
using System.Diagnostics;

namespace VDesk.Services.Desktop;

/// <summary>
/// Maintains a real-time snapshot of which windows are on which monitor.
/// Uses WinEvent hooks to track window creation/destruction/movement.
/// This snapshot is critical: after a desktop switch, hidden windows can no longer
/// be enumerated, so we must know their HWNDs from BEFORE the switch.
/// </summary>
public class WindowTracker : IDisposable
{
    private readonly WindowEnumerator _enumerator;
    private readonly MonitorService _monitorService;
    private readonly object _lock = new();

    // monitorHandle -> list of window handles on that monitor
    private readonly Dictionary<IntPtr, List<IntPtr>> _windowsByMonitor = new();

    private IntPtr _winEventHook;
    private NativeMethods.WinEventDelegate? _winEventProc;

    public WindowTracker(WindowEnumerator enumerator, MonitorService monitorService)
    {
        _enumerator = enumerator;
        _monitorService = monitorService;
    }

    public void Start()
    {
        // Take initial snapshot
        RefreshAll();

        // Install WinEvent hook to track window changes
        _winEventProc = OnWinEvent;
        _winEventHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_SHOW,
            NativeMethods.EVENT_OBJECT_DESTROY,
            IntPtr.Zero,
            _winEventProc,
            0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
    }

    /// <summary>
    /// Full refresh: enumerate all windows and group by monitor.
    /// </summary>
    public void RefreshAll()
    {
        lock (_lock)
        {
            _windowsByMonitor.Clear();
            var allWindows = _enumerator.GetAllVisibleWindows();
            foreach (var win in allWindows)
            {
                if (win.MonitorHandle == IntPtr.Zero) continue;
                if (!_windowsByMonitor.ContainsKey(win.MonitorHandle))
                    _windowsByMonitor[win.MonitorHandle] = new List<IntPtr>();
                _windowsByMonitor[win.MonitorHandle].Add(win.Handle);
            }
            Logger.Log($"[WindowTracker] Refreshed: {allWindows.Count} windows across {_windowsByMonitor.Count} monitors");
        }
    }

    /// <summary>
    /// Get the last known window handles for a specific monitor.
    /// </summary>
    public List<IntPtr> GetWindowsOnMonitor(IntPtr monitorHandle)
    {
        lock (_lock)
        {
            if (_windowsByMonitor.TryGetValue(monitorHandle, out var list))
                return new List<IntPtr>(list);
            return new List<IntPtr>();
        }
    }

    /// <summary>
    /// Get all window handles on all monitors EXCEPT the specified one.
    /// </summary>
    public List<IntPtr> GetWindowsExceptMonitor(IntPtr excludeMonitorHandle)
    {
        lock (_lock)
        {
            var result = new List<IntPtr>();
            foreach (var kvp in _windowsByMonitor)
            {
                if (kvp.Key == excludeMonitorHandle) continue;
                result.AddRange(kvp.Value);
            }
            return result;
        }
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Only care about top-level windows (idObject == 0)
        if (idObject != 0 || hwnd == IntPtr.Zero) return;

        // Debounce: just refresh the full snapshot
        // This is cheap enough (<1ms for typical window counts)
        RefreshAll();
    }

    public void Dispose()
    {
        if (_winEventHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }
    }
}
