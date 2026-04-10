using WinVDeskEssential.Services;
using WinVDeskEssential.Services.Interop;
using System.Runtime.InteropServices;
using WindowsDesktop;

namespace WinVDeskEssential.Services.Desktop;

public class VirtualDesktopService : IVirtualDesktopService, IDisposable
{
    private bool _initialized;

    // Official public COM interface for querying window-desktop associations
    [ComImport]
    [Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, out bool onCurrentDesktop);
        [PreserveSig]
        int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);
        [PreserveSig]
        int MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
    }

    [ComImport]
    [Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")]
    private class VirtualDesktopManagerClass { }

    private IVirtualDesktopManager? _vdManager;

    public event Action<Guid, Guid>? DesktopChanged;
    public event Action<Guid>? DesktopCreated;
    public event Action<Guid>? DesktopRemoved;
    public event Action<Guid, string>? DesktopRenamed;
    public event Action<Guid, int, int>? DesktopMoved;

    public VirtualDesktopService()
    {
        try
        {
            _vdManager = (IVirtualDesktopManager)new VirtualDesktopManagerClass();
            Logger.Log("[VDService] IVirtualDesktopManager COM created OK");
        }
        catch (Exception ex)
        {
            Logger.Log($"[VDService] IVirtualDesktopManager COM failed: {ex.Message}");
        }

        try
        {
            // Slions.VirtualDesktop auto-detects Windows build and loads correct COM GUIDs
            var desktops = VirtualDesktop.GetDesktops();
            _initialized = desktops.Length > 0;

            if (_initialized)
            {
                VirtualDesktop.CurrentChanged += OnSystemDesktopChanged;
                VirtualDesktop.Created += OnSystemDesktopCreated;
                VirtualDesktop.Destroyed += OnSystemDesktopDestroyed;
                VirtualDesktop.Renamed += OnSystemDesktopRenamed;
                VirtualDesktop.Moved += OnSystemDesktopMoved;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"VirtualDesktop COM init failed: {ex.Message}");
            _initialized = false;
        }
    }

    private void OnSystemDesktopChanged(object? sender, VirtualDesktopChangedEventArgs e)
    {
        DesktopChanged?.Invoke(e.OldDesktop.Id, e.NewDesktop.Id);
    }

    private void OnSystemDesktopCreated(object? sender, VirtualDesktop e)
    {
        DesktopCreated?.Invoke(e.Id);
    }

    private void OnSystemDesktopDestroyed(object? sender, VirtualDesktopDestroyEventArgs e)
    {
        DesktopRemoved?.Invoke(e.Destroyed.Id);
    }

    private void OnSystemDesktopRenamed(object? sender, VirtualDesktopRenamedEventArgs e)
    {
        DesktopRenamed?.Invoke(e.Desktop.Id, e.Name);
    }

    private void OnSystemDesktopMoved(object? sender, VirtualDesktopMovedEventArgs e)
    {
        DesktopMoved?.Invoke(e.Desktop.Id, e.OldIndex, e.NewIndex);
    }

    public List<VirtualDesktopInfo> GetAllDesktops()
    {
        if (!_initialized) return GetFallbackDesktops();

        try
        {
            return VirtualDesktop.GetDesktops()
                .Select((d, i) => new VirtualDesktopInfo
                {
                    Id = d.Id,
                    Name = d.Name ?? $"Desktop {i + 1}",
                    Index = i
                })
                .ToList();
        }
        catch
        {
            return GetFallbackDesktops();
        }
    }

    /// <summary>
    /// Get the current desktop by finding a visible window and querying its desktop ID.
    /// Only visible, non-pinned windows are on the "current" desktop.
    /// </summary>
    public VirtualDesktopInfo? GetCurrentDesktop()
    {
        if (!_initialized) return GetFallbackDesktops().FirstOrDefault();

        try
        {
            Guid currentDesktopId = Guid.Empty;

            // Strategy: find ANY visible window, check if it's on the current desktop.
            // Use IsWindowOnCurrentVirtualDesktop + GetWindowDesktopId.
            if (_vdManager != null)
            {
                // Enumerate visible windows to find one on the current desktop
                IntPtr foundHwnd = IntPtr.Zero;
                NativeMethods.EnumWindows((hwnd, _) =>
                {
                    if (!NativeMethods.IsWindowVisible(hwnd)) return true;
                    if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd) return true;

                    int hr = _vdManager.IsWindowOnCurrentVirtualDesktop(hwnd, out bool onCurrent);
                    if (hr == 0 && onCurrent)
                    {
                        int hr2 = _vdManager.GetWindowDesktopId(hwnd, out var dId);
                        if (hr2 == 0 && dId != Guid.Empty)
                        {
                            currentDesktopId = dId;
                            foundHwnd = hwnd;
                            return false; // Stop enumeration
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }

            // Fallback to library
            if (currentDesktopId == Guid.Empty)
                currentDesktopId = VirtualDesktop.Current.Id;

            // Match to known desktops
            var all = VirtualDesktop.GetDesktops();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].Id == currentDesktopId)
                {
                    return new VirtualDesktopInfo
                    {
                        Id = all[i].Id,
                        Name = all[i].Name ?? $"Desktop {i + 1}",
                        Index = i
                    };
                }
            }

            return new VirtualDesktopInfo { Id = currentDesktopId, Name = "Unknown", Index = -1 };
        }
        catch
        {
            return GetFallbackDesktops().FirstOrDefault();
        }
    }

    public VirtualDesktopInfo? GetDesktopById(Guid id)
    {
        return GetAllDesktops().FirstOrDefault(d => d.Id == id);
    }

    public void SwitchToDesktop(Guid desktopId)
    {
        if (!_initialized) return;

        try
        {
            var desktop = VirtualDesktop.GetDesktops().FirstOrDefault(d => d.Id == desktopId);
            desktop?.Switch();
        }
        catch (Exception ex)
        {
            Logger.Log($"SwitchToDesktop failed: {ex.Message}");
        }
    }

    public VirtualDesktopInfo CreateDesktop()
    {
        if (_initialized)
        {
            try
            {
                var desktop = VirtualDesktop.Create();
                var all = VirtualDesktop.GetDesktops();
                return new VirtualDesktopInfo
                {
                    Id = desktop.Id,
                    Name = desktop.Name ?? $"Desktop {all.Length}",
                    Index = all.Length - 1
                };
            }
            catch { }
        }

        // Fallback
        return new VirtualDesktopInfo
        {
            Id = Guid.NewGuid(),
            Name = "New Desktop",
            Index = -1
        };
    }

    public void RemoveDesktop(Guid desktopId)
    {
        if (!_initialized) return;

        try
        {
            var desktop = VirtualDesktop.GetDesktops().FirstOrDefault(d => d.Id == desktopId);
            desktop?.Remove();
        }
        catch (Exception ex)
        {
            Logger.Log($"RemoveDesktop failed: {ex.Message}");
        }
    }

    public void RenameDesktop(Guid desktopId, string name)
    {
        if (!_initialized) return;

        try
        {
            var desktop = VirtualDesktop.GetDesktops().FirstOrDefault(d => d.Id == desktopId);
            if (desktop != null)
            {
                desktop.Name = name;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"RenameDesktop failed: {ex.Message}");
        }
    }

    public void MoveWindowToDesktop(IntPtr hwnd, Guid desktopId)
    {
        if (!_initialized) return;

        try
        {
            var desktop = VirtualDesktop.GetDesktops().FirstOrDefault(d => d.Id == desktopId);
            if (desktop != null)
            {
                VirtualDesktop.MoveToDesktop(hwnd, desktop);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"MoveWindowToDesktop failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Reliably moves a window to whichever desktop is currently displayed.
    /// Uses the pin/unpin trick: pinning a window shows it on all desktops,
    /// unpinning then "grounds" it on the currently-viewed desktop.
    /// This avoids the race where SetForegroundWindow on a foreign-desktop
    /// window would otherwise switch the display instead of moving the window.
    /// </summary>
    public void MoveWindowToCurrentDesktop(IntPtr hwnd)
    {
        if (!_initialized) return;

        try
        {
            // Fast path: if window is already on current desktop, nothing to do.
            var current = VirtualDesktop.Current;
            var windowDesktop = VirtualDesktop.FromHwnd(hwnd);
            if (windowDesktop != null && current != null && windowDesktop.Id == current.Id)
                return;

            // Pin/unpin trick — leaves the window on the current desktop.
            VirtualDesktop.PinWindow(hwnd);
            VirtualDesktop.UnpinWindow(hwnd);
        }
        catch (Exception ex)
        {
            Logger.Log($"MoveWindowToCurrentDesktop failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns true if the window is visible on the currently-displayed virtual desktop
    /// (i.e. either on the current desktop, or pinned to all desktops).
    /// </summary>
    public bool IsWindowOnCurrentDesktop(IntPtr hwnd)
    {
        if (!_initialized || hwnd == IntPtr.Zero) return false;

        // Preferred: use the public IVirtualDesktopManager COM interface
        if (_vdManager != null)
        {
            try
            {
                int hr = _vdManager.IsWindowOnCurrentVirtualDesktop(hwnd, out bool onCurrent);
                if (hr == 0) return onCurrent;
            }
            catch { /* fall through */ }
        }

        // Fallback: compare desktop IDs
        try
        {
            var current = VirtualDesktop.Current;
            var windowDesktop = VirtualDesktop.FromHwnd(hwnd);
            if (current == null) return false;
            // Pinned windows have no specific desktop — treat as "on current"
            if (windowDesktop == null) return true;
            return windowDesktop.Id == current.Id;
        }
        catch
        {
            return false;
        }
    }

    public Guid? GetDesktopIdForWindow(IntPtr hwnd)
    {
        if (!_initialized) return null;

        try
        {
            var desktop = VirtualDesktop.FromHwnd(hwnd);
            return desktop?.Id;
        }
        catch
        {
            return null;
        }
    }

    private static List<VirtualDesktopInfo> GetFallbackDesktops()
    {
        return new List<VirtualDesktopInfo>
        {
            new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), Name = "Desktop 1", Index = 0 },
            new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), Name = "Desktop 2", Index = 1 },
        };
    }

    public void Dispose()
    {
        if (_initialized)
        {
            VirtualDesktop.CurrentChanged -= OnSystemDesktopChanged;
            VirtualDesktop.Created -= OnSystemDesktopCreated;
            VirtualDesktop.Destroyed -= OnSystemDesktopDestroyed;
            VirtualDesktop.Renamed -= OnSystemDesktopRenamed;
            VirtualDesktop.Moved -= OnSystemDesktopMoved;
        }
    }
}
