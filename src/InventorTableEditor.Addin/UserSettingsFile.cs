using System;
using System.IO;
using InventorTableEditor.Core.Settings;

namespace InventorTableEditor.Addin;

/// <summary>Настройки пользователя для новых таблиц: %APPDATA%\InventorTableEditor\settings.json.</summary>
internal static class UserSettingsFile
{
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InventorTableEditor", "settings.json");

    /// <summary>Сохранённые настройки; нет файла или он повреждён — заводские (ошибка пишется в журнал).</summary>
    public static UserSettings Load()
    {
        var settings = UserSettings.Load(FilePath, out var error);
        if (error != null)
            AddinLog.Write($"Файл настроек {FilePath} не прочитан, взяты заводские: {error}");
        return settings;
    }

    /// <summary>Сохраняет настройки; возвращает текст ошибки или null.</summary>
    public static string? Save(UserSettings settings)
    {
        try
        {
            settings.Save(FilePath);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddinLog.Write($"Файл настроек {FilePath} не сохранён: {ex.Message}");
            return ex.Message;
        }
    }
}
