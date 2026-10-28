using System;
using System.IO;

namespace InventorTableEditor.Addin;

/// <summary>
/// Простой журнал для диагностики: %APPDATA%\InventorTableEditor\log.txt.
/// Когда журнал вырастает больше 1 МБ, он переименовывается в log.old.txt и начинается заново.
/// </summary>
internal static class AddinLog
{
    private const long MaxSizeBytes = 1024 * 1024;

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InventorTableEditor", "log.txt");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var file = new FileInfo(FilePath);
            if (file.Exists && file.Length > MaxSizeBytes)
                File.Move(FilePath, Path.ChangeExtension(FilePath, ".old.txt"), overwrite: true);
            File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Журнал не должен мешать работе аддина.
        }
    }
}
