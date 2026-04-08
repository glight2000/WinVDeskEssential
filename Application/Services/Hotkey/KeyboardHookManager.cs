using WinVDeskEssential.Services;
using WinVDeskEssential.Services.Interop;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace WinVDeskEssential.Services.Hotkey;

public class KeyboardHookManager : IDisposable
{
    private IntPtr _hookId = IntPtr.Zero;
    private readonly Dictionary<(uint modifiers, uint vk), Action> _interceptors = new();

    // CRITICAL: The delegate must be stored in a static field AND pinned via GCHandle
    // to prevent the .NET GC from collecting or relocating it.
    // Without this, SetWindowsHookEx gets a valid handle but the callback is never invoked.
    private static NativeMethods.LowLevelKeyboardProc? s_proc;
    private static GCHandle s_gcHandle;
    private static KeyboardHookManager? s_instance;

    // Track modifier key states
    private bool _ctrlPressed;
    private bool _shiftPressed;
    private bool _winPressed;
    private bool _altPressed;
    private int _callCount;

    public void Install()
    {
        s_instance = this;
        s_proc = StaticHookCallback;
        s_gcHandle = GCHandle.Alloc(s_proc);

        var hMod = NativeMethods.GetModuleHandle("user32.dll");
        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            s_proc,
            hMod,
            0);

        if (_hookId == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            Logger.Log($"[KBHook] FAILED to install hook! Win32 error={error}");
        }
        else
        {
            Logger.Log($"[KBHook] Hook installed OK, handle=0x{_hookId:X}, delegate pinned");
        }
    }

    public void RegisterInterceptor(ModifierKeys modifiers, Key key, Action action)
    {
        uint mod = 0;
        if (modifiers.HasFlag(ModifierKeys.Control)) mod |= 0x0002;
        if (modifiers.HasFlag(ModifierKeys.Shift)) mod |= 0x0004;
        if (modifiers.HasFlag(ModifierKeys.Windows)) mod |= 0x0008;
        if (modifiers.HasFlag(ModifierKeys.Alt)) mod |= 0x0001;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        _interceptors[(mod, vk)] = action;
        Logger.Log($"[KBHook] Registered interceptor: mod=0x{mod:X4} vk=0x{vk:X2} ({key})");
    }

    public void ClearInterceptors()
    {
        _interceptors.Clear();
    }

    // Static callback to ensure the function pointer is stable
    private static IntPtr StaticHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        return s_instance?.HookCallback(nCode, wParam, lParam)
            ?? NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        var count = System.Threading.Interlocked.Increment(ref _callCount);

        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            int msg = wParam.ToInt32();
            bool isKeyDown = msg == 0x0100 /*WM_KEYDOWN*/ || msg == 0x0104 /*WM_SYSKEYDOWN*/;

            // Track modifier states
            switch (hookStruct.vkCode)
            {
                case 0xA2: case 0xA3: case 0x11: // Ctrl
                    _ctrlPressed = isKeyDown; break;
                case 0xA0: case 0xA1: case 0x10: // Shift
                    _shiftPressed = isKeyDown; break;
                case 0x5B: case 0x5C: // Win
                    _winPressed = isKeyDown; break;
                case 0xA4: case 0xA5: case 0x12: // Alt
                    _altPressed = isKeyDown; break;
            }

            // Log keydowns when Win is held (to diagnose Ctrl+Win+Arrow)
            if (isKeyDown && _winPressed)
                Logger.Log($"[KBHook] KeyDown with Win held: vk=0x{hookStruct.vkCode:X2} ctrl={_ctrlPressed} shift={_shiftPressed}");

            if (isKeyDown && _interceptors.Count > 0)
            {
                uint currentMod = 0;
                if (_ctrlPressed) currentMod |= 0x0002;
                if (_shiftPressed) currentMod |= 0x0004;
                if (_winPressed) currentMod |= 0x0008;
                if (_altPressed) currentMod |= 0x0001;

                if (_interceptors.TryGetValue((currentMod, hookStruct.vkCode), out var action))
                {
                    Logger.Log($"[KBHook] MATCH: mod=0x{currentMod:X4} vk=0x{hookStruct.vkCode:X2}");
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(action);
                    return (IntPtr)1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
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
