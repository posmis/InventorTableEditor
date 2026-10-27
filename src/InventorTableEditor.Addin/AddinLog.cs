using System;
using System.IO;

namespace InventorTableEditor.Addin;

/// <summary>
/// Простой журнал для диагностики: %APPDATA%\InventorTableEditor\log.txt.
/// </summary>
internal static class AddinLog
{
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InventorTableEditor", "log.txt");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Журнал не должен мешать работе аддина.
        }
    }
}
