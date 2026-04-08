using Microsoft.EntityFrameworkCore;
using VDesk.Models;
using System.Windows.Media;

namespace VDesk.Data;

public class ConfigRepository : IDisposable
{
    private readonly VDeskDbContext _db;

    public ConfigRepository()
    {
        _db = new VDeskDbContext();
        _db.Database.EnsureCreated();
    }

    public List<DesktopSlot> LoadDesktopsForMonitor(string monitorDeviceId)
    {
        var entities = _db.DesktopConfigs
            .Where(e => e.MonitorDeviceId == monitorDeviceId)
            .OrderBy(e => e.SortOrder)
            .ToList();

        return entities.Select(MapToSlot).ToList();
    }

    public void SaveDesktopsForMonitor(string monitorDeviceId, List<DesktopSlot> desktops)
    {
        var existing = _db.DesktopConfigs
            .Where(e => e.MonitorDeviceId == monitorDeviceId)
            .ToList();
        _db.DesktopConfigs.RemoveRange(existing);

        for (int i = 0; i < desktops.Count; i++)
        {
            _db.DesktopConfigs.Add(MapToEntity(monitorDeviceId, desktops[i], i));
        }
        _db.SaveChanges();
    }

    public void SaveDesktopSlot(string monitorDeviceId, DesktopSlot slot, int sortOrder)
    {
        var entity = _db.DesktopConfigs
            .FirstOrDefault(e => e.MonitorDeviceId == monitorDeviceId
                && e.SystemDesktopId == slot.SystemDesktopId);

        if (entity == null)
        {
            entity = MapToEntity(monitorDeviceId, slot, sortOrder);
            _db.DesktopConfigs.Add(entity);
        }
        else
        {
            UpdateEntity(entity, slot, sortOrder);
        }
        _db.SaveChanges();
    }

    public string? GetSetting(string key)
    {
        return _db.AppSettings.FirstOrDefault(s => s.Key == key)?.Value;
    }

    public void SetSetting(string key, string value)
    {
        var entity = _db.AppSettings.FirstOrDefault(s => s.Key == key);
        if (entity == null)
        {
            _db.AppSettings.Add(new AppSettingsEntity { Key = key, Value = value });
        }
        else
        {
            entity.Value = value;
        }
        _db.SaveChanges();
    }

    public List<HotkeyConfigEntity> LoadHotkeys()
    {
        return _db.HotkeyConfigs.ToList();
    }

    public void SaveHotkey(HotkeyConfigEntity hotkey)
    {
        var existing = _db.HotkeyConfigs.FirstOrDefault(h => h.ActionId == hotkey.ActionId);
        if (existing == null)
        {
            _db.HotkeyConfigs.Add(hotkey);
        }
        else
        {
            existing.Modifiers = hotkey.Modifiers;
            existing.Key = hotkey.Key;
            existing.IsEnabled = hotkey.IsEnabled;
            existing.DisplayName = hotkey.DisplayName;
        }
        _db.SaveChanges();
    }

    private static DesktopSlot MapToSlot(DesktopConfigEntity e)
    {
        return new DesktopSlot
        {
            SystemDesktopId = e.SystemDesktopId,
            Name = e.Name,
            Background = new DesktopBackground
            {
                Type = (BackgroundType)e.BackgroundType,
                PrimaryColor = (Color)ColorConverter.ConvertFromString(e.PrimaryColor),
                SecondaryColor = (Color)ColorConverter.ConvertFromString(e.SecondaryColor),
                GradientAngle = e.GradientAngle,
                ImagePath = e.ImagePath,
                ImageFillMode = (FillMode)e.ImageFillMode,
            },
            Watermark = new WatermarkSettings
            {
                IsEnabled = e.WatermarkEnabled,
                Position = (CornerPosition)e.WatermarkPosition,
                CustomText = e.WatermarkCustomText,
                FontSize = e.WatermarkFontSize,
                FontFamily = e.WatermarkFontFamily,
                TextColor = (Color)ColorConverter.ConvertFromString(e.WatermarkTextColor),
                Opacity = e.WatermarkOpacity,
                Margin = e.WatermarkMargin,
            },
            Border = new BorderSettings
            {
                IsEnabled = e.BorderEnabled,
                BorderColor = (Color)ColorConverter.ConvertFromString(e.BorderColor),
                Width = e.BorderWidth,
                ShowTop = e.BorderShowTop,
                ShowBottom = e.BorderShowBottom,
                ShowLeft = e.BorderShowLeft,
                ShowRight = e.BorderShowRight,
            }
        };
    }

    private static DesktopConfigEntity MapToEntity(string monitorId, DesktopSlot slot, int order)
    {
        return new DesktopConfigEntity
        {
            MonitorDeviceId = monitorId,
            SystemDesktopId = slot.SystemDesktopId,
            SortOrder = order,
            Name = slot.Name,
            BackgroundType = (int)slot.Background.Type,
            PrimaryColor = slot.Background.PrimaryColor.ToString(),
            SecondaryColor = slot.Background.SecondaryColor.ToString(),
            GradientAngle = slot.Background.GradientAngle,
            ImagePath = slot.Background.ImagePath,
            ImageFillMode = (int)slot.Background.ImageFillMode,
            WatermarkEnabled = slot.Watermark.IsEnabled,
            WatermarkPosition = (int)slot.Watermark.Position,
            WatermarkCustomText = slot.Watermark.CustomText,
            WatermarkFontSize = slot.Watermark.FontSize,
            WatermarkFontFamily = slot.Watermark.FontFamily,
            WatermarkTextColor = slot.Watermark.TextColor.ToString(),
            WatermarkOpacity = slot.Watermark.Opacity,
            WatermarkMargin = slot.Watermark.Margin,
            BorderEnabled = slot.Border.IsEnabled,
            BorderColor = slot.Border.BorderColor.ToString(),
            BorderWidth = slot.Border.Width,
            BorderShowTop = slot.Border.ShowTop,
            BorderShowBottom = slot.Border.ShowBottom,
            BorderShowLeft = slot.Border.ShowLeft,
            BorderShowRight = slot.Border.ShowRight,
        };
    }

    private static void UpdateEntity(DesktopConfigEntity entity, DesktopSlot slot, int order)
    {
        entity.SortOrder = order;
        entity.Name = slot.Name;
        entity.BackgroundType = (int)slot.Background.Type;
        entity.PrimaryColor = slot.Background.PrimaryColor.ToString();
        entity.SecondaryColor = slot.Background.SecondaryColor.ToString();
        entity.GradientAngle = slot.Background.GradientAngle;
        entity.ImagePath = slot.Background.ImagePath;
        entity.ImageFillMode = (int)slot.Background.ImageFillMode;
        entity.WatermarkEnabled = slot.Watermark.IsEnabled;
        entity.WatermarkPosition = (int)slot.Watermark.Position;
        entity.WatermarkCustomText = slot.Watermark.CustomText;
        entity.WatermarkFontSize = slot.Watermark.FontSize;
        entity.WatermarkFontFamily = slot.Watermark.FontFamily;
        entity.WatermarkTextColor = slot.Watermark.TextColor.ToString();
        entity.WatermarkOpacity = slot.Watermark.Opacity;
        entity.WatermarkMargin = slot.Watermark.Margin;
        entity.BorderEnabled = slot.Border.IsEnabled;
        entity.BorderColor = slot.Border.BorderColor.ToString();
        entity.BorderWidth = slot.Border.Width;
        entity.BorderShowTop = slot.Border.ShowTop;
        entity.BorderShowBottom = slot.Border.ShowBottom;
        entity.BorderShowLeft = slot.Border.ShowLeft;
        entity.BorderShowRight = slot.Border.ShowRight;
    }

    public void Dispose()
    {
        _db.Dispose();
    }
}
