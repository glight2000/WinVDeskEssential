using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WinVDeskEssential.Models;

namespace WinVDeskEssential.Services.Interop;

public class WindowInfo
{
    public IntPtr Handle { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public IntPtr MonitorHandle { get; set; }
}

public class WindowEnumerator
{
    private readonly HashSet<string> _excludedProcessNames;
    private readonly HashSet<string> _excludedWindowClasses;

    public WindowEnumerator(AppSettings settings)
    {
        _excludedProcessNames = new HashSet<string>(settings.ExcludedProcessNames, StringComparer.OrdinalIgnoreCase);
        _excludedWindowClasses = new HashSet<string>(settings.ExcludedWindowClasses, StringComparer.OrdinalIgnoreCase);
    }

    public List<WindowInfo> GetWindowsOnMonitor(IntPtr monitorHandle)
    {
        var windows = new List<WindowInfo>();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!IsValidWindow(hwnd)) return true;

            var winMonitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONULL);
            if (winMonitor != monitorHandle) return true;

            var info = GetWindowInfo(hwnd);
            if (info == null) return true;

            if (_excludedProcessNames.Contains(info.ProcessName)) return true;
            if (_excludedWindowClasses.Contains(info.ClassName)) return true;

            info.MonitorHandle = monitorHandle;
            windows.Add(info);
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    public List<WindowInfo> GetAllVisibleWindows()
    {
        var windows = new List<WindowInfo>();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!IsValidWindow(hwnd)) return true;

            var info = GetWindowInfo(hwnd);
            if (info == null) return true;

            if (_excludedProcessNames.Contains(info.ProcessName)) return true;
            if (_excludedWindowClasses.Contains(info.ClassName)) return true;

            info.MonitorHandle = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONULL);
            windows.Add(info);
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    private static bool IsValidWindow(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindowVisible(hwnd)) return false;
        if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd) return false;

        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return false;

        var titleBuf = new StringBuilder(256);
        NativeMethods.GetWindowText(hwnd, titleBuf, 256);
        if (titleBuf.Length == 0) return false;

        return true;
    }

    private static WindowInfo? GetWindowInfo(IntPtr hwnd)
    {
        var titleBuf = new StringBuilder(256);
        NativeMethods.GetWindowText(hwnd, titleBuf, 256);

        var classBuf = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, classBuf, 256);

        string processName = string.Empty;
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            using var proc = Process.GetProcessById((int)pid);
            processName = proc.ProcessName;
        }
        catch { }

        return new WindowInfo
        {
            Handle = hwnd,
            Title = titleBuf.ToString(),
            ClassName = classBuf.ToString(),
            ProcessName = processName,
        };
    }
}
