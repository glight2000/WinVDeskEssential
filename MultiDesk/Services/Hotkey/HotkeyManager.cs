using WinVDeskEssential.Models;
using WinVDeskEssential.Services.Interop;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace WinVDeskEssential.Services.Hotkey;

public class HotkeyManager : IDisposable
{
    private readonly Dictionary<int, Action> _hotkeyActions = new();
    private readonly Dictionary<int, HotkeyBinding> _registeredHotkeys = new();
    private HwndSource? _hwndSource;
    private IntPtr _hwnd;
    private int _nextId = 0x0001;

    public void Initialize(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _hwnd = helper.EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);
    }

    public bool RegisterHotkey(HotkeyBinding binding, Action callback)
    {
        var id = _nextId++;
        uint modifiers = 0;
        if (binding.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= 0x0001;
        if (binding.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= 0x0002;
        if (binding.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= 0x0004;
        if (binding.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= 0x0008;
        modifiers |= 0x4000; // MOD_NOREPEAT

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(binding.Key);

        if (NativeMethods.RegisterHotKey(_hwnd, id, modifiers, vk))
        {
            _hotkeyActions[id] = callback;
            _registeredHotkeys[id] = binding;
            return true;
        }
        return false;
    }

    public void UnregisterAll()
    {
        foreach (var id in _hotkeyActions.Keys)
        {
            NativeMethods.UnregisterHotKey(_hwnd, id);
        }
        _hotkeyActions.Clear();
        _registeredHotkeys.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_hotkeyActions.TryGetValue(id, out var action))
            {
                action.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _hwndSource?.RemoveHook(WndProc);
    }
}
