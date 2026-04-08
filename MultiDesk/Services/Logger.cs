using System.IO;

namespace WinVDeskEssential.Services;

/// <summary>
/// Simple file logger for diagnostics. Writes to %LOCALAPPDATA%\WinVDeskEssential\debug.log
/// </summary>
public static class Logger
{
    private static readonly string LogPath;
    private static readonly object Lock = new();

    static Logger()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinVDeskEssential");
        Directory.CreateDirectory(dir);
        LogPath = Path.Combine(dir, "debug.log");

        // Truncate on startup
        File.WriteAllText(LogPath, $"=== WinVDeskEssential started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
    }

    public static void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        lock (Lock)
        {
            try { File.AppendAllText(LogPath, line + "\n"); } catch { }
        }
        System.Diagnostics.Debug.WriteLine(line);
    }
}
