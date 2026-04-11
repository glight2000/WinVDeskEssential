using WinVDeskEssential.Models;
using WinVDeskEssential.Services.Desktop;
using WinVDeskEssential.Services.Interop;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WinVDeskEssential.Services.QuickWindow;

/// <summary>
/// Manages a persistent, process-name-driven list of quick-access windows.
///
/// Unified model:
///   - The list of "pinned apps" is a set of process names (case-insensitive).
///   - A scanner periodically binds each name to the currently-running process's
///     main window. Entries survive app exit/restart — the scanner re-binds on relaunch.
///   - Clicking "+" and picking a window adds its process name to the set.
///   - Right-clicking an entry removes its process name from the set.
///   - Settings UI can edit the set directly.
///
/// Callers are notified via <see cref="AutoListChanged"/> whenever the set changes
/// from within the service (picker / remove), so AppSettings can be updated and persisted.
/// </summary>
public class QuickWindowService : IDisposable
{
    private readonly IVirtualDesktopService _vdService;

    public ObservableCollection<Models.QuickWindow> Windows { get; } = new();

    public bool IsPicking { get; private set; }
    public event Action? PickingStateChanged;

    /// <summary>
    /// Raised when the underlying process-name set changes via a user action
    /// (picker added or right-click removed). Not raised when SetAutoProcessNames
    /// is called externally (the caller already knows).
    /// </summary>
    public event Action? AutoListChanged;

    // Picker hook state
    private IntPtr _pickHookId = IntPtr.Zero;
    private static NativeMethods.LowLevelMouseProc? s_pickProc;
    private static GCHandle s_pickGcHandle;
    private static QuickWindowService? s_instance;
    private System.Windows.Threading.DispatcherTimer? _escTimer;

    // HWNDs belonging to our own process — ignored by the picker
    private readonly HashSet<IntPtr> _ownHwnds = new();

    // The persistent process-name set (case-insensitive)
    private readonly HashSet<string> _autoNames = new(StringComparer.OrdinalIgnoreCase);
    private System.Windows.Threading.DispatcherTimer? _scanTimer;

    public QuickWindowService(IVirtualDesktopService vdService)
    {
        _vdService = vdService;
    }

    /// <summary>
    /// Replace the set of process names to auto-pin. Triggers an immediate reconcile.
    /// Does NOT raise AutoListChanged (caller is the source of truth).
    /// </summary>
    public void SetAutoProcessNames(IEnumerable<string> names)
    {
        _autoNames.Clear();
        foreach (var n in names)
        {
            if (!string.IsNullOrWhiteSpace(n))
                _autoNames.Add(n.Trim());
        }
        Logger.Log($"[QuickWindow] Auto process names set: {string.Join(",", _autoNames)}");
        ReconcileAutoWindows();
    }

    /// <summary>
    /// Return a snapshot of the current process-name set (for persistence).
    /// </summary>
    public List<string> GetAutoProcessNames() => _autoNames.ToList();

    /// <summary>
    /// Start the periodic scanner. Polls every 2 seconds to rebind after
    /// app exit / restart and to pick up newly launched matching apps.
    /// </summary>
    public void StartAutoScanner()
    {
        if (_scanTimer != null) return;
        _scanTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _scanTimer.Tick += (_, _) => ReconcileAutoWindows();
        _scanTimer.Start();
        Logger.Log("[QuickWindow] Auto-scanner started (2s interval)");
    }

    /// <summary>
    /// Reconcile the UI list with the name set and currently-running processes:
    ///   - Drop entries whose process no longer has a valid main window
    ///   - Drop entries whose process name is no longer in the set
    ///   - Add entries for names in the set that don't have a live binding yet
    /// </summary>
    private void ReconcileAutoWindows()
    {
        // 1. Drop entries whose hwnd is dead OR whose name was removed from the set
        for (int i = Windows.Count - 1; i >= 0; i--)
        {
            var qw = Windows[i];
            bool dead = !NativeMethods.IsWindow(qw.Hwnd);
            bool orphaned = !_autoNames.Contains(qw.ProcessName);
            if (dead || orphaned)
            {
                Logger.Log($"[QuickWindow] Dropping '{qw.ProcessName}' (dead={dead}, orphaned={orphaned})");
                Windows.RemoveAt(i);
            }
        }

        if (_autoNames.Count == 0) return;

        // 2. Build covered set
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in Windows)
            covered.Add(w.ProcessName);

        // 3. For each missing name, try to bind to a running process
        foreach (var name in _autoNames)
        {
            if (covered.Contains(name)) continue;

            var hwnd = FindTopLevelWindowForProcess(name);
            if (hwnd == IntPtr.Zero) continue;

            Windows.Add(new Models.QuickWindow
            {
                Hwnd = hwnd,
                ProcessName = name,
                Title = name,
            });
            Logger.Log($"[QuickWindow] Bound '{name}' hwnd=0x{hwnd:X}");
        }
    }

    /// <summary>
    /// Find a top-level window for any running process with the given name.
    /// Uses Process.MainWindowHandle — the first visible top-level window with a caption.
    /// </summary>
    private static IntPtr FindTopLevelWindowForProcess(string processName)
    {
        try
        {
            var processes = Process.GetProcessesByName(processName);
            foreach (var p in processes)
            {
                try
                {
                    var hwnd = p.MainWindowHandle;
                    if (hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd))
                        return hwnd;
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[QuickWindow] FindTopLevelWindowForProcess('{processName}') failed: {ex.Message}");
        }
        return IntPtr.Zero;
    }

    public void RegisterOwnWindow(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero) _ownHwnds.Add(hwnd);
    }

    /// <summary>
    /// Enter picker mode. The next click on a foreign top-level window adds its process name.
    /// Press ESC to cancel.
    /// </summary>
    public void StartPicking()
    {
        if (IsPicking) return;

        s_instance = this;
        s_pickProc = StaticPickCallback;
        s_pickGcHandle = GCHandle.Alloc(s_pickProc);

        var hMod = NativeMethods.GetModuleHandle("user32.dll");
        _pickHookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            s_pickProc,
            hMod,
            0);

        if (_pickHookId == IntPtr.Zero)
        {
            Logger.Log($"[QuickWindow] Failed to install picker hook, error={Marshal.GetLastWin32Error()}");
            CleanupPicker();
            return;
        }

        IsPicking = true;
        PickingStateChanged?.Invoke();
        Logger.Log("[QuickWindow] Picker mode started");

        // Poll ESC to cancel
        _escTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _escTimer.Tick += (_, _) =>
        {
            if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8000) != 0)
            {
                Logger.Log("[QuickWindow] Picker cancelled by ESC");
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
        if (hwnd == IntPtr.Zero)
            return NativeMethods.CallNextHookEx(_pickHookId, nCode, wParam, lParam);

        hwnd = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (hwnd == IntPtr.Zero)
            return NativeMethods.CallNextHookEx(_pickHookId, nCode, wParam, lParam);

        // Ignore clicks on our own windows (panel, overlay, tray, etc.)
        if (_ownHwnds.Contains(hwnd))
            return NativeMethods.CallNextHookEx(_pickHookId, nCode, wParam, lParam);

        // Also ignore ToolWindows (avoid grabbing our own overlays even if not registered)
        var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
            return NativeMethods.CallNextHookEx(_pickHookId, nCode, wParam, lParam);

        var pickedHwnd = hwnd;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            AddPickedWindow(pickedHwnd);
            CleanupPicker();
            PickingStateChanged?.Invoke();
        });

        // Swallow the click so the target window isn't activated by the OS
        return (IntPtr)1;
    }

    private void AddPickedWindow(IntPtr hwnd)
    {
        var proc = GetProcessName(hwnd);
        if (string.IsNullOrEmpty(proc))
        {
            Logger.Log($"[QuickWindow] Could not resolve process for hwnd=0x{hwnd:X}, ignoring");
            return;
        }

        // De-dupe by process name
        if (_autoNames.Contains(proc))
        {
            Logger.Log($"[QuickWindow] Process '{proc}' already pinned, skipping");
            return;
        }

        _autoNames.Add(proc);
        Logger.Log($"[QuickWindow] Added '{proc}' to auto list (from picker)");
        ReconcileAutoWindows();
        AutoListChanged?.Invoke();
    }

    /// <summary>
    /// Remove a quick window by dropping its process name from the persistent set.
    /// Triggers reconcile + AutoListChanged so callers can persist.
    /// </summary>
    public void RemoveWindow(Models.QuickWindow qw)
    {
        if (!_autoNames.Remove(qw.ProcessName))
        {
            // Not in the set — just remove from UI (shouldn't happen in unified model)
            Windows.Remove(qw);
            return;
        }
        Logger.Log($"[QuickWindow] Removed '{qw.ProcessName}' from auto list");
        ReconcileAutoWindows();
        AutoListChanged?.Invoke();
    }

    /// <summary>
    /// Toggle the quick window's visibility on the current desktop:
    ///   - Not on current desktop  → move to current + restore + foreground
    ///   - On current, minimized   → restore + foreground
    ///   - On current, visible     → minimize
    /// If the hwnd is dead, reconcile immediately (scanner may find a new main window).
    /// </summary>
    public void ActivateWindow(Models.QuickWindow qw)
    {
        if (!NativeMethods.IsWindow(qw.Hwnd))
        {
            Logger.Log($"[QuickWindow] Dead hwnd, reconciling: '{qw.ProcessName}'");
            ReconcileAutoWindows();
            return;
        }

        bool onCurrent = _vdService.IsWindowOnCurrentDesktop(qw.Hwnd);
        bool minimized = NativeMethods.IsIconic(qw.Hwnd);

        if (!onCurrent)
        {
            _vdService.MoveWindowToCurrentDesktop(qw.Hwnd);
            if (minimized)
                NativeMethods.ShowWindow(qw.Hwnd, NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(qw.Hwnd);
            Logger.Log($"[QuickWindow] Brought '{qw.Title}' to current desktop");
        }
        else if (minimized)
        {
            NativeMethods.ShowWindow(qw.Hwnd, NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(qw.Hwnd);
            Logger.Log($"[QuickWindow] Restored '{qw.Title}'");
        }
        else
        {
            NativeMethods.ShowWindow(qw.Hwnd, NativeMethods.SW_MINIMIZE);
            Logger.Log($"[QuickWindow] Minimized '{qw.Title}'");
        }
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
