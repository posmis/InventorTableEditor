using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Editing;

/// <summary>
/// Сеанс редактирования таблицы без интерфейса: таблица, текущая ячейка, выделение ячеек, история отмены
/// и все действия окна редактора (структура, размеры, выравнивание, правка текста). Окно только
/// отображает состояние и передаёт сюда действия пользователя.
/// </summary>
public sealed class TableSession
{
    private readonly TableHistory _history = new();
    private readonly Func<int> _caret;
    private (int Row, int Column) _anchor;
    private bool _batch;

    /// <param name="model">Таблица; сеанс работает с ней напрямую (окно передаёт копию).</param>
    /// <param name="caret">Позиция курсора в текущей ячейке — для истории отмены.</param>
    public TableSession(TableModel model, Func<int>? caret = null)
    {
        Model = model;
        _caret = caret ?? (() => 0);
    }

    public TableModel Model { get; private set; }

    public int Row { get; private set; }

    public int Column { get; private set; }

    /// <summary>Выделенный прямоугольник ячеек; null — выделена одна текущая ячейка.</summary>
    public CellRange? Range { get; private set; }

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    public TableCell CurrentCell => Model.GetCell(Row, Column);

    /// <summary>Ячейки, к которым относятся выравнивание и формат: выделенные или текущая.</summary>
    public IEnumerable<(int Row, int Column)> TargetCells => Range?.Cells() ?? new[] { (Row, Column) };

    // ---------- Текущая ячейка и переходы ----------

    /// <summary>Делает ячейку текущей. Переход в другую ячейку начинает новый шаг отмены.</summary>
    public void MoveTo(int row, int column)
    {
        row = Math.Clamp(row, 0, Model.RowCount - 1);
        column = Math.Clamp(column, 0, Model.ColumnCount - 1);
        if (row != Row || column != Column)
            _history.BreakMerge();
        Row = row;
        Column = column;
    }

    /// <summary>
    /// Соседняя ячейка: по столбцам (Tab, Shift+Tab) с переходом на следующую или предыдущую строку,
    /// по строкам (стрелки вверх и вниз). null — дальше ячеек нет.
    /// </summary>
    public (int Row, int Column)? Neighbor(int rowStep, int columnStep)
    {
        var row = Row + rowStep;
        var column = Column + columnStep;
        if (columnStep != 0)
        {
            if (column >= Model.ColumnCount)
            {
                column = 0;
                row++;
            }
            else if (column < 0)
            {
                column = Model.ColumnCount - 1;
                row--;
            }
        }

        return row < 0 || row >= Model.RowCount || column < 0 || column >= Model.ColumnCount ? null : (row, column);
    }

    // ---------- Выделение ячеек ----------

    /// <summary>Начало выделения (нажатие мыши в ячейке): выделение сбрасывается.</summary>
    public void BeginSelection(int row, int column)
    {
        ClearSelection();
        _anchor = (row, column);
    }

    /// <summary>
    /// Выделение до ячейки (протягивание мышью или Shift+щелчок от текущей ячейки). Если ячейка
    /// та же, что в начале, выделение снова одна ячейка. Возвращает, изменилось ли выделение.
    /// </summary>
    public bool ExtendSelection(int row, int column, bool fromCurrent = false)
    {
        if (fromCurrent)
            _anchor = (Row, Column);

        CellRange? range = (row, column) == _anchor ? null : CellRange.Between(_anchor, (row, column));
        if (range == Range)
            return false;

        Range = range;
        return true;
    }

    public bool ClearSelection()
    {
        if (Range == null)
            return false;

        Range = null;
        return true;
    }

    /// <summary>
    /// Включить или выключить формат у нескольких ячеек, как в Excel: если формат есть у всего текста,
    /// он снимается, иначе включается. Пустые ячейки (null) не учитываются; если пусты все — включается.
    /// </summary>
    public static bool ShouldTurnOn(IEnumerable<bool?> cellsAllOn)
    {
        var states = cellsAllOn.Where(v => v != null).ToList();
        return states.Count == 0 || states.Any(v => v == false);
    }

    // ---------- История отмены ----------

    /// <summary>Запоминает таблицу до действия (если не идёт действие над несколькими ячейками).</summary>
    public void Record(string? mergeKey = null)
    {
        if (!_batch)
            _history.Record(Model, Cursor(), mergeKey);
    }

    /// <summary>Действие над несколькими ячейками — один шаг истории.</summary>
    public void Batch(Action action)
    {
        Record();
        var outer = _batch;
        _batch = true;
        try
        {
            action();
        }
        finally
        {
            _batch = outer;
        }
    }

    /// <summary>Курсор переставлен — следующий набор начнёт новый шаг отмены.</summary>
    public void BreakMerge() => _history.BreakMerge();

    /// <summary>Отменяет шаг; возвращает позицию курсора в восстановленной текущей ячейке или null.</summary>
    public int? Undo() => Restore(_history.Undo(Model, Cursor()));

    public int? Redo() => Restore(_history.Redo(Model, Cursor()));

    private TableCursor Cursor() => new(Row, Column, _caret());

    private int? Restore((TableModel Model, TableCursor Cursor)? step)
    {
        if (step is not { } value)
            return null;

        Model = value.Model;
        Range = null;
        Row = Math.Clamp(value.Cursor.Row, 0, Model.RowCount - 1);
        Column = Math.Clamp(value.Cursor.Column, 0, Model.ColumnCount - 1);
        return value.Cursor.Caret;
    }

    // ---------- Текст ----------

    /// <summary>
    /// Новый текст ячейки после правки. Записывает шаг отмены (набор и удаление подряд в одной ячейке — один шаг).
    /// Возвращает false, если текст и формат не изменились.
    /// </summary>
    public bool ReplaceCell(int row, int column, TableCell cell, EditKind kind)
    {
        var before = Model.GetCell(row, column);
        cell.HorizontalAlignment = before.HorizontalAlignment;
        cell.VerticalAlignment = before.VerticalAlignment;
        cell.Normalize();
        if (before.SameContent(cell))
            return false;

        Record(kind switch
        {
            EditKind.Typing => $"type:{row}:{column}",
            EditKind.Deleting => $"delete:{row}:{column}",
            _ => null,
        });
        Model.SetCell(row, column, cell);
        return true;
    }

    // ---------- Выравнивание ----------

    public void SetHorizontalAlignment(HorizontalTextAlignment alignment)
    {
        Record();
        foreach (var (row, column) in TargetCells)
            Model.GetCell(row, column).HorizontalAlignment = alignment;
    }

    public void SetVerticalAlignment(VerticalTextAlignment alignment)
    {
        Record();
        foreach (var (row, column) in TargetCells)
            Model.GetCell(row, column).VerticalAlignment = alignment;
    }

    // ---------- Структура ----------

    public void InsertColumnLeft() => Change(() => Model.InsertColumn(Column));

    /// <summary>Вставляет столбец справа; текущей становится новая ячейка.</summary>
    public void InsertColumnRight() => Change(() =>
    {
        Model.InsertColumn(Column + 1);
        Column++;
    });

    public void DeleteColumn()
    {
        if (Model.ColumnCount == 1)
            throw new InvalidOperationException("Нельзя удалить единственный столбец.");
        Change(() => Model.RemoveColumn(Column));
    }

    /// <summary>Вставляет строку сверху (над строкой заголовков — под ней).</summary>
    public void InsertRowAbove() => Change(() =>
    {
        var index = Model.IsHeaderRow(Row) ? 1 : Row;
        Model.InsertRow(index);
        Row = index;
    });

    /// <summary>Вставляет строку снизу; текущей становится новая строка.</summary>
    public void InsertRowBelow() => Change(() =>
    {
        Model.InsertRow(Row + 1);
        Row++;
    });

    public void DeleteRow()
    {
        if (Model.RowCount == 1)
            throw new InvalidOperationException("Нельзя удалить единственную строку.");
        Change(() => Model.RemoveRow(Row));
    }

    /// <summary>Включает или выключает строку заголовков; текущей становится первая строка.</summary>
    public void SetHeader(bool enabled)
    {
        if (enabled == Model.HasHeader)
            return;

        Change(() =>
        {
            Model.SetHeader(enabled);
            Row = 0;
        });
    }

    /// <summary>Ширина текущего столбца; false — значение не изменилось.</summary>
    public bool SetColumnWidth(double widthMm) => SetColumnWidth(Column, widthMm);

    /// <summary>
    /// Ширина столбца; остальные столбцы не меняются. <paramref name="record"/> = false — без шага отмены
    /// (перетаскивание границы мышью: шаг записывается один раз в начале).
    /// </summary>
    public bool SetColumnWidth(int column, double widthMm, bool record = true) => SetSize(Model.ColumnWidths, column, widthMm, record);

    /// <summary>Высота текущей строки; false — значение не изменилось.</summary>
    public bool SetRowHeight(double heightMm) => SetSize(Model.RowHeights, Row, heightMm, record: true);

    private bool SetSize(List<double> sizes, int index, double value, bool record)
    {
        if (index < 0 || index >= sizes.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (!double.IsFinite(value) || value < MmParser.MinCellSize || value > MmParser.MaxCellSize)
            throw new ArgumentOutOfRangeException(nameof(value));
        if (Math.Abs(sizes[index] - value) < 1e-9)
            return false;

        if (record)
            Record();
        sizes[index] = value;
        return true;
    }

    /// <summary>Изменение структуры: шаг отмены, сброс выделения, текущая ячейка — в пределах таблицы.</summary>
    private void Change(Action action)
    {
        Record();
        action();
        Range = null;
        Row = Math.Clamp(Row, 0, Model.RowCount - 1);
        Column = Math.Clamp(Column, 0, Model.ColumnCount - 1);
    }
}
