using System;
using System.IO;

namespace InventorTableEditor.Addin;

/// <summary>Папки пользователя аддина в его профиле.</summary>
internal static class AddinPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InventorTableEditor");

    /// <summary>Пользовательские шаблоны таблиц (*.json).</summary>
    public static string TemplatesDirectory { get; } = Path.Combine(DataDirectory, "Templates");
}
