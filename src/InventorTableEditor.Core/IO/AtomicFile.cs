using System.Text;

namespace InventorTableEditor.Core.IO;

/// <summary>Запись файла целиком: сначала во временный файл, потом замена — файл не останется обрезанным.</summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string text)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
