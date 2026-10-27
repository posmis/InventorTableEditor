using System.Text.Json;

namespace InventorTableEditor.Core.Model;

/// <summary>
/// Таблица: размеры столбцов и строк в мм и текст ячеек.
/// Если <see cref="HasHeader"/> включён, строка 0 — строка заголовков.
/// </summary>
public sealed class TableModel
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;

    public TableStyle Style { get; set; } = new();

    public bool HasHeader { get; set; }

    public List<double> ColumnWidths { get; set; } = new();

    public List<double> RowHeights { get; set; } = new();

    /// <summary>Ячейки [строка][столбец].</summary>
    public List<List<TableCell>> Cells { get; set; } = new();

    public int RowCount => RowHeights.Count;

    public int ColumnCount => ColumnWidths.Count;

    public static TableModel CreateEmpty(int rows, int columns, TableStyle? style = null)
    {
        if (rows < 1) throw new ArgumentOutOfRangeException(nameof(rows));
        if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns));

        var model = new TableModel { Style = style?.Clone() ?? new TableStyle() };
        for (var c = 0; c < columns; c++)
            model.ColumnWidths.Add(model.Style.DefaultColumnWidthMm);
        for (var r = 0; r < rows; r++)
            model.InsertRow(r);
        return model;
    }

    public TableCell GetCell(int row, int column) => Cells[row][column];

    public string GetText(int row, int column) => Cells[row][column].PlainText;

    /// <summary>Заменяет содержимое ячейки простым текстом с курсивом по умолчанию таблицы.</summary>
    public void SetText(int row, int column, string? text) =>
        Cells[row][column] = TableCell.FromText(text, Style.Italic);

    public void SetCell(int row, int column, TableCell cell)
    {
        cell.Normalize();
        Cells[row][column] = cell;
    }

    public void InsertRow(int index)
    {
        if (index < 0 || index > RowCount) throw new ArgumentOutOfRangeException(nameof(index));
        RowHeights.Insert(index, Style.DefaultRowHeightMm);
        Cells.Insert(index, Enumerable.Range(0, ColumnCount).Select(_ => new TableCell()).ToList());
    }

    public void RemoveRow(int index)
    {
        if (index < 0 || index >= RowCount) throw new ArgumentOutOfRangeException(nameof(index));
        if (RowCount == 1) throw new InvalidOperationException("Нельзя удалить единственную строку.");
        RowHeights.RemoveAt(index);
        Cells.RemoveAt(index);
        if (index == 0) HasHeader = false;
    }

    public void InsertColumn(int index)
    {
        if (index < 0 || index > ColumnCount) throw new ArgumentOutOfRangeException(nameof(index));
        ColumnWidths.Insert(index, Style.DefaultColumnWidthMm);
        foreach (var row in Cells)
            row.Insert(index, new TableCell());
    }

    public void RemoveColumn(int index)
    {
        if (index < 0 || index >= ColumnCount) throw new ArgumentOutOfRangeException(nameof(index));
        if (ColumnCount == 1) throw new InvalidOperationException("Нельзя удалить единственный столбец.");
        ColumnWidths.RemoveAt(index);
        foreach (var row in Cells)
            row.RemoveAt(index);
    }

    /// <summary>Включает строку заголовков (добавляет пустую строку сверху) или выключает (удаляет строку 0).</summary>
    public void SetHeader(bool enabled)
    {
        if (enabled == HasHeader) return;
        if (enabled)
        {
            InsertRow(0);
            HasHeader = true;
        }
        else if (RowCount > 1)
        {
            RemoveRow(0);
        }
        else
        {
            HasHeader = false;
        }
    }

    public bool IsHeaderRow(int row) => HasHeader && row == 0;

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static TableModel FromJson(string json)
    {
        TableModel? model;
        using (var document = JsonDocument.Parse(json))
        {
            model = IsLegacyFormat(document.RootElement)
                ? FromLegacy(JsonSerializer.Deserialize<LegacyTableModel>(json, JsonOptions))
                : JsonSerializer.Deserialize<TableModel>(json, JsonOptions);
        }

        if (model == null)
            throw new InvalidDataException("Пустые данные таблицы.");

        model.Version = CurrentVersion;
        model.Validate();
        return model;
    }

    /// <summary>Версия 1 хранила текст ячеек строками.</summary>
    private static bool IsLegacyFormat(JsonElement root) =>
        root.TryGetProperty(nameof(Cells), out var cells)
        && cells.ValueKind == JsonValueKind.Array
        && cells.EnumerateArray().Any(row => row.ValueKind == JsonValueKind.Array
                                              && row.EnumerateArray().Any(cell => cell.ValueKind == JsonValueKind.String));

    private static TableModel? FromLegacy(LegacyTableModel? legacy)
    {
        if (legacy == null)
            return null;

        return new TableModel
        {
            Style = legacy.Style,
            HasHeader = legacy.HasHeader,
            ColumnWidths = legacy.ColumnWidths,
            RowHeights = legacy.RowHeights,
            Cells = legacy.Cells.Select(row => row.Select(text => TableCell.FromText(text, legacy.Style.Italic)).ToList()).ToList(),
        };
    }

    private sealed class LegacyTableModel
    {
        public TableStyle Style { get; set; } = new();

        public bool HasHeader { get; set; }

        public List<double> ColumnWidths { get; set; } = new();

        public List<double> RowHeights { get; set; } = new();

        public List<List<string>> Cells { get; set; } = new();
    }

    private void Validate()
    {
        if (ColumnCount == 0 || RowCount == 0 || Cells.Count != RowCount || Cells.Any(r => r.Count != ColumnCount))
            throw new InvalidDataException("Повреждённые данные таблицы.");
    }
}
