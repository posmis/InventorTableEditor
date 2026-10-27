using System.Text.Json.Serialization;

namespace InventorTableEditor.Core.Model;

/// <summary>
/// Ячейка таблицы: текст из фрагментов с форматированием.
/// </summary>
public sealed class TableCell
{
    public List<TextRun> Runs { get; set; } = new();

    /// <summary>null — по умолчанию: заголовки по центру, данные слева.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HorizontalTextAlignment? HorizontalAlignment { get; set; }

    /// <summary>null — по умолчанию: по центру.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VerticalTextAlignment? VerticalAlignment { get; set; }

    public string PlainText => string.Concat(Runs.Select(r => r.Text));

    public bool IsEmpty => string.IsNullOrWhiteSpace(PlainText);

    public static TableCell FromText(string? text, bool italic)
    {
        var cell = new TableCell();
        var normalized = NormalizeLineBreaks(text);
        if (normalized.Length > 0)
            cell.Runs.Add(new TextRun(normalized, italic));
        return cell;
    }

    public TableCell Clone() => new()
    {
        Runs = Runs.Select(r => r.Clone()).ToList(),
        HorizontalAlignment = HorizontalAlignment,
        VerticalAlignment = VerticalAlignment,
    };

    /// <summary>
    /// Нормализует фрагменты: единые переносы строк, без пустых фрагментов,
    /// соседние фрагменты с одинаковым форматированием склеены.
    /// </summary>
    public void Normalize()
    {
        var result = new List<TextRun>();
        foreach (var run in Runs)
        {
            run.Text = NormalizeLineBreaks(run.Text);
            if (run.Text.Length == 0)
                continue;

            if (result.Count > 0 && result[^1].SameFormat(run))
                result[^1].Text += run.Text;
            else
                result.Add(run);
        }

        Runs = result;
    }

    /// <summary>
    /// Разбивает текст ячейки на строки по '\n'. Пустая строка — пустой список фрагментов.
    /// </summary>
    public List<List<TextRun>> SplitLines()
    {
        var lines = new List<List<TextRun>> { new() };
        foreach (var run in Runs)
        {
            var parts = run.Text.Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                    lines.Add(new List<TextRun>());
                if (parts[i].Length > 0)
                    lines[^1].Add(run.WithText(parts[i]));
            }
        }

        return lines;
    }

    private static string NormalizeLineBreaks(string? text) =>
        (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
}
