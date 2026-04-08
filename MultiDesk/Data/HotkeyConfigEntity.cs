using System.ComponentModel.DataAnnotations;

namespace WinVDeskEssential.Data;

public class HotkeyConfigEntity
{
    [Key]
    public int Id { get; set; }
    public string ActionId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Modifiers { get; set; }
    public int Key { get; set; }
    public bool IsEnabled { get; set; } = true;
}
