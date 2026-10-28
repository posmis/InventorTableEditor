namespace InventorTableEditor.Core.Editing;

/// <summary>Вид правки текста ячейки — для объединения шагов истории отмены.</summary>
public enum EditKind
{
    /// <summary>Набор текста: подряд в одной ячейке — один шаг.</summary>
    Typing,

    /// <summary>Backspace и Delete: подряд в одной ячейке — один шаг.</summary>
    Deleting,

    /// <summary>Прочее (формат, вставка, перенос строки) — отдельный шаг.</summary>
    Other,
}
