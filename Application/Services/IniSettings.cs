using WinVDeskEssential.Models;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace WinVDeskEssential.Services;

/// <summary>
/// Persists AppSettings to a .ini file next to the executable.
/// Format: key=value, one per line. Simple and human-editable.
/// </summary>
public static class IniSettings
{
    private static string GetIniPath()
    {
        var exePath = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "WinVDeskEssential.exe");
        return Path.ChangeExtension(exePath, ".ini");
    }

    public static void Load(AppSettings settings)
    {
        var path = GetIniPath();
        if (!File.Exists(path)) return;

        try
        {
            var lines = File.ReadAllLines(path);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith(';'))
                    continue;
                var eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;
                dict[trimmed[..eq].Trim()] = trimmed[(eq + 1)..].Trim();
            }

            if (dict.TryGetValue("PanelDockPosition", out var v))
                if (Enum.TryParse<DockPosition>(v, true, out var dp)) settings.PanelDockPosition = dp;
            if (dict.TryGetValue("PanelLeft", out v))
                if (double.TryParse(v, CultureInfo.InvariantCulture, out var d)) settings.PanelLeft = d;
            if (dict.TryGetValue("PanelTop", out v))
                if (double.TryParse(v, CultureInfo.InvariantCulture, out var d)) settings.PanelTop = d;
            if (dict.TryGetValue("WatermarkAlwaysOn", out v))
                if (bool.TryParse(v, out var b)) settings.WatermarkAlwaysOn = b;
            if (dict.TryGetValue("WatermarkPosition", out v))
                if (Enum.TryParse<CornerPosition>(v, true, out var cp)) settings.WatermarkPosition = cp;
            if (dict.TryGetValue("WatermarkFontSize", out v))
                if (double.TryParse(v, CultureInfo.InvariantCulture, out var d)) settings.WatermarkFontSize = d;
            if (dict.TryGetValue("WatermarkOpacity", out v))
                if (double.TryParse(v, CultureInfo.InvariantCulture, out var d)) settings.WatermarkOpacity = d;
            if (dict.TryGetValue("WatermarkMargin", out v))
                if (double.TryParse(v, CultureInfo.InvariantCulture, out var d)) settings.WatermarkMargin = d;
            if (dict.TryGetValue("AltDragEnabled", out v))
                if (bool.TryParse(v, out var b)) settings.AltDragEnabled = b;
            if (dict.TryGetValue("AutoQuickWindows", out v))
            {
                settings.AutoQuickWindowProcessNames = v
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
            }

            Logger.Log($"[Settings] Loaded from {path}");
        }
        catch (Exception ex)
        {
            Logger.Log($"[Settings] Load failed: {ex.Message}");
        }
    }

    public static void Save(AppSettings settings)
    {
        var path = GetIniPath();
        try
        {
            var ci = CultureInfo.InvariantCulture;
            var lines = new[]
            {
                "# WinVDeskEssential Settings",
                "",
                "# Panel",
                $"PanelDockPosition={settings.PanelDockPosition}",
                $"PanelLeft={settings.PanelLeft.ToString(ci)}",
                $"PanelTop={settings.PanelTop.ToString(ci)}",
                "",
                "# Watermark",
                $"WatermarkAlwaysOn={settings.WatermarkAlwaysOn}",
                $"WatermarkPosition={settings.WatermarkPosition}",
                $"WatermarkFontSize={settings.WatermarkFontSize.ToString(ci)}",
                $"WatermarkOpacity={settings.WatermarkOpacity.ToString(ci)}",
                $"WatermarkMargin={settings.WatermarkMargin.ToString(ci)}",
                "",
                "# AltDrag",
                $"AltDragEnabled={settings.AltDragEnabled}",
                "",
                "# Auto Quick Windows (comma-separated process names without .exe)",
                $"AutoQuickWindows={string.Join(",", settings.AutoQuickWindowProcessNames)}",
            };
            File.WriteAllLines(path, lines);
        }
        catch (Exception ex)
        {
            Logger.Log($"[Settings] Save failed: {ex.Message}");
        }
    }
}
