using System.ComponentModel.DataAnnotations;

namespace WinVDeskEssential.Data;

public class AppSettingsEntity
{
    [Key]
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
