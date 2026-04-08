using System.ComponentModel.DataAnnotations;

namespace VDesk.Data;

public class AppSettingsEntity
{
    [Key]
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
