using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Editing;

/// <summary>Где был курсор: ячейка и позиция в её тексте.</summary>
public readonly record struct TableCursor(int Row, int Column, int Caret);

/// <summary>
/// История отмены для всей таблицы, как в Excel и Word: каждое действие — один шаг (снимок таблицы до него).
/// Подряд идущие действия с одинаковым ключом объединения (набор текста в одной ячейке) — один шаг.
/// </summary>
public sealed class TableHistory
{
    private readonly Stack<Entry> _undo = new();
    private readonly Stack<Entry> _redo = new();
    private string? _mergeKey;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Запоминает таблицу до действия. <paramref name="mergeKey"/> — ключ объединения: если он совпадает
    /// с ключом предыдущего действия, новый шаг не создаётся (продолжение набора в той же ячейке).
    /// </summary>
    public void Record(TableModel before, TableCursor cursor, string? mergeKey = null)
    {
        if (mergeKey != null && mergeKey == _mergeKey)
            return;

        _undo.Push(new Entry(before.ToJson(), cursor));
        _redo.Clear();
        _mergeKey = mergeKey;
    }

    /// <summary>Следующее действие начнёт новый шаг (курсор ушёл в другую ячейку или в другое место).</summary>
    public void BreakMerge() => _mergeKey = null;

    /// <summary>Отменяет шаг: возвращает таблицу и курсор до него; null — отменять нечего.</summary>
    public (TableModel Model, TableCursor Cursor)? Undo(TableModel current, TableCursor cursor) => Move(_undo, _redo, current, cursor);

    /// <summary>Повторяет отменённый шаг; null — повторять нечего.</summary>
    public (TableModel Model, TableCursor Cursor)? Redo(TableModel current, TableCursor cursor) => Move(_redo, _undo, current, cursor);

    private (TableModel Model, TableCursor Cursor)? Move(Stack<Entry> from, Stack<Entry> to, TableModel current, TableCursor cursor)
    {
        _mergeKey = null;
        if (from.Count == 0)
            return null;

        to.Push(new Entry(current.ToJson(), cursor));
        var entry = from.Pop();
        return (TableModel.FromJson(entry.Json), entry.Cursor);
    }

    private sealed record Entry(string Json, TableCursor Cursor);
}
