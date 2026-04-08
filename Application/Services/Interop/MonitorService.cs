using WinVDeskEssential.Models;
using System.Windows;

namespace WinVDeskEssential.Services.Interop;

public class MonitorService
{
    public List<MonitorInfo> GetAllMonitors()
    {
        var monitors = new List<MonitorInfo>();

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data) =>
        {
            var info = NativeMethods.MONITORINFOEX.Create();
            if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
            {
                monitors.Add(new MonitorInfo
                {
                    Handle = hMonitor,
                    DeviceId = info.szDevice,
                    DisplayName = info.szDevice,
                    Bounds = new Rect(info.rcMonitor.Left, info.rcMonitor.Top,
                        info.rcMonitor.Right - info.rcMonitor.Left,
                        info.rcMonitor.Bottom - info.rcMonitor.Top),
                    WorkArea = new Rect(info.rcWork.Left, info.rcWork.Top,
                        info.rcWork.Right - info.rcWork.Left,
                        info.rcWork.Bottom - info.rcWork.Top),
                    IsPrimary = (info.dwFlags & 1) != 0
                });
            }
            return true;
        }, IntPtr.Zero);

        return monitors;
    }

    public MonitorInfo? GetMonitorFromPoint(int x, int y)
    {
        var pt = new NativeMethods.POINT { X = x, Y = y };
        var hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return GetAllMonitors().FirstOrDefault(m => m.Handle == hMonitor);
    }

    public MonitorInfo? GetMonitorFromCursor()
    {
        NativeMethods.GetCursorPos(out var pt);
        return GetMonitorFromPoint(pt.X, pt.Y);
    }

    public MonitorInfo? GetMonitorFromWindow(IntPtr hwnd)
    {
        var hMonitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONULL);
        if (hMonitor == IntPtr.Zero) return null;
        return GetAllMonitors().FirstOrDefault(m => m.Handle == hMonitor);
    }
}
