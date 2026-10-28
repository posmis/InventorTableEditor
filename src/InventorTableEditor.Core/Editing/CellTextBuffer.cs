using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Editing;

/// <summary>
/// Текст ячейки для редактирования: символы с форматом каждого, курсор, выделение,
/// формат для набираемого текста и история отмены. Без зависимостей от WPF.
/// Индексы — позиции между символами (0 — перед первым).
/// </summary>
public sealed class CellTextBuffer
{
    private readonly TableStyle _style;
    private readonly Stack<Snapshot> _undo = new();
    private readonly Stack<Snapshot> _redo = new();
    private List<char> _chars = new();
    private List<TextRun> _formats = new();

    public CellTextBuffer(TableCell cell, TableStyle style)
    {
        _style = style;
        foreach (var run in cell.Runs)
        {
            var format = run.WithText(string.Empty);
            foreach (var ch in run.Text)
            {
                _chars.Add(ch);
                _formats.Add(format);
            }
        }

        TypingFormat = FormatAt(0);
    }

    public string Text => new(_chars.ToArray());

    public int Length => _chars.Count;

    public int Caret { get; private set; }

    /// <summary>Второй конец выделения; равен <see cref="Caret"/>, если выделения нет.</summary>
    public int Anchor { get; private set; }

    public int SelectionStart => Math.Min(Caret, Anchor);

    public int SelectionEnd => Math.Max(Caret, Anchor);

    public bool HasSelection => Caret != Anchor;

    public string SelectedText => new(_chars.GetRange(SelectionStart, SelectionEnd - SelectionStart).ToArray());

    /// <summary>Формат, с которым будет набран следующий текст (как в Word: формат символа перед курсором).</summary>
    public TextRun TypingFormat { get; private set; }

    /// <summary>Формат для панели инструментов: первого выделенного символа или набираемого текста.</summary>
    public TextRun CurrentFormat => HasSelection ? _formats[SelectionStart] : TypingFormat;

    public TableCell ToCell(TableCell template)
    {
        var cell = new TableCell
        {
            HorizontalAlignment = template.HorizontalAlignment,
            VerticalAlignment = template.VerticalAlignment,
        };
        for (var i = 0; i < _chars.Count; i++)
        {
            if (cell.Runs.Count > 0 && cell.Runs[^1].SameFormat(_formats[i]))
                cell.Runs[^1].Text += _chars[i];
            else
                cell.Runs.Add(_formats[i].WithText(_chars[i].ToString()));
        }

        cell.Normalize();
        return cell;
    }

    public void MoveTo(int index, bool extend)
    {
        Caret = Math.Clamp(index, 0, _chars.Count);
        if (!extend)
            Anchor = Caret;
        TypingFormat = FormatAt(Caret);
    }

    public void SelectAll()
    {
        Anchor = 0;
        Caret = _chars.Count;
    }

    /// <summary>Начало слова слева от позиции (для Ctrl+←).</summary>
    public int WordLeft(int index)
    {
        var i = Math.Clamp(index, 0, _chars.Count);
        while (i > 0 && char.IsWhiteSpace(_chars[i - 1]))
            i--;
        while (i > 0 && !char.IsWhiteSpace(_chars[i - 1]))
            i--;
        return i;
    }

    /// <summary>Начало следующего слова справа от позиции (для Ctrl+→).</summary>
    public int WordRight(int index)
    {
        var i = Math.Clamp(index, 0, _chars.Count);
        while (i < _chars.Count && !char.IsWhiteSpace(_chars[i]))
            i++;
        while (i < _chars.Count && char.IsWhiteSpace(_chars[i]))
            i++;
        return i;
    }

    /// <summary>Вставляет текст вместо выделения с форматом <see cref="TypingFormat"/>.</summary>
    public void Insert(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (text.Length == 0 && !HasSelection)
            return;

        SaveUndo();
        var format = TypingFormat;
        RemoveSelection();
        _chars.InsertRange(Caret, text);
        _formats.InsertRange(Caret, Enumerable.Repeat(format, text.Length));
        Caret += text.Length;
        Anchor = Caret;
        TypingFormat = format;
    }

    public void Backspace()
    {
        if (HasSelection)
        {
            DeleteSelection();
            return;
        }

        if (Caret == 0)
            return;

        SaveUndo();
        Caret--;
        Anchor = Caret;
        _chars.RemoveAt(Caret);
        _formats.RemoveAt(Caret);
        TypingFormat = FormatAt(Caret);
    }

    public void Delete()
    {
        if (HasSelection)
        {
            DeleteSelection();
            return;
        }

        if (Caret == _chars.Count)
            return;

        SaveUndo();
        _chars.RemoveAt(Caret);
        _formats.RemoveAt(Caret);
        TypingFormat = FormatAt(Caret);
    }

    public void DeleteSelection()
    {
        if (!HasSelection)
            return;

        SaveUndo();
        RemoveSelection();
        TypingFormat = FormatAt(Caret);
    }

    public void ToggleBold() => Toggle(f => f.Bold, (f, on) => f.Bold = on);

    public void ToggleItalic() => Toggle(f => f.Italic, (f, on) => f.Italic = on);

    public void ToggleUnderline() => Toggle(f => f.Underline, (f, on) => f.Underline = on);

    /// <summary>Включает индекс, а если он уже включён у всего выделения (или у набираемого текста) — выключает.</summary>
    public void ToggleScript(ScriptPosition script) =>
        Toggle(f => f.Script == script, (f, on) => f.Script = on ? script : ScriptPosition.Normal);

    /// <summary>Обычный текст вместо индекса (Ctrl+Пробел, как в Word).</summary>
    public void ResetScript() => Apply(f => f.Script = ScriptPosition.Normal);

    /// <summary>Шрифт выделения или набираемого текста; шрифт таблицы хранится как null.</summary>
    public void SetFontName(string fontName)
    {
        var value = string.Equals(fontName, _style.FontName, StringComparison.OrdinalIgnoreCase) ? null : fontName;
        Apply(f => f.FontName = value);
    }

    /// <summary>Высота шрифта основного текста, мм; размер таблицы хранится как null.</summary>
    public void SetFontSize(double sizeMm)
    {
        double? value = Math.Abs(sizeMm - _style.FontSizeMm) < 1e-6 ? null : sizeMm;
        Apply(f => f.FontSizeMm = value);
    }

    /// <summary>
    /// Выполняется ли условие для всех символов выделения (без выделения — для набираемого текста).
    /// Для нескольких выделенных ячеек: включить формат, если он есть не у всего текста.
    /// </summary>
    public bool All(Func<TextRun, bool> predicate) =>
        HasSelection
            ? Enumerable.Range(SelectionStart, SelectionEnd - SelectionStart).All(i => predicate(_formats[i]))
            : predicate(TypingFormat);

    /// <summary>Меняет формат выделенных символов (без выделения — формат набираемого текста).</summary>
    public void ApplyFormat(Action<TextRun> change) => Apply(change);

    /// <summary>Есть ли в ячейке видимый текст (не только пробелы и переносы).</summary>
    public bool HasText => _chars.Any(ch => !char.IsWhiteSpace(ch));

    public bool Undo() => Restore(_undo, _redo);

    public bool Redo() => Restore(_redo, _undo);

    /// <summary>Формат по позиции: символа перед ней, а в начале строки — символа после неё.</summary>
    private TextRun FormatAt(int index)
    {
        if (index > 0 && _chars[index - 1] != '\n')
            return _formats[index - 1];
        if (index < _chars.Count && _chars[index] != '\n')
            return _formats[index];
        if (index > 0)
            return _formats[index - 1];
        return new TextRun(string.Empty, _style.Italic);
    }

    private void Toggle(Func<TextRun, bool> isOn, Action<TextRun, bool> set)
    {
        var allOn = All(isOn);
        Apply(f => set(f, !allOn));
    }

    /// <summary>Меняет формат выделенных символов, а без выделения — формат набираемого текста.</summary>
    private void Apply(Action<TextRun> change)
    {
        if (!HasSelection)
        {
            var typing = TypingFormat.Clone();
            change(typing);
            TypingFormat = typing;
            return;
        }

        SaveUndo();
        // Символы с одинаковым форматом получают одинаковый новый формат.
        var changed = new Dictionary<TextRun, TextRun>(ReferenceEqualityComparer.Instance);
        for (var i = SelectionStart; i < SelectionEnd; i++)
        {
            if (!changed.TryGetValue(_formats[i], out var updated))
            {
                updated = _formats[i].Clone();
                change(updated);
                changed[_formats[i]] = updated;
            }

            _formats[i] = updated;
        }

        TypingFormat = _formats[SelectionEnd - 1];
    }

    private void RemoveSelection()
    {
        var start = SelectionStart;
        var count = SelectionEnd - start;
        _chars.RemoveRange(start, count);
        _formats.RemoveRange(start, count);
        Caret = Anchor = start;
    }

    private void SaveUndo()
    {
        _undo.Push(new Snapshot(new List<char>(_chars), new List<TextRun>(_formats), Caret, Anchor));
        _redo.Clear();
    }

    private bool Restore(Stack<Snapshot> from, Stack<Snapshot> to)
    {
        if (from.Count == 0)
            return false;

        to.Push(new Snapshot(new List<char>(_chars), new List<TextRun>(_formats), Caret, Anchor));
        var snapshot = from.Pop();
        _chars = snapshot.Chars;
        _formats = snapshot.Formats;
        Caret = snapshot.Caret;
        Anchor = snapshot.Anchor;
        TypingFormat = FormatAt(Caret);
        return true;
    }

    private sealed record Snapshot(List<char> Chars, List<TextRun> Formats, int Caret, int Anchor);
}
