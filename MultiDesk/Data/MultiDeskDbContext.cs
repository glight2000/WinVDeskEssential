using Microsoft.EntityFrameworkCore;
using System.IO;

namespace VDesk.Data;

public class VDeskDbContext : DbContext
{
    public DbSet<DesktopConfigEntity> DesktopConfigs { get; set; } = null!;
    public DbSet<HotkeyConfigEntity> HotkeyConfigs { get; set; } = null!;
    public DbSet<AppSettingsEntity> AppSettings { get; set; } = null!;

    private readonly string _dbPath;

    public VDeskDbContext()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "VDesk");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "multidesk.db");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DesktopConfigEntity>()
            .HasIndex(e => new { e.MonitorDeviceId, e.SystemDesktopId })
            .IsUnique();

        modelBuilder.Entity<HotkeyConfigEntity>()
            .HasIndex(e => e.ActionId)
            .IsUnique();

        modelBuilder.Entity<AppSettingsEntity>()
            .HasIndex(e => e.Key)
            .IsUnique();
    }
}
