using WinVDeskEssential.Services.Interop;
using System.Runtime.InteropServices;

namespace WinVDeskEssential.Services.WindowDrag;

/// <summary>
/// AltDrag-like window manipulation:
///   Alt + Left-click drag  → move window
///   Alt + Right-click drag → resize window
/// Uses a low-level mouse hook to intercept mouse events when Alt is held.
/// </summary>
public class WindowDragService : IDisposable
{
    private IntPtr _hookId = IntPtr.Zero;

    // Static prevent GC
    private static NativeMethods.LowLevelMouseProc? s_proc;
    private static GCHandle s_gcHandle;
    private static WindowDragService? s_instance;

    // Drag state
    private bool _isDragging;
    private bool _isResizing;
    private IntPtr _targetHwnd;
    private NativeMethods.POINT _dragStart;
    private NativeMethods.RECT _windowStartRect;

    // Resize: which edges to move (determined by cursor position within the window)
    private bool _resizeLeft, _resizeTop, _resizeRight, _resizeBottom;

    private const int MinWindowSize = 100;

    public bool Enabled { get; set; } = true;

    public void Install()
    {
        s_instance = this;
        s_proc = StaticHookCallback;
        s_gcHandle = GCHandle.Alloc(s_proc);

        var hMod = NativeMethods.GetModuleHandle("user32.dll");
        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            s_proc,
            hMod,
            0);

        if (_hookId == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            Logger.Log($"[AltDrag] FAILED to install mouse hook! Win32 error={error}");
        }
        else
        {
            Logger.Log($"[AltDrag] Mouse hook installed OK, handle=0x{_hookId:X}");
        }
    }

    private static IntPtr StaticHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        return s_instance?.HookCallback(nCode, wParam, lParam)
            ?? NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || !Enabled)
            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);

        int msg = wParam.ToInt32();
        var mouseData = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

        // Check if Alt is currently held
        bool altDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0;

        switch (msg)
        {
            case NativeMethods.WM_LBUTTONDOWN when altDown:
                if (BeginDrag(mouseData.pt, isResize: false))
                    return (IntPtr)1; // swallow
                break;

            case NativeMethods.WM_RBUTTONDOWN when altDown:
                if (BeginDrag(mouseData.pt, isResize: true))
                    return (IntPtr)1;
                break;

            case NativeMethods.WM_MOUSEMOVE:
                if (_isDragging || _isResizing)
                {
                    OnMouseMove(mouseData.pt);
                    // Don't swallow — let cursor keep moving
                }
                break;

            case NativeMethods.WM_LBUTTONUP:
                if (_isDragging) { EndDrag(); return (IntPtr)1; }
                break;

            case NativeMethods.WM_RBUTTONUP:
                if (_isResizing) { EndDrag(); return (IntPtr)1; }
                break;
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool BeginDrag(NativeMethods.POINT pt, bool isResize)
    {
        // Find the top-level window under the cursor
        var hwnd = NativeMethods.WindowFromPoint(pt);
        if (hwnd == IntPtr.Zero) return false;

        // Walk up to the root/top-level window
        hwnd = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (hwnd == IntPtr.Zero) return false;

        // Skip our own windows and the desktop/taskbar
        var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return false;

        if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return false;

        // If the window is maximized, restore it first so we can move/resize it
        if (NativeMethods.IsZoomed(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            if (!NativeMethods.GetWindowRect(hwnd, out rect)) return false;
        }

        _targetHwnd = hwnd;
        _dragStart = pt;
        _windowStartRect = rect;

        if (isResize)
        {
            _isResizing = true;
            _isDragging = false;

            // Determine which quadrant the cursor is in to decide resize edges
            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;
            int relX = pt.X - rect.Left;
            int relY = pt.Y - rect.Top;

            _resizeLeft = relX < w / 2;
            _resizeRight = !_resizeLeft;
            _resizeTop = relY < h / 2;
            _resizeBottom = !_resizeTop;
        }
        else
        {
            _isDragging = true;
            _isResizing = false;
        }

        return true;
    }

    private void OnMouseMove(NativeMethods.POINT pt)
    {
        int dx = pt.X - _dragStart.X;
        int dy = pt.Y - _dragStart.Y;

        if (_isDragging)
        {
            int newX = _windowStartRect.Left + dx;
            int newY = _windowStartRect.Top + dy;
            int w = _windowStartRect.Right - _windowStartRect.Left;
            int h = _windowStartRect.Bottom - _windowStartRect.Top;
            NativeMethods.MoveWindow(_targetHwnd, newX, newY, w, h, true);
        }
        else if (_isResizing)
        {
            int left = _windowStartRect.Left;
            int top = _windowStartRect.Top;
            int right = _windowStartRect.Right;
            int bottom = _windowStartRect.Bottom;

            if (_resizeLeft) left += dx;
            if (_resizeRight) right += dx;
            if (_resizeTop) top += dy;
            if (_resizeBottom) bottom += dy;

            // Enforce minimum size
            if (right - left < MinWindowSize)
            {
                if (_resizeLeft) left = right - MinWindowSize;
                else right = left + MinWindowSize;
            }
            if (bottom - top < MinWindowSize)
            {
                if (_resizeTop) top = bottom - MinWindowSize;
                else bottom = top + MinWindowSize;
            }

            NativeMethods.MoveWindow(_targetHwnd, left, top, right - left, bottom - top, true);
        }
    }

    private void EndDrag()
    {
        _isDragging = false;
        _isResizing = false;
        _targetHwnd = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        if (s_gcHandle.IsAllocated)
            s_gcHandle.Free();
        s_instance = null;
        s_proc = null;
    }
}
