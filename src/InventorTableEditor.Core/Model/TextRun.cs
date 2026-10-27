using System.Text.Json.Serialization;

namespace InventorTableEditor.Core.Model;

public enum ScriptPosition
{
    Normal,
    Superscript,
    Subscript,
}

/// <summary>
/// Фрагмент текста с единым форматированием. Перенос строки внутри ячейки — '\n'.
/// </summary>
public sealed class TextRun
{
    public TextRun()
    {
    }

    public TextRun(string text, bool italic = false)
    {
        Text = text;
        Italic = italic;
    }

    public string Text { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Bold { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Italic { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Underline { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ScriptPosition Script { get; set; }

    /// <summary>Шрифт фрагмента; null — шрифт таблицы.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FontName { get; set; }

    /// <summary>Высота шрифта основного текста, мм; null — размер таблицы. Для индексов уменьшается автоматически.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? FontSizeMm { get; set; }

    public TextRun Clone() => (TextRun)MemberwiseClone();

    public TextRun WithText(string text)
    {
        var copy = Clone();
        copy.Text = text;
        return copy;
    }

    /// <summary>Совпадает ли форматирование (текст не сравнивается).</summary>
    public bool SameFormat(TextRun other) =>
        Bold == other.Bold && Italic == other.Italic && Underline == other.Underline && Script == other.Script
        && FontName == other.FontName && FontSizeMm == other.FontSizeMm;
}
