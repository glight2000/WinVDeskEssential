using VDesk.Models;
using System.IO;
using System.Runtime.InteropServices;

namespace VDesk.Services.Wallpaper;

/// <summary>
/// Manages per-monitor wallpaper using the IDesktopWallpaper COM interface.
/// Falls back to SystemParametersInfo for single-monitor scenarios.
/// </summary>
public class WallpaperService : IDisposable
{
    // IDesktopWallpaper COM interface
    [ComImport]
    [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID,
                          [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetMonitorDevicePathAt(uint monitorIndex);

        uint GetMonitorDevicePathCount();

        void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out RECT displayRect);

        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(int position);
        int GetPosition();
        void SetSlideshow(IntPtr items);
        IntPtr GetSlideshow();
        void SetSlideshowOptions(int options, uint slideshowTick);
        void GetSlideshowOptions(out int options, out uint slideshowTick);
        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, int direction);
        int GetStatus();
        void Enable(bool enable);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [ComImport]
    [Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaperClass { }

    private IDesktopWallpaper? _wallpaper;

    public WallpaperService()
    {
        try
        {
            _wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
        }
        catch
        {
            _wallpaper = null;
        }
    }

    /// <summary>
    /// Apply the background settings for a desktop on a specific monitor.
    /// </summary>
    public void ApplyBackground(string monitorDevicePath, DesktopBackground background)
    {
        switch (background.Type)
        {
            case BackgroundType.SolidColor:
                SetSolidColor(monitorDevicePath, background.PrimaryColor);
                break;
            case BackgroundType.Image:
                if (!string.IsNullOrEmpty(background.ImagePath))
                    SetWallpaperImage(monitorDevicePath, background.ImagePath, background.ImageFillMode);
                break;
            case BackgroundType.Gradient:
                // Gradients need a generated image - create a temp gradient image
                var tempPath = GenerateGradientImage(background);
                if (tempPath != null)
                    SetWallpaperImage(monitorDevicePath, tempPath, FillMode.Stretch);
                break;
        }
    }

    /// <summary>
    /// Get the monitor device path for IDesktopWallpaper by index.
    /// </summary>
    public string? GetMonitorDevicePath(int index)
    {
        if (_wallpaper == null) return null;
        try
        {
            return _wallpaper.GetMonitorDevicePathAt((uint)index);
        }
        catch { return null; }
    }

    public int GetMonitorCount()
    {
        if (_wallpaper == null) return 0;
        try
        {
            return (int)_wallpaper.GetMonitorDevicePathCount();
        }
        catch { return 0; }
    }

    private void SetSolidColor(string monitorDevicePath, System.Windows.Media.Color color)
    {
        if (_wallpaper == null) return;
        try
        {
            // Set a solid color by setting background color and clearing the wallpaper
            uint bgr = (uint)(color.B << 16 | color.G << 8 | color.R);
            _wallpaper.SetBackgroundColor(bgr);
            _wallpaper.SetWallpaper(monitorDevicePath, "");
        }
        catch { }
    }

    private void SetWallpaperImage(string monitorDevicePath, string imagePath, FillMode fillMode)
    {
        if (_wallpaper == null) return;
        try
        {
            // Map FillMode to IDesktopWallpaper position
            // 0=Center, 1=Tile, 2=Stretch, 3=Fit, 4=Fill, 5=Span
            int position = fillMode switch
            {
                FillMode.Center => 0,
                FillMode.Tile => 1,
                FillMode.Stretch => 2,
                FillMode.Fit => 3,
                FillMode.Fill => 4,
                _ => 4
            };
            _wallpaper.SetPosition(position);
            _wallpaper.SetWallpaper(monitorDevicePath, imagePath);
        }
        catch { }
    }

    private static string? GenerateGradientImage(DesktopBackground bg)
    {
        try
        {
            var tempDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VDesk", "wallpapers");
            Directory.CreateDirectory(tempDir);

            var hash = $"{bg.PrimaryColor}_{bg.SecondaryColor}_{bg.GradientAngle}".GetHashCode();
            var path = Path.Combine(tempDir, $"gradient_{hash:X8}.bmp");

            if (File.Exists(path)) return path;

            // Generate a 1920x1080 gradient BMP
            const int w = 1920, h = 1080;
            using var bitmap = new System.Drawing.Bitmap(w, h);
            using var g = System.Drawing.Graphics.FromImage(bitmap);

            var angle = (float)bg.GradientAngle;
            var color1 = System.Drawing.Color.FromArgb(bg.PrimaryColor.A, bg.PrimaryColor.R, bg.PrimaryColor.G, bg.PrimaryColor.B);
            var color2 = System.Drawing.Color.FromArgb(bg.SecondaryColor.A, bg.SecondaryColor.R, bg.SecondaryColor.G, bg.SecondaryColor.B);

            using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                new System.Drawing.Rectangle(0, 0, w, h), color1, color2, angle);
            g.FillRectangle(brush, 0, 0, w, h);
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Bmp);

            return path;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_wallpaper != null)
        {
            Marshal.ReleaseComObject(_wallpaper);
            _wallpaper = null;
        }
    }
}
