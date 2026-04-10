using WinVDeskEssential.Models;
using WinVDeskEssential.Services.Desktop;
using WinVDeskEssential.Services.Interop;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WinVDeskEssential.Services.QuickWindow;

/// <summary>
/// Manages a user-curated list of "quick-access" windows.
/// - PickWindow(): enters picker mode; next click on any window captures it.
/// - ActivateWindow(): moves the target window to the current virtual desktop and brings it to front.
/// - Uses a temporary low-level mouse hook for click picking, and ESC key-state polling to cancel.
/// </summary>
public class QuickWindowService : IDisposable
{
    private readonly IVirtualDesktopService _vdService;

    public ObservableCollection<Models.QuickWindow> Windows { get; } = new();

    public bool IsPicking { get; private set; }
    public event Action? PickingStateChanged;

    // Picker hook state
    private IntPtr _pickHookId = IntPtr.Zero;
    private static NativeMethods.LowLevelMouseProc? s_pickProc;
    private static GCHandle s_pickGcHandle;
    private static QuickWindowService? s_instance;
    private System.Windows.Threading.DispatcherTimer? _escTimer;

    // HWNDs belonging to our own process — ignored by the picker
    private readonly HashSet<IntPtr> _ownHwnds = new();

    public QuickWindowService(IVirtualDesktopService vdService)
    {
        _vdService = vdService;
    }

    public void RegisterOwnWindow(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero) _ownHwnds.Add(hwnd);
    }

    /// <summary>
    /// Enter picker mode. The next click on a foreign top-level window adds it.
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

        // Poll ESC to cancel (can't use keyboard hook here because it's separate)
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

        // Capture this window
        var pickedHwnd = hwnd;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            AddWindow(pickedHwnd);
            CleanupPicker();
            PickingStateChanged?.Invoke();
        });

        // Swallow the click so the target window isn't activated incorrectly
        return (IntPtr)1;
    }

    private void AddWindow(IntPtr hwnd)
    {
        // De-dupe
        if (Windows.Any(w => w.Hwnd == hwnd))
        {
            Logger.Log($"[QuickWindow] Already in list, skipping hwnd=0x{hwnd:X}");
            return;
        }

        var title = GetWindowTitle(hwnd);
        var proc = GetProcessName(hwnd);
        var display = !string.IsNullOrEmpty(proc) ? proc : title;

        Windows.Add(new Models.QuickWindow
        {
            Hwnd = hwnd,
            Title = display,
            ProcessName = proc,
        });
        Logger.Log($"[QuickWindow] Added: '{display}' hwnd=0x{hwnd:X}");
    }

    public void RemoveWindow(Models.QuickWindow qw)
    {
        Windows.Remove(qw);
    }

    /// <summary>
    /// Toggle the quick window's visibility on the current desktop:
    ///   - Not on current desktop  → move to current + restore + foreground
    ///   - On current, minimized   → restore + foreground
    ///   - On current, visible     → minimize
    /// Removes the entry if the window is no longer valid.
    /// </summary>
    public void ActivateWindow(Models.QuickWindow qw)
    {
        if (!NativeMethods.IsWindow(qw.Hwnd))
        {
            Logger.Log($"[QuickWindow] Dead window, removing: hwnd=0x{qw.Hwnd:X}");
            Windows.Remove(qw);
            return;
        }

        bool onCurrent = _vdService.IsWindowOnCurrentDesktop(qw.Hwnd);
        bool minimized = NativeMethods.IsIconic(qw.Hwnd);

        if (!onCurrent)
        {
            // Case 1: Bring to current desktop
            _vdService.MoveWindowToCurrentDesktop(qw.Hwnd);
            if (minimized)
                NativeMethods.ShowWindow(qw.Hwnd, NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(qw.Hwnd);
            Logger.Log($"[QuickWindow] Brought '{qw.Title}' to current desktop");
        }
        else if (minimized)
        {
            // Case 2: Restore minimized window on current desktop
            NativeMethods.ShowWindow(qw.Hwnd, NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(qw.Hwnd);
            Logger.Log($"[QuickWindow] Restored '{qw.Title}'");
        }
        else
        {
            // Case 3: Visible on current → minimize
            NativeMethods.ShowWindow(qw.Hwnd, NativeMethods.SW_MINIMIZE);
            Logger.Log($"[QuickWindow] Minimized '{qw.Title}'");
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
            var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    public void Dispose()
    {
        CleanupPicker();
        s_instance = null;
    }
}
