using System.Text;
using InventorTableEditor.Core.IO;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Templates;

/// <summary>Шаблон таблицы: встроенный (в комплекте, не удаляется) или пользовательский (файл JSON).</summary>
public sealed record TableTemplate(string Name, bool BuiltIn, string? FilePath = null);

/// <summary>
/// Шаблоны таблиц: встроенные и пользовательские из папки (<c>%APPDATA%\InventorTableEditor\Templates</c>).
/// Шаблон хранит всю таблицу: структуру, размеры, заголовки, объединения, текст и оформление.
/// </summary>
public sealed class TemplateLibrary
{
    public const int MaxNameLength = 80;

    private const string Extension = ".json";

    private static readonly char[] ForbiddenChars = "<>:\"/\\|?*".ToCharArray();

    private static readonly string[] ReservedNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public TemplateLibrary(string directory)
    {
        Directory = directory;
    }

    public string Directory { get; }

    /// <summary>Встроенные шаблоны, затем пользовательские по алфавиту.</summary>
    public List<TableTemplate> List()
    {
        var templates = BuiltInTemplates.Names.Select(name => new TableTemplate(name, BuiltIn: true)).ToList();
        if (System.IO.Directory.Exists(Directory))
        {
            templates.AddRange(System.IO.Directory.EnumerateFiles(Directory, "*" + Extension)
                .Select(path => new TableTemplate(Path.GetFileNameWithoutExtension(path), BuiltIn: false, path))
                .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase));
        }

        return templates;
    }

    /// <summary>Таблица из шаблона — новая копия. Повреждённый файл — <see cref="InvalidDataException"/>.</summary>
    public TableModel Load(TableTemplate template)
    {
        if (template.BuiltIn)
            return BuiltInTemplates.Create(template.Name);

        try
        {
            var model = TableModel.FromJson(File.ReadAllText(template.FilePath ?? PathOf(template.Name), Encoding.UTF8));
            model.Editor = null;
            return model;
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidDataException($"Файл шаблона «{template.Name}» повреждён: {ex.Message}", ex);
        }
    }

    /// <summary>Есть ли шаблон с таким именем (без учёта регистра, как имена файлов Windows).</summary>
    public bool Exists(string name) =>
        List().Any(t => string.Equals(t.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Сохраняет таблицу как шаблон (без размера окна редактора). Существующий пользовательский шаблон
    /// с тем же именем заменяется, только если <paramref name="overwrite"/>.
    /// </summary>
    public TableTemplate Save(string name, TableModel model, bool overwrite = false)
    {
        name = name.Trim();
        if (CheckName(name) is { } error)
            throw new ArgumentException(error, nameof(name));
        if (IsBuiltIn(name))
            throw new InvalidOperationException($"«{name}» — встроенный шаблон, выберите другое имя.");

        var path = PathOf(name);
        var existing = UserTemplate(name);
        if (!overwrite && existing != null)
            throw new InvalidOperationException($"Шаблон «{name}» уже есть.");

        var copy = TableModel.FromJson(model.ToJson());
        copy.Editor = null;
        AtomicFile.WriteAllText(path, copy.ToJson());
        // Тот же шаблон с именем в другом регистре (на диске с учётом регистра) — старый файл не нужен.
        if (existing?.FilePath is { } oldPath && oldPath != path && File.Exists(oldPath))
            File.Delete(oldPath);
        return new TableTemplate(name, BuiltIn: false, path);
    }

    public void Delete(TableTemplate template)
    {
        if (template.BuiltIn)
            throw new InvalidOperationException("Встроенный шаблон удалить нельзя.");

        File.Delete(template.FilePath ?? PathOf(template.Name));
    }

    public TableTemplate Rename(TableTemplate template, string newName)
    {
        if (template.BuiltIn)
            throw new InvalidOperationException("Встроенный шаблон переименовать нельзя.");

        newName = newName.Trim();
        if (CheckName(newName) is { } error)
            throw new ArgumentException(error, nameof(newName));
        if (IsBuiltIn(newName))
            throw new InvalidOperationException($"«{newName}» — встроенный шаблон, выберите другое имя.");

        var from = template.FilePath ?? PathOf(template.Name);
        var to = PathOf(newName);
        // Смена только регистра букв — тот же файл в Windows; иначе имя не должно быть занято.
        var sameFile = string.Equals(template.Name, newName, StringComparison.OrdinalIgnoreCase);
        if (!sameFile && UserTemplate(newName) != null)
            throw new InvalidOperationException($"Шаблон «{newName}» уже есть.");

        if (sameFile)
        {
            var temporary = to + ".rename";
            File.Move(from, temporary);
            File.Move(temporary, to);
        }
        else
        {
            File.Move(from, to);
        }

        return new TableTemplate(newName, BuiltIn: false, to);
    }

    /// <summary>Ошибка в имени шаблона (по-русски) или null — имя подходит для имени файла Windows.</summary>
    public static string? CheckName(string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name))
            return "Введите имя шаблона.";
        if (name.Length > MaxNameLength)
            return $"Имя шаблона длиннее {MaxNameLength} символов.";
        if (name.IndexOfAny(ForbiddenChars) >= 0 || name.Any(char.IsControl))
            return "Имя шаблона не может содержать символы < > : \" / \\ | ? *";
        if (name.EndsWith('.'))
            return "Имя шаблона не может заканчиваться точкой.";
        if (ReservedNames.Contains(name.Split('.')[0], StringComparer.OrdinalIgnoreCase))
            return $"Имя «{name}» зарезервировано Windows.";
        return null;
    }

    private TableTemplate? UserTemplate(string name) =>
        List().FirstOrDefault(t => !t.BuiltIn && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    private static bool IsBuiltIn(string name) =>
        BuiltInTemplates.Names.Contains(name, StringComparer.OrdinalIgnoreCase);

    private string PathOf(string name) => Path.Combine(Directory, name + Extension);
}
