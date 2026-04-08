using WinVDeskEssential.Data;
using WinVDeskEssential.Models;
using WinVDeskEssential.Services;
using WinVDeskEssential.Services.Desktop;
using System.Diagnostics;
using System.Windows.Input;

namespace WinVDeskEssential.Services.Hotkey;

public class HotkeyService : IDisposable
{
    private readonly HotkeyManager _hotkeyManager;
    private readonly KeyboardHookManager _keyboardHook;
    private readonly DesktopSwitchEngine _switchEngine;
    private readonly ConfigRepository _configRepo;
    private readonly List<HotkeyBinding> _bindings = new();

    public event Action? TogglePanelRequested;

    public HotkeyService(
        HotkeyManager hotkeyManager,
        KeyboardHookManager keyboardHook,
        DesktopSwitchEngine switchEngine,
        ConfigRepository configRepo)
    {
        _hotkeyManager = hotkeyManager;
        _keyboardHook = keyboardHook;
        _switchEngine = switchEngine;
        _configRepo = configRepo;
    }

    public List<HotkeyBinding> GetBindings() => _bindings.ToList();

    public void RegisterDefaultHotkeys()
    {
        var defaults = GetDefaultBindings();

        // Load custom bindings from DB, falling back to defaults
        var saved = _configRepo.LoadHotkeys();
        foreach (var def in defaults)
        {
            var custom = saved.FirstOrDefault(s => s.ActionId == def.ActionId);
            if (custom != null)
            {
                def.Modifiers = (ModifierKeys)custom.Modifiers;
                def.Key = (Key)custom.Key;
                def.IsEnabled = custom.IsEnabled;
            }
            _bindings.Add(def);
        }

        RegisterAllBindings();
    }

    private void RegisterAllBindings()
    {
        _hotkeyManager.UnregisterAll();
        _keyboardHook.ClearInterceptors();

        foreach (var binding in _bindings.Where(b => b.IsEnabled))
        {
            var action = GetActionForBinding(binding);
            if (action == null) continue;

            // For system desktop hotkeys (Ctrl+Win+Arrow, Ctrl+Win+D, etc.),
            // do NOT intercept — let the system handle them natively.
            // We detect the switch via polling and fix windows afterward.
            bool isSystemDesktopKey = binding.ActionId is "switch_next" or "switch_prev"
                or "new_desktop" or "close_desktop"
                or "switch_1" or "switch_2" or "switch_3" or "switch_4" or "switch_5"
                or "switch_6" or "switch_7" or "switch_8" or "switch_9";

            if (isSystemDesktopKey)
            {
                Logger.Log($"[Hotkey] SKIP system key: {binding.DisplayString} -> {binding.ActionId} (handled via poll)");
                continue;
            }

            // For non-system keys, try RegisterHotKey; fallback to LL hook
            if (_hotkeyManager.RegisterHotkey(binding, action))
            {
                Logger.Log($"[Hotkey] RegisterHotKey OK: {binding.DisplayString} -> {binding.ActionId}");
            }
            else
            {
                Logger.Log($"[Hotkey] RegisterHotKey FAILED for {binding.DisplayString}, falling back to LL hook");
                _keyboardHook.RegisterInterceptor(binding.Modifiers, binding.Key, action);
            }
        }
    }

    private Action? GetActionForBinding(HotkeyBinding binding)
    {
        return binding.ActionId switch
        {
            "switch_next" => () => _switchEngine.SwitchToNextDesktop(),
            "switch_prev" => () => _switchEngine.SwitchToPreviousDesktop(),
            "switch_1" => () => _switchEngine.SwitchToDesktopIndex(0),
            "switch_2" => () => _switchEngine.SwitchToDesktopIndex(1),
            "switch_3" => () => _switchEngine.SwitchToDesktopIndex(2),
            "switch_4" => () => _switchEngine.SwitchToDesktopIndex(3),
            "switch_5" => () => _switchEngine.SwitchToDesktopIndex(4),
            "switch_6" => () => _switchEngine.SwitchToDesktopIndex(5),
            "switch_7" => () => _switchEngine.SwitchToDesktopIndex(6),
            "switch_8" => () => _switchEngine.SwitchToDesktopIndex(7),
            "switch_9" => () => _switchEngine.SwitchToDesktopIndex(8),
            "new_desktop" => () => {
                var monitor = new Interop.MonitorService().GetMonitorFromCursor();
                if (monitor != null) _switchEngine.AddDesktop(monitor.DeviceId);
            },
            "close_desktop" => () => {
                var monitor = new Interop.MonitorService().GetMonitorFromCursor();
                if (monitor != null)
                {
                    var state = _switchEngine.GetStateForMonitor(monitor.DeviceId);
                    if (state != null) _switchEngine.RemoveDesktop(monitor.DeviceId, state.CurrentIndex);
                }
            },
            "toggle_panel" => () => TogglePanelRequested?.Invoke(),
            "move_window_next" => () => _switchEngine.MoveActiveWindowToDesktop(1),
            "move_window_prev" => () => _switchEngine.MoveActiveWindowToDesktop(-1),
            _ => null
        };
    }

    public void UpdateBinding(HotkeyBinding binding)
    {
        var existing = _bindings.FirstOrDefault(b => b.ActionId == binding.ActionId);
        if (existing != null)
        {
            existing.Modifiers = binding.Modifiers;
            existing.Key = binding.Key;
            existing.IsEnabled = binding.IsEnabled;
        }

        _configRepo.SaveHotkey(new HotkeyConfigEntity
        {
            ActionId = binding.ActionId,
            DisplayName = binding.DisplayName,
            Modifiers = (int)binding.Modifiers,
            Key = (int)binding.Key,
            IsEnabled = binding.IsEnabled,
        });

        RegisterAllBindings();
    }

    private static List<HotkeyBinding> GetDefaultBindings()
    {
        return new List<HotkeyBinding>
        {
            new() { ActionId = "switch_next", DisplayName = "Next Desktop", Modifiers = ModifierKeys.Control | ModifierKeys.Windows, Key = Key.Right },
            new() { ActionId = "switch_prev", DisplayName = "Previous Desktop", Modifiers = ModifierKeys.Control | ModifierKeys.Windows, Key = Key.Left },
            new() { ActionId = "switch_1", DisplayName = "Desktop 1", Modifiers = ModifierKeys.Control | ModifierKeys.Windows, Key = Key.D1 },
            new() { ActionId = "switch_2", DisplayName = "Desktop 2", Modifiers = ModifierKeys.Control | ModifierKeys.Windows, Key = Key.D2 },
            new() { ActionId = "switch_3", DisplayName = "Desktop 3", Modifiers = ModifierKeys.Control | ModifierKeys.Windows, Key = Key.D3 },
            new() { ActionId = "switch_4", DisplayName = "Desktop 4", Modifiers = ModifierKeys.Control | ModifierKeys.Windows, Key = Key.D4 },
            new() { ActionId = "switch_5", DisplayName = "Desktop 5", Modifiers = ModifierKeys.Control | ModifierKeys.Windows, Key = Key.D5 },
            new() { ActionId = "switch_6", DisplayName = "Desktop 6", Modifiers = ModifierKeys.Control | ModifierKeys.Windows, Key = Key.D6 },
            new() { ActionId = "switch_7", DisplayName = "Desktop 7", Modifiers = ModifierKeys.Control | ModifierKeys.Windows, Key = Key.D7 },
            new() { ActionId = "switch_8", DisplayName = "Desktop 8", Modifiers = ModifierKeys.Control | ModifierKeys.Windows, Key = Key.D8 },
            new() { ActionId = "switch_9", DisplayName = "Desktop 9", Modifiers = ModifierKeys.Control | ModifierKeys.Windows, Key = Key.D9 },
            new() { ActionId = "new_desktop", DisplayName = "New Desktop", Modifiers = ModifierKeys.Control | ModifierKeys.Windows, Key = Key.D },
            new() { ActionId = "close_desktop", DisplayName = "Close Desktop", Modifiers = ModifierKeys.Control | ModifierKeys.Windows, Key = Key.F4 },
            new() { ActionId = "toggle_panel", DisplayName = "Toggle Panel", Modifiers = ModifierKeys.Windows, Key = Key.OemTilde },
            new() { ActionId = "move_window_next", DisplayName = "Move Window Next", Modifiers = ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Windows, Key = Key.Right },
            new() { ActionId = "move_window_prev", DisplayName = "Move Window Prev", Modifiers = ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Windows, Key = Key.Left },
        };
    }

    public void Dispose()
    {
        _hotkeyManager.Dispose();
        _keyboardHook.Dispose();
    }
}
