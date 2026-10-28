using System.Text;
using System.Text.Json;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Settings;

/// <summary>
/// Настройки пользователя для новых таблиц: оформление и размер новой таблицы. Хранятся в его профиле
/// (<c>%APPDATA%\InventorTableEditor\settings.json</c>); на уже вставленные таблицы не влияют.
/// </summary>
public sealed class UserSettings
{
    public const int MaxNewTableSize = 100;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public int Version { get; set; } = 1;

    public TableStyle Style { get; set; } = new();

    public int NewTableRows { get; set; } = 3;

    public int NewTableColumns { get; set; } = 3;

    public bool NewTableHeader { get; set; }

    public UserSettings Clone() => new()
    {
        Version = Version,
        Style = Style.Clone(),
        NewTableRows = NewTableRows,
        NewTableColumns = NewTableColumns,
        NewTableHeader = NewTableHeader,
    };

    /// <summary>Новая пустая таблица по этим настройкам.</summary>
    public TableModel CreateTable()
    {
        var model = TableModel.CreateEmpty(NewTableRows, NewTableColumns, Style);
        if (NewTableHeader)
            model.SetHeader(true);
        return model;
    }

    /// <summary>Ошибки настроек (по-русски); пустой список — всё в порядке.</summary>
    public List<string> Check()
    {
        var errors = Style.Check();
        if (NewTableRows < 1 || NewTableRows > MaxNewTableSize)
            errors.Add($"Строк в новой таблице: от 1 до {MaxNewTableSize}.");
        if (NewTableColumns < 1 || NewTableColumns > MaxNewTableSize)
            errors.Add($"Столбцов в новой таблице: от 1 до {MaxNewTableSize}.");
        return errors;
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Настройки из JSON; неверные значения заменяются заводскими.</summary>
    public static UserSettings FromJson(string json)
    {
        var settings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
        settings.Style ??= new TableStyle();
        settings.Style.Repair();
        var factory = new UserSettings();
        if (settings.NewTableRows < 1 || settings.NewTableRows > MaxNewTableSize)
            settings.NewTableRows = factory.NewTableRows;
        if (settings.NewTableColumns < 1 || settings.NewTableColumns > MaxNewTableSize)
            settings.NewTableColumns = factory.NewTableColumns;
        return settings;
    }

    /// <summary>
    /// Читает настройки из файла. Нет файла — заводские; файл повреждён — заводские и текст ошибки
    /// в <paramref name="error"/> (файл не трогается, чтобы его можно было поправить).
    /// </summary>
    public static UserSettings Load(string path, out string? error)
    {
        error = null;
        if (!File.Exists(path))
            return new UserSettings();

        try
        {
            return FromJson(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = ex.Message;
            return new UserSettings();
        }
    }

    /// <summary>Сохраняет настройки: сначала во временный файл, потом заменяет — файл не останется обрезанным.</summary>
    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, ToJson(), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
}
