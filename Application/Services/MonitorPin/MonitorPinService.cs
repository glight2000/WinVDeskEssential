using WinVDeskEssential.Models;
using WinVDeskEssential.Services.Desktop;
using WinVDeskEssential.Services.Interop;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WinVDeskEssential.Services.MonitorPin;

/// <summary>
/// Per-monitor pinned windows — a curated list of windows that should "follow" a specific monitor.
///
/// Identity: (ProcessName, TitleKey) where TitleKey is the first 30 chars of the window title
/// captured at pin time. On rebind, the scanner does a process + title-contains match, which
/// is robust to title changes for single-window apps (empty TitleKey) and works reasonably for
/// multi-window apps like Chrome where the title includes a page/doc identifier.
///
/// The unified model mirrors QuickWindowService: the persistent source of truth is a configured
/// set; a scanner rebinds HWNDs every few seconds; picker / remove mutate the set and fire
/// ConfigChanged so the caller can persist to INI.
/// </summary>
public class MonitorPinService : IDisposable
{
    private readonly IVirtualDesktopService _vdService;

    // deviceId -> observable list of PinnedWindow shown in UI
    private readonly Dictionary<string, ObservableCollection<PinnedWindow>> _pinsByMonitor = new();

    // deviceId -> list of MonitorPinConfig (persistent source of truth)
    private readonly Dictionary<string, List<MonitorPinConfig>> _configs = new();

    public event Action? ConfigChanged;
    public event Action? PickingStateChanged;
    public bool IsPicking { get; private set; }
    private string? _pickingForMonitor;

    // Picker hook state
    private IntPtr _pickHookId = IntPtr.Zero;
    private static NativeMethods.LowLevelMouseProc? s_pickProc;
    private static GCHandle s_pickGcHandle;
    private static MonitorPinService? s_instance;
    private System.Windows.Threading.DispatcherTimer? _escTimer;

    private readonly HashSet<IntPtr> _ownHwnds = new();
    private System.Windows.Threading.DispatcherTimer? _scanTimer;

    public MonitorPinService(IVirtualDesktopService vdService)
    {
        _vdService = vdService;
    }

    public void RegisterOwnWindow(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero) _ownHwnds.Add(hwnd);
    }

    /// <summary>
    /// Replace the current config with the given per-monitor list and reconcile.
    /// Does NOT raise ConfigChanged (caller is the source).
    /// </summary>
    public void SetConfig(Dictionary<string, List<MonitorPinConfig>> configs)
    {
        _configs.Clear();
        foreach (var kvp in configs)
            _configs[kvp.Key] = new List<MonitorPinConfig>(kvp.Value);
        Reconcile();
    }

    /// <summary>
    /// Return a snapshot of the current config for persistence.
    /// </summary>
    public Dictionary<string, List<MonitorPinConfig>> GetConfig()
    {
        return _configs.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Select(c => new MonitorPinConfig
            {
                ProcessName = c.ProcessName,
                TitleKey = c.TitleKey
            }).ToList());
    }

    /// <summary>
    /// Get (or create) the observable list for a monitor's pinned windows.
    /// The UI binds to this.
    /// </summary>
    public ObservableCollection<PinnedWindow> GetPinsForMonitor(string deviceId)
    {
        if (!_pinsByMonitor.TryGetValue(deviceId, out var list))
        {
            list = new ObservableCollection<PinnedWindow>();
            _pinsByMonitor[deviceId] = list;
        }
        return list;
    }

    /// <summary>
    /// Return all currently-bound HWNDs across all monitors.
    /// Used by DesktopSwitchEngine to move pinned windows on every desktop switch.
    /// </summary>
    public List<IntPtr> GetAllBoundHwnds()
    {
        var result = new List<IntPtr>();
        foreach (var list in _pinsByMonitor.Values)
            foreach (var pw in list)
                if (pw.Hwnd != IntPtr.Zero)
                    result.Add(pw.Hwnd);
        return result;
    }

    public void StartScanner()
    {
        if (_scanTimer != null) return;
        _scanTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _scanTimer.Tick += (_, _) => Reconcile();
        _scanTimer.Start();
        Logger.Log("[MonitorPin] Scanner started (2s interval)");
    }

    /// <summary>
    /// Reconcile the observable lists with the config + currently-running processes.
    /// IMPORTANT: never remove entries from <see cref="_pinsByMonitor"/> — the VM may
    /// hold a reference to a specific list (for the selected monitor). Only clear.
    /// </summary>
    private void Reconcile()
    {
        // Union of all monitor IDs we care about (config side + previously-tracked lists)
        var allMonitorIds = new HashSet<string>(_configs.Keys);
        foreach (var k in _pinsByMonitor.Keys) allMonitorIds.Add(k);

        foreach (var deviceId in allMonitorIds)
        {
            var list = GetPinsForMonitor(deviceId);  // creates if missing, preserves references

            _configs.TryGetValue(deviceId, out var configs);
            var desired = new List<PinnedWindow>();
            if (configs != null)
            {
                foreach (var cfg in configs)
                {
                    var hwnd = FindWindow(cfg.ProcessName, cfg.TitleKey);
                    var display = !string.IsNullOrEmpty(cfg.TitleKey) ? cfg.TitleKey : cfg.ProcessName;
                    desired.Add(new PinnedWindow
                    {
                        MonitorDeviceId = deviceId,
                        ProcessName = cfg.ProcessName,
                        TitleKey = cfg.TitleKey,
                        DisplayTitle = display,
                        Hwnd = hwnd,
                    });
                }
            }

            // Rebuild in place — keeps the collection reference stable for the VM.
            list.Clear();
            foreach (var pw in desired) list.Add(pw);
        }
    }

    /// <summary>
    /// Find a live top-level window whose process + title matches the pin identity.
    /// - If titleKey is empty: return any MainWindowHandle of the process.
    /// - If titleKey is set: prefer a window whose title contains the key (case-insensitive).
    ///   Falls back to MainWindowHandle of the first process if nothing matches.
    /// </summary>
    private static IntPtr FindWindow(string processName, string titleKey)
    {
        try
        {
            var processes = Process.GetProcessesByName(processName);
            if (processes.Length == 0) return IntPtr.Zero;

            IntPtr fallback = IntPtr.Zero;
            foreach (var p in processes)
            {
                try
                {
                    var hwnd = p.MainWindowHandle;
                    if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd)) continue;

                    if (string.IsNullOrEmpty(titleKey))
                        return hwnd;

                    var title = GetWindowTitle(hwnd);
                    if (title.Contains(titleKey, StringComparison.OrdinalIgnoreCase))
                        return hwnd;

                    if (fallback == IntPtr.Zero) fallback = hwnd;
                }
                finally { p.Dispose(); }
            }
            return fallback;
        }
        catch (Exception ex)
        {
            Logger.Log($"[MonitorPin] FindWindow('{processName}','{titleKey}') failed: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    // ---- Picker (next-click captures a window for a specific monitor) ----

    public void StartPickingForMonitor(string deviceId)
    {
        if (IsPicking) return;
        _pickingForMonitor = deviceId;

        s_instance = this;
        s_pickProc = StaticPickCallback;
        if (s_pickGcHandle.IsAllocated) s_pickGcHandle.Free();
        s_pickGcHandle = GCHandle.Alloc(s_pickProc);

        var hMod = NativeMethods.GetModuleHandle("user32.dll");
        _pickHookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            s_pickProc,
            hMod,
            0);

        if (_pickHookId == IntPtr.Zero)
        {
            Logger.Log($"[MonitorPin] Failed to install picker hook, error={Marshal.GetLastWin32Error()}");
            CleanupPicker();
            return;
        }

        IsPicking = true;
        PickingStateChanged?.Invoke();
        Logger.Log($"[MonitorPin] Picker started for monitor {deviceId}");

        _escTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _escTimer.Tick += (_, _) =>
        {
            if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8000) != 0)
            {
                Logger.Log("[MonitorPin] Picker cancelled by ESC");
                CancelPicking();
            }
        };
        _escTimer.Start();
    }

    public void CancelPicking()
    {
        if (!IsPicking) return;
        CleanupPicker();
        PickingStateChanged?.Invoke();
    }

    private void CleanupPicker()
    {
        if (_pickHookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_pickHookId);
            _pickHookId = IntPtr.Zero;
        }
        if (s_pickGcHandle.IsAllocated)
            s_pickGcHandle.Free();
        s_pickProc = null;
        _escTimer?.Stop();
        _escTimer = null;
        IsPicking = false;
        _pickingForMonitor = null;
    }

    private static IntPtr StaticPickCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        return s_instance?.PickCallback(nCode, wParam, lParam)
            ?? NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr PickCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || !IsPicking)
            return NativeMethods.CallNextHookEx(_pickHookId, nCode, wParam, lParam);

        int msg = wParam.ToInt32();
        if (msg != NativeMethods.WM_LBUTTONDOWN)
            return NativeMethods.CallNextHookEx(_pickHookId, nCode, wParam, lParam);

        var mouseData = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
        var hwnd = NativeMethods.WindowFromPoint(mouseData.pt);
        Logger.Log($"[MonitorPin] PickCallback click at ({mouseData.pt.X},{mouseData.pt.Y}) hwnd=0x{hwnd:X}");

        if (hwnd == IntPtr.Zero)
            return NativeMethods.CallNextHookEx(_pickHookId, nCode, wParam, lParam);

        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero)
            return NativeMethods.CallNextHookEx(_pickHookId, nCode, wParam, lParam);

        if (_ownHwnds.Contains(root))
        {
            Logger.Log($"[MonitorPin] Click on own panel (0x{root:X}), ignoring");
            return NativeMethods.CallNextHookEx(_pickHookId, nCode, wParam, lParam);
        }

        var exStyle = NativeMethods.GetWindowLong(root, NativeMethods.GWL_EXSTYLE);
        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
        {
            Logger.Log($"[MonitorPin] Click on tool window (0x{root:X}), ignoring");
            return NativeMethods.CallNextHookEx(_pickHookId, nCode, wParam, lParam);
        }

        var pickedHwnd = root;
        var monitorId = _pickingForMonitor;
        Logger.Log($"[MonitorPin] Captured hwnd=0x{pickedHwnd:X} for monitor={monitorId}");

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (monitorId != null)
                AddPickedWindow(monitorId, pickedHwnd);
            CleanupPicker();
            PickingStateChanged?.Invoke();
        });

        return (IntPtr)1;
    }

    private void AddPickedWindow(string monitorId, IntPtr hwnd)
    {
        var procName = GetProcessName(hwnd);
        if (string.IsNullOrEmpty(procName))
        {
            Logger.Log($"[MonitorPin] Could not resolve process for hwnd=0x{hwnd:X}");
            return;
        }

        var title = GetWindowTitle(hwnd);
        var titleKey = string.IsNullOrEmpty(title)
            ? ""
            : (title.Length > 30 ? title.Substring(0, 30) : title);

        if (!_configs.TryGetValue(monitorId, out var list))
        {
            list = new List<MonitorPinConfig>();
            _configs[monitorId] = list;
        }

        // De-dupe by (process, titleKey) pair
        if (list.Any(c => string.Equals(c.ProcessName, procName, StringComparison.OrdinalIgnoreCase)
                       && string.Equals(c.TitleKey, titleKey, StringComparison.OrdinalIgnoreCase)))
        {
            Logger.Log($"[MonitorPin] Already pinned on {monitorId}: {procName}/{titleKey}");
            return;
        }

        list.Add(new MonitorPinConfig { ProcessName = procName, TitleKey = titleKey });
        Logger.Log($"[MonitorPin] Added pin on {monitorId}: {procName}/{titleKey}");
        Reconcile();
        ConfigChanged?.Invoke();
    }

    /// <summary>
    /// Remove a pinned window by (process, titleKey). Triggers reconcile + ConfigChanged.
    /// </summary>
    public void RemovePin(PinnedWindow pw)
    {
        if (!_configs.TryGetValue(pw.MonitorDeviceId, out var list)) return;
        int removed = list.RemoveAll(c =>
            string.Equals(c.ProcessName, pw.ProcessName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.TitleKey, pw.TitleKey, StringComparison.OrdinalIgnoreCase));
        if (removed == 0) return;
        Logger.Log($"[MonitorPin] Removed pin on {pw.MonitorDeviceId}: {pw.ProcessName}/{pw.TitleKey}");
        Reconcile();
        ConfigChanged?.Invoke();
    }

    /// <summary>
    /// Click behaviour (same three-state toggle as QuickWindow):
    ///   - Not on current desktop → move to current + restore + foreground
    ///   - On current, minimized  → restore + foreground
    ///   - On current, visible    → minimize
    /// </summary>
    public void ActivatePin(PinnedWindow pw)
    {
        if (!NativeMethods.IsWindow(pw.Hwnd))
        {
            Logger.Log($"[MonitorPin] Dead hwnd, reconciling: {pw.ProcessName}/{pw.TitleKey}");
            Reconcile();
            return;
        }

        bool onCurrent = _vdService.IsWindowOnCurrentDesktop(pw.Hwnd);
        bool minimized = NativeMethods.IsIconic(pw.Hwnd);

        if (!onCurrent)
        {
            _vdService.MoveWindowToCurrentDesktop(pw.Hwnd);
            if (minimized)
                NativeMethods.ShowWindow(pw.Hwnd, NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(pw.Hwnd);
        }
        else if (minimized)
        {
            NativeMethods.ShowWindow(pw.Hwnd, NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(pw.Hwnd);
        }
        else
        {
            NativeMethods.ShowWindow(pw.Hwnd, NativeMethods.SW_MINIMIZE);
        }
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetProcessName(IntPtr hwnd)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    public void Dispose()
    {
        _scanTimer?.Stop();
        _scanTimer = null;
        CleanupPicker();
        s_instance = null;
    }
}
