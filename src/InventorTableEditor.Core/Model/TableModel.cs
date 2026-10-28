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

    [System.Text.Json.Serialization.JsonIgnore]
    public int RowCount => RowHeights.Count;

    [System.Text.Json.Serialization.JsonIgnore]
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

    /// <summary>Заменяет содержимое ячейки простым текстом с курсивом по умолчанию таблицы; объединение сохраняется.</summary>
    public void SetText(int row, int column, string? text) => SetCell(row, column, TableCell.FromText(text, Style.Italic));

    /// <summary>Заменяет ячейку. Объединение — свойство таблицы, а не текста: оно сохраняется.</summary>
    public void SetCell(int row, int column, TableCell cell)
    {
        var previous = Cells[row][column];
        cell.ExtraRows = previous.ExtraRows;
        cell.ExtraColumns = previous.ExtraColumns;
        cell.Normalize();
        Cells[row][column] = cell;
    }

    /// <summary>
    /// Вставляет пустую строку. Над строкой заголовков вставить нельзя — строка встанет под ней.
    /// Строка внутри объединённой ячейки расширяет объединение.
    /// </summary>
    public void InsertRow(int index)
    {
        if (index < 0 || index > RowCount) throw new ArgumentOutOfRangeException(nameof(index));
        if (HasHeader && index == 0)
            index = 1;

        var merges = Merges();
        RowHeights.Insert(index, Style.DefaultRowHeightMm);
        Cells.Insert(index, Enumerable.Range(0, ColumnCount).Select(_ => new TableCell()).ToList());
        ApplyMerges(merges.Select(m => m with
        {
            Top = m.Top >= index ? m.Top + 1 : m.Top,
            Bottom = m.Bottom >= index ? m.Bottom + 1 : m.Bottom,
        }));
    }

    /// <summary>Удаляет строку. Объединение, в которое она входит, сужается; текст объединения сохраняется.</summary>
    public void RemoveRow(int index)
    {
        if (index < 0 || index >= RowCount) throw new ArgumentOutOfRangeException(nameof(index));
        if (RowCount == 1) throw new InvalidOperationException("Нельзя удалить единственную строку.");

        var merges = Merges();
        foreach (var m in merges.Where(m => m.Top == index && m.Bottom > index))
            Cells[index + 1][m.Left] = Cells[index][m.Left];

        RowHeights.RemoveAt(index);
        Cells.RemoveAt(index);
        if (index == 0) HasHeader = false;
        ApplyMerges(merges.Select(m => m with
        {
            Top = m.Top > index ? m.Top - 1 : m.Top,
            Bottom = m.Bottom >= index ? m.Bottom - 1 : m.Bottom,
        }));
    }

    /// <summary>Вставляет пустой столбец. Столбец внутри объединённой ячейки расширяет объединение.</summary>
    public void InsertColumn(int index)
    {
        if (index < 0 || index > ColumnCount) throw new ArgumentOutOfRangeException(nameof(index));

        var merges = Merges();
        ColumnWidths.Insert(index, Style.DefaultColumnWidthMm);
        foreach (var row in Cells)
            row.Insert(index, new TableCell());
        ApplyMerges(merges.Select(m => m with
        {
            Left = m.Left >= index ? m.Left + 1 : m.Left,
            Right = m.Right >= index ? m.Right + 1 : m.Right,
        }));
    }

    /// <summary>Удаляет столбец. Объединение, в которое он входит, сужается; текст объединения сохраняется.</summary>
    public void RemoveColumn(int index)
    {
        if (index < 0 || index >= ColumnCount) throw new ArgumentOutOfRangeException(nameof(index));
        if (ColumnCount == 1) throw new InvalidOperationException("Нельзя удалить единственный столбец.");

        var merges = Merges();
        foreach (var m in merges.Where(m => m.Left == index && m.Right > index))
            Cells[m.Top][index + 1] = Cells[m.Top][index];

        ColumnWidths.RemoveAt(index);
        foreach (var row in Cells)
            row.RemoveAt(index);
        ApplyMerges(merges.Select(m => m with
        {
            Left = m.Left > index ? m.Left - 1 : m.Left,
            Right = m.Right >= index ? m.Right - 1 : m.Right,
        }));
    }

    // ---------- Объединение ячеек ----------

    /// <summary>Все объединённые ячейки (прямоугольники больше одной ячейки).</summary>
    public List<CellRange> Merges()
    {
        var merges = new List<CellRange>();
        for (var r = 0; r < RowCount; r++)
        {
            for (var c = 0; c < ColumnCount; c++)
            {
                var cell = Cells[r][c];
                if (cell.IsMergeAnchor)
                    merges.Add(new CellRange(r, c, r + cell.ExtraRows, c + cell.ExtraColumns));
            }
        }

        return merges;
    }

    /// <summary>Прямоугольник объединения, в которое входит ячейка; для обычной ячейки — она сама.</summary>
    public CellRange GetMerge(int row, int column)
    {
        foreach (var merge in Merges())
        {
            if (merge.Contains(row, column))
                return merge;
        }

        return new CellRange(row, column, row, column);
    }

    /// <summary>Левая верхняя ячейка объединения — в ней хранится текст; для обычной ячейки — она сама.</summary>
    public (int Row, int Column) AnchorOf(int row, int column)
    {
        var merge = GetMerge(row, column);
        return (merge.Top, merge.Left);
    }

    /// <summary>Ячейка закрыта объединением (не левая верхняя) и не отображается отдельно.</summary>
    public bool IsCovered(int row, int column) => AnchorOf(row, column) != (row, column);

    /// <summary>Прямоугольник, расширенный так, чтобы объединённые ячейки входили в него целиком (как в Excel).</summary>
    public CellRange ExpandToMerges(CellRange range)
    {
        var merges = Merges();
        bool grown;
        do
        {
            grown = false;
            foreach (var m in merges)
            {
                var intersects = m.Top <= range.Bottom && m.Bottom >= range.Top && m.Left <= range.Right && m.Right >= range.Left;
                var inside = m.Top >= range.Top && m.Bottom <= range.Bottom && m.Left >= range.Left && m.Right <= range.Right;
                if (!intersects || inside)
                    continue;

                range = new CellRange(Math.Min(range.Top, m.Top), Math.Min(range.Left, m.Left),
                    Math.Max(range.Bottom, m.Bottom), Math.Max(range.Right, m.Right));
                grown = true;
            }
        }
        while (grown);

        return range;
    }

    /// <summary>
    /// Объединяет ячейки прямоугольника (расширенного на задетые объединения). Текст всех ячеек
    /// собирается в левой верхней, каждый с новой строки, как в Word; выравнивание — левой верхней.
    /// </summary>
    public void Merge(CellRange range)
    {
        range = ExpandToMerges(range);
        if (range.IsSingleCell)
            return;

        var anchor = Cells[range.Top][range.Left];
        var merged = new TableCell
        {
            HorizontalAlignment = anchor.HorizontalAlignment,
            VerticalAlignment = anchor.VerticalAlignment,
            ExtraRows = range.Bottom - range.Top,
            ExtraColumns = range.Right - range.Left,
        };

        foreach (var (row, column) in range.Cells())
        {
            var cell = Cells[row][column];
            if (!cell.IsEmpty)
            {
                if (merged.Runs.Count > 0)
                    merged.Runs.Add(merged.Runs[^1].WithText("\n"));
                merged.Runs.AddRange(cell.Runs.Select(r => r.Clone()));
            }

            Cells[row][column] = new TableCell();
        }

        merged.Normalize();
        Cells[range.Top][range.Left] = merged;
    }

    /// <summary>Разделяет объединённую ячейку, в которую входит ячейка; текст остаётся в левой верхней.</summary>
    public void Split(int row, int column)
    {
        var merge = GetMerge(row, column);
        var anchor = Cells[merge.Top][merge.Left];
        anchor.ExtraRows = 0;
        anchor.ExtraColumns = 0;
    }

    /// <summary>Расставляет объединения заново (после вставки или удаления строк и столбцов).</summary>
    private void ApplyMerges(IEnumerable<CellRange> merges)
    {
        foreach (var row in Cells)
        {
            foreach (var cell in row)
            {
                cell.ExtraRows = 0;
                cell.ExtraColumns = 0;
            }
        }

        foreach (var m in merges)
        {
            if (m.Bottom < m.Top || m.Right < m.Left || m.IsSingleCell)
                continue;

            Cells[m.Top][m.Left].ExtraRows = m.Bottom - m.Top;
            Cells[m.Top][m.Left].ExtraColumns = m.Right - m.Left;
        }
    }

    /// <summary>
    /// Убирает объединения, которые выходят за таблицу или пересекаются (повреждённые данные):
    /// такие ячейки становятся обычными.
    /// </summary>
    private void RepairMerges()
    {
        var taken = new bool[RowCount, ColumnCount];
        for (var r = 0; r < RowCount; r++)
        {
            for (var c = 0; c < ColumnCount; c++)
            {
                var cell = Cells[r][c];
                if (cell.ExtraRows < 0 || cell.ExtraColumns < 0)
                {
                    cell.ExtraRows = 0;
                    cell.ExtraColumns = 0;
                }

                if (!cell.IsMergeAnchor)
                    continue;

                var range = new CellRange(r, c, r + cell.ExtraRows, c + cell.ExtraColumns);
                // Первое по порядку объединение остаётся, пересекающиеся с ним — сбрасываются.
                var valid = range.Bottom < RowCount && range.Right < ColumnCount
                            && range.Cells().All(p => !taken[p.Row, p.Column]);
                if (!valid)
                {
                    cell.ExtraRows = 0;
                    cell.ExtraColumns = 0;
                    continue;
                }

                foreach (var (row, column) in range.Cells())
                    taken[row, column] = true;
            }
        }
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

    /// <summary>Выравнивание ячейки по горизонтали с учётом умолчаний: заголовки по центру, данные слева.</summary>
    public HorizontalTextAlignment GetHorizontalAlignment(int row, int column) =>
        Cells[row][column].HorizontalAlignment ?? (IsHeaderRow(row) ? HorizontalTextAlignment.Center : HorizontalTextAlignment.Left);

    public VerticalTextAlignment GetVerticalAlignment(int row, int column) =>
        Cells[row][column].VerticalAlignment ?? VerticalTextAlignment.Middle;

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

    /// <summary>Проверяет данные, прочитанные из чертежа, и приводит к норме то, что можно исправить.</summary>
    private void Validate()
    {
        Style ??= new TableStyle();
        ColumnWidths ??= new List<double>();
        RowHeights ??= new List<double>();
        Cells ??= new List<List<TableCell>>();

        if (ColumnCount == 0 || RowCount == 0 || Cells.Count != RowCount || Cells.Any(r => r == null || r.Count != ColumnCount))
            throw new InvalidDataException("Повреждённые данные таблицы: число строк и столбцов не совпадает с ячейками.");
        if (ColumnWidths.Concat(RowHeights).Any(size => !double.IsFinite(size) || size <= 0))
            throw new InvalidDataException("Повреждённые данные таблицы: размеры строк и столбцов должны быть положительными.");
        if (!(Style.FontSizeMm > 0) || !(Style.ScriptScale > 0) || !(Style.LineSpacing > 0) || Style.PaddingMm < 0)
            throw new InvalidDataException("Повреждённые данные таблицы: недопустимые параметры оформления.");
        if (!(Style.MinScaleBeforeWrap > 0 && Style.MinScaleBeforeWrap <= 1))
            Style.MinScaleBeforeWrap = new TableStyle().MinScaleBeforeWrap;

        foreach (var row in Cells)
        {
            for (var c = 0; c < row.Count; c++)
            {
                var cell = row[c] ?? new TableCell();
                cell.Runs = (cell.Runs ?? new List<TextRun>()).Where(r => r != null).ToList();
                foreach (var run in cell.Runs)
                    run.Text ??= string.Empty;
                cell.Normalize();
                row[c] = cell;
            }
        }

        RepairMerges();
    }
}
