namespace WinVDeskEssential.Services.Desktop;

public class VirtualDesktopInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Index { get; set; }
}

public interface IVirtualDesktopService
{
    List<VirtualDesktopInfo> GetAllDesktops();
    VirtualDesktopInfo? GetCurrentDesktop();
    VirtualDesktopInfo? GetDesktopById(Guid id);
    void SwitchToDesktop(Guid desktopId);
    VirtualDesktopInfo CreateDesktop();
    void RemoveDesktop(Guid desktopId);
    void RenameDesktop(Guid desktopId, string name);
    void MoveWindowToDesktop(IntPtr hwnd, Guid desktopId);
    void MoveWindowToCurrentDesktop(IntPtr hwnd);
    bool IsWindowOnCurrentDesktop(IntPtr hwnd);
    Guid? GetDesktopIdForWindow(IntPtr hwnd);

    event Action<Guid, Guid>? DesktopChanged; // oldId, newId
    event Action<Guid>? DesktopCreated;
    event Action<Guid>? DesktopRemoved;
    event Action<Guid, string>? DesktopRenamed;
    event Action<Guid, int, int>? DesktopMoved; // desktopId, oldIndex, newIndex
}
