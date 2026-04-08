using WinVDeskEssential.Models;
using WinVDeskEssential.Services.Interop;
using WinVDeskEssential.Services;
using System.Diagnostics;

namespace WinVDeskEssential.Services.Desktop;

/// <summary>
/// Core engine: per-monitor independent virtual desktop switching.
///
/// Strategy (shared pool):
///   All monitors share the same system virtual desktops.
///   When switching desktop on Monitor A, we:
///     1. Snapshot windows on OTHER monitors (from the live WindowTracker)
///     2. Call system Switch (global — all monitors change)
///     3. Immediately move other monitors' windows TO the new desktop so they stay visible
///
/// For system-triggered switches (Win+Tab):
///   We listen to VirtualDesktop.CurrentChanged and use the pre-switch
///   snapshot (maintained by WindowTracker) to restore other monitors' windows.
/// </summary>
public class DesktopSwitchEngine : IDisposable
{
    private readonly IVirtualDesktopService _vdService;
    private readonly WindowTracker _windowTracker;
    private readonly MonitorService _monitorService;
    private readonly Dictionary<string, MonitorDesktopState> _monitorStates = new();
    private readonly object _switchLock = new();
    private volatile bool _isSwitching;
    private System.Windows.Threading.DispatcherTimer? _pollTimer;
    private Guid _lastKnownDesktopId;
    private List<Guid> _lastKnownDesktopOrder = new();
    private List<string> _lastKnownDesktopNames = new();

    public event Action<string, DesktopSlot>? DesktopSwitched;
    /// <summary>
    /// Fired when the desktop list changes (add/remove/reorder) and the panel should refresh.
    /// </summary>
    public event Action? DesktopListChanged;

    public DesktopSwitchEngine(
        IVirtualDesktopService vdService,
        WindowTracker windowTracker,
        MonitorService monitorService)
    {
        _vdService = vdService;
        _windowTracker = windowTracker;
        _monitorService = monitorService;

        // Listen for system-level desktop changes (Win+Tab, taskbar, etc.)
        _vdService.DesktopChanged += OnSystemDesktopChanged;
        _vdService.DesktopCreated += OnSystemDesktopCreated;
        _vdService.DesktopRemoved += OnSystemDesktopRemoved;
        _vdService.DesktopRenamed += OnSystemDesktopRenamed;
        _vdService.DesktopMoved += OnSystemDesktopMoved;
    }

    public void Initialize(Dictionary<string, MonitorDesktopState> states)
    {
        foreach (var kvp in states)
            _monitorStates[kvp.Key] = kvp.Value;

        // Record initial desktop ID for polling
        var current = _vdService.GetCurrentDesktop();
        _lastKnownDesktopId = current?.Id ?? Guid.Empty;
        var initialDesktops = _vdService.GetAllDesktops();
        _lastKnownDesktopOrder = initialDesktops.Select(d => d.Id).ToList();
        _lastKnownDesktopNames = initialDesktops.Select(d => d.Name).ToList();

        Logger.Log($"[SwitchEngine] Initialized with {states.Count} monitors, currentDesktop={_lastKnownDesktopId}");
        foreach (var kvp in states)
            Logger.Log($"  Monitor {kvp.Key}: {kvp.Value.Desktops.Count} desktops, current={kvp.Value.CurrentIndex}");

        // Start polling for desktop changes (fallback for when COM events don't work)
        _pollTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();
        Logger.Log("[SwitchEngine] Desktop change polling started (100ms interval)");
    }

    private int _pollCount;
    private void OnPollTick(object? sender, EventArgs e)
    {
        if (_isSwitching) return;

        try
        {
            var current = _vdService.GetCurrentDesktop();
            _pollCount++;
            // Log every 50th poll (~5s) to confirm polling is alive
            if (_pollCount % 50 == 1)
                Logger.Log($"[SwitchEngine] Poll #{_pollCount}: current={current?.Id}, lastKnown={_lastKnownDesktopId}");

            if (current == null) return;

            if (current.Id != _lastKnownDesktopId)
            {
                var oldId = _lastKnownDesktopId;
                _lastKnownDesktopId = current.Id;
                Logger.Log($"[SwitchEngine] Poll DETECTED change: {oldId} -> {current.Id} ({current.Name})");
                OnSystemDesktopChanged(oldId, current.Id);
            }

            // Check for desktop list changes (add/remove/reorder/rename) as fallback
            var currentDesktops = _vdService.GetAllDesktops();
            var currentOrder = currentDesktops.Select(d => d.Id).ToList();
            var currentNames = currentDesktops.Select(d => d.Name).ToList();
            if (!currentOrder.SequenceEqual(_lastKnownDesktopOrder) ||
                !currentNames.SequenceEqual(_lastKnownDesktopNames))
            {
                Logger.Log($"[SwitchEngine] Poll DETECTED desktop list/name change");
                _lastKnownDesktopOrder = currentOrder;
                _lastKnownDesktopNames = currentNames;
                SyncDesktopsFromSystem();
                DesktopListChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[SwitchEngine] Poll error: {ex.Message}");
        }
    }

    public Dictionary<string, MonitorDesktopState> GetMonitorStates() => _monitorStates;

    public MonitorDesktopState? GetStateForMonitor(string monitorDeviceId)
    {
        return _monitorStates.TryGetValue(monitorDeviceId, out var state) ? state : null;
    }

    /// <summary>
    /// Switch desktop on a specific monitor. Other monitors' windows stay put.
    /// </summary>
    public void SwitchDesktop(string monitorDeviceId, int targetIndex)
    {
        lock (_switchLock)
        {
            if (!_monitorStates.TryGetValue(monitorDeviceId, out var state))
            {
                Logger.Log($"[SwitchEngine] Monitor {monitorDeviceId} not found");
                return;
            }
            if (targetIndex < 0 || targetIndex >= state.Desktops.Count)
            {
                Logger.Log($"[SwitchEngine] Invalid index {targetIndex}, have {state.Desktops.Count} desktops");
                return;
            }
            if (targetIndex == state.CurrentIndex)
            {
                Logger.Log($"[SwitchEngine] Already on desktop {targetIndex}");
                return;
            }

            var targetDesktop = state.Desktops[targetIndex];
            Logger.Log($"[SwitchEngine] Switching monitor {monitorDeviceId} from {state.CurrentIndex} to {targetIndex} (sysId={targetDesktop.SystemDesktopId})");

            // Step 1: Grab snapshot of OTHER monitors' windows BEFORE switching
            var windowsToRestore = GetOtherMonitorsWindows(monitorDeviceId);
            Logger.Log($"[SwitchEngine] Snapshot: {windowsToRestore.Count} windows on other monitors to preserve");

            // Step 2: Perform system desktop switch
            _isSwitching = true;
            try
            {
                _vdService.SwitchToDesktop(targetDesktop.SystemDesktopId);
            }
            catch (Exception ex)
            {
                Logger.Log($"[SwitchEngine] SwitchToDesktop failed: {ex.Message}");
                _isSwitching = false;
                return;
            }

            // Step 3: Move other monitors' windows to the NEW desktop so they stay visible
            MoveWindowsToDesktop(windowsToRestore, targetDesktop.SystemDesktopId);
            _isSwitching = false;

            // Step 4: Update state, poller tracking, and notify
            _lastKnownDesktopId = targetDesktop.SystemDesktopId;
            state.CurrentIndex = targetIndex;

            // Step 5: Refresh window tracker since we just moved things
            _windowTracker.RefreshAll();

            DesktopSwitched?.Invoke(monitorDeviceId, targetDesktop);
        }
    }

    /// <summary>
    /// Handle system-triggered desktop change (Win+Tab, taskbar click, etc.)
    /// Only intervene when cursor is on the PRIMARY monitor — keep secondary stable.
    /// If cursor is on secondary, let Windows default behavior happen (all monitors switch together).
    /// </summary>
    private void OnSystemDesktopChanged(Guid oldDesktopId, Guid newDesktopId)
    {
        if (_isSwitching) return;

        Logger.Log($"[SwitchEngine] System desktop changed: {oldDesktopId} -> {newDesktopId}");

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            lock (_switchLock)
            {
                if (_isSwitching) return;

                var cursorMonitor = _monitorService.GetMonitorFromCursor();
                if (cursorMonitor == null) return;

                // Only intervene when switching from the PRIMARY monitor.
                // If cursor is on a secondary monitor, do nothing — let Windows default behavior happen.
                if (!cursorMonitor.IsPrimary)
                {
                    Logger.Log($"[SwitchEngine] Cursor on secondary ({cursorMonitor.DeviceId}), skipping — default Windows behavior");
                    _windowTracker.RefreshAll();
                    return;
                }

                if (!_monitorStates.TryGetValue(cursorMonitor.DeviceId, out var cursorState)) return;

                var targetIndex = cursorState.Desktops.FindIndex(d => d.SystemDesktopId == newDesktopId);

                Logger.Log($"[SwitchEngine] Primary switch: {cursorMonitor.DeviceId} -> index {targetIndex}");

                // Move secondary monitors' windows TO the new desktop so they stay visible
                var windowsToRestore = GetOtherMonitorsWindows(cursorMonitor.DeviceId);
                Logger.Log($"[SwitchEngine] Keeping {windowsToRestore.Count} secondary windows visible");

                MoveWindowsToDesktop(windowsToRestore, newDesktopId);

                if (targetIndex >= 0)
                {
                    cursorState.CurrentIndex = targetIndex;
                    DesktopSwitched?.Invoke(cursorMonitor.DeviceId, cursorState.Desktops[targetIndex]);
                }
                _windowTracker.RefreshAll();
            }
        });
    }

    /// <summary>
    /// Get window handles from all monitors except the specified one.
    /// Uses the WindowTracker's live snapshot.
    /// </summary>
    private List<IntPtr> GetOtherMonitorsWindows(string excludeMonitorDeviceId)
    {
        var result = new List<IntPtr>();
        foreach (var kvp in _monitorStates)
        {
            if (kvp.Key == excludeMonitorDeviceId) continue;
            var windows = _windowTracker.GetWindowsOnMonitor(kvp.Value.Monitor.Handle);
            result.AddRange(windows);
        }
        return result;
    }

    /// <summary>
    /// Move a list of windows to the specified desktop.
    /// </summary>
    private void MoveWindowsToDesktop(List<IntPtr> windowHandles, Guid desktopId)
    {
        int moved = 0, failed = 0;
        foreach (var hwnd in windowHandles)
        {
            try
            {
                _vdService.MoveWindowToDesktop(hwnd, desktopId);
                moved++;
            }
            catch (Exception ex)
            {
                failed++;
                Logger.Log($"[SwitchEngine] Failed to move window {hwnd}: {ex.Message}");
            }
        }
        Logger.Log($"[SwitchEngine] Moved {moved} windows, {failed} failed");
    }

    public void SwitchToNextDesktop()
    {
        var monitor = _monitorService.GetMonitorFromCursor();
        if (monitor == null) return;
        if (!_monitorStates.TryGetValue(monitor.DeviceId, out var state)) return;
        var next = (state.CurrentIndex + 1) % state.Desktops.Count;
        Logger.Log($"[SwitchEngine] SwitchToNext: monitor={monitor.DeviceId}, {state.CurrentIndex} -> {next}");
        SwitchDesktop(monitor.DeviceId, next);
    }

    public void SwitchToPreviousDesktop()
    {
        var monitor = _monitorService.GetMonitorFromCursor();
        if (monitor == null) return;
        if (!_monitorStates.TryGetValue(monitor.DeviceId, out var state)) return;
        var prev = (state.CurrentIndex - 1 + state.Desktops.Count) % state.Desktops.Count;
        Logger.Log($"[SwitchEngine] SwitchToPrev: monitor={monitor.DeviceId}, {state.CurrentIndex} -> {prev}");
        SwitchDesktop(monitor.DeviceId, prev);
    }

    public void SwitchToDesktopIndex(int index)
    {
        var monitor = _monitorService.GetMonitorFromCursor();
        if (monitor == null) return;
        Logger.Log($"[SwitchEngine] SwitchToIndex: monitor={monitor.DeviceId}, index={index}");
        SwitchDesktop(monitor.DeviceId, index);
    }

    public void MoveActiveWindowToDesktop(int direction)
    {
        var fgHwnd = NativeMethods.GetForegroundWindow();
        if (fgHwnd == IntPtr.Zero) return;

        var monitor = _monitorService.GetMonitorFromWindow(fgHwnd);
        if (monitor == null) return;
        if (!_monitorStates.TryGetValue(monitor.DeviceId, out var state)) return;

        var targetIndex = state.CurrentIndex + direction;
        if (targetIndex < 0 || targetIndex >= state.Desktops.Count) return;

        var targetDesktop = state.Desktops[targetIndex];
        _vdService.MoveWindowToDesktop(fgHwnd, targetDesktop.SystemDesktopId);
    }

    public DesktopSlot? AddDesktop(string monitorDeviceId)
    {
        if (!_monitorStates.TryGetValue(monitorDeviceId, out var state)) return null;

        var newVd = _vdService.CreateDesktop();
        var slot = new DesktopSlot
        {
            SystemDesktopId = newVd.Id,
            Name = $"Desktop {state.Desktops.Count + 1}"
        };
        state.Desktops.Add(slot);
        return slot;
    }

    public bool RemoveDesktop(string monitorDeviceId, int index)
    {
        if (!_monitorStates.TryGetValue(monitorDeviceId, out var state)) return false;
        if (state.Desktops.Count <= 1) return false;
        if (index < 0 || index >= state.Desktops.Count) return false;

        var desktop = state.Desktops[index];
        _vdService.RemoveDesktop(desktop.SystemDesktopId);
        state.Desktops.RemoveAt(index);

        if (state.CurrentIndex >= state.Desktops.Count)
            state.CurrentIndex = state.Desktops.Count - 1;

        return true;
    }

    /// <summary>
    /// Sync internal state with the system's actual desktop list.
    /// Called when desktops are created, removed, or reordered externally.
    /// </summary>
    private void SyncDesktopsFromSystem()
    {
        var systemDesktops = _vdService.GetAllDesktops();
        _lastKnownDesktopOrder = systemDesktops.Select(d => d.Id).ToList();
        _lastKnownDesktopNames = systemDesktops.Select(d => d.Name).ToList();
        var systemIds = systemDesktops.Select(d => d.Id).ToHashSet();

        var systemNameMap = systemDesktops.ToDictionary(d => d.Id, d => d.Name);

        foreach (var state in _monitorStates.Values)
        {
            // Remove desktops that no longer exist in the system
            state.Desktops.RemoveAll(d => !systemIds.Contains(d.SystemDesktopId));

            // Sync names from system for existing desktops
            foreach (var slot in state.Desktops)
            {
                if (systemNameMap.TryGetValue(slot.SystemDesktopId, out var sysName) && sysName.Length > 0)
                    slot.Name = sysName;
            }

            // Add any new system desktops not yet in our list
            var knownIds = state.Desktops.Select(d => d.SystemDesktopId).ToHashSet();
            foreach (var sd in systemDesktops)
            {
                if (!knownIds.Contains(sd.Id))
                {
                    state.Desktops.Add(new DesktopSlot
                    {
                        SystemDesktopId = sd.Id,
                        Name = sd.Name.Length > 0 ? sd.Name : $"Desktop {state.Desktops.Count + 1}",
                    });
                }
            }

            // Reorder to match system order
            var orderMap = systemDesktops.ToDictionary(d => d.Id, d => d.Index);
            state.Desktops.Sort((a, b) =>
            {
                var ia = orderMap.GetValueOrDefault(a.SystemDesktopId, int.MaxValue);
                var ib = orderMap.GetValueOrDefault(b.SystemDesktopId, int.MaxValue);
                return ia.CompareTo(ib);
            });

            // Fix current index
            if (state.CurrentIndex >= state.Desktops.Count)
                state.CurrentIndex = Math.Max(0, state.Desktops.Count - 1);
        }

        // Update current index to match the actual current desktop
        var current = _vdService.GetCurrentDesktop();
        if (current != null)
        {
            _lastKnownDesktopId = current.Id;
            foreach (var state in _monitorStates.Values)
            {
                var idx = state.Desktops.FindIndex(d => d.SystemDesktopId == current.Id);
                if (idx >= 0) state.CurrentIndex = idx;
            }
        }
    }

    private void OnSystemDesktopCreated(Guid newDesktopId)
    {
        Logger.Log($"[SwitchEngine] System desktop created: {newDesktopId}");
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            lock (_switchLock)
            {
                SyncDesktopsFromSystem();
                DesktopListChanged?.Invoke();
            }
        });
    }

    private void OnSystemDesktopRemoved(Guid removedDesktopId)
    {
        Logger.Log($"[SwitchEngine] System desktop removed: {removedDesktopId}");
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            lock (_switchLock)
            {
                SyncDesktopsFromSystem();
                DesktopListChanged?.Invoke();
            }
        });
    }

    private void OnSystemDesktopMoved(Guid desktopId, int oldIndex, int newIndex)
    {
        Logger.Log($"[SwitchEngine] System desktop moved: {desktopId} from {oldIndex} to {newIndex}");
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            lock (_switchLock)
            {
                SyncDesktopsFromSystem();
                DesktopListChanged?.Invoke();
            }
        });
    }

    private void OnSystemDesktopRenamed(Guid desktopId, string newName)
    {
        Logger.Log($"[SwitchEngine] System desktop renamed: {desktopId} -> {newName}");
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            lock (_switchLock)
            {
                foreach (var state in _monitorStates.Values)
                {
                    var slot = state.Desktops.FirstOrDefault(d => d.SystemDesktopId == desktopId);
                    if (slot != null) slot.Name = newName;
                }
                DesktopListChanged?.Invoke();
            }
        });
    }

    public void Dispose()
    {
        _pollTimer?.Stop();
        _vdService.DesktopChanged -= OnSystemDesktopChanged;
        _vdService.DesktopCreated -= OnSystemDesktopCreated;
        _vdService.DesktopRemoved -= OnSystemDesktopRemoved;
        _vdService.DesktopRenamed -= OnSystemDesktopRenamed;
        _vdService.DesktopMoved -= OnSystemDesktopMoved;
    }
}
