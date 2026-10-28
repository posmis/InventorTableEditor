namespace InventorTableEditor.Core.Model;

/// <summary>
/// Как таблица была открыта в редакторе: размер окна и масштаб. Хранится вместе с таблицей,
/// чтобы при следующем открытии окно было таким же. На чертёж не влияет.
/// </summary>
public sealed class EditorView
{
    public const double MinWidth = 720;
    public const double MinHeight = 440;
    public const double MaxSize = 10000;

    public double Width { get; set; } = 1100;

    public double Height { get; set; } = 700;

    public bool Maximized { get; set; }

    /// <summary>Масштаб таблицы в окне (1 — натуральный размер).</summary>
    public double Zoom { get; set; } = 1.5;

    public EditorView Clone() => (EditorView)MemberwiseClone();

    /// <summary>Приводит к допустимым значениям (повреждённые или чужие данные).</summary>
    public void Normalize()
    {
        Width = double.IsFinite(Width) ? Math.Clamp(Width, MinWidth, MaxSize) : new EditorView().Width;
        Height = double.IsFinite(Height) ? Math.Clamp(Height, MinHeight, MaxSize) : new EditorView().Height;
        Zoom = Editing.Zoom.Clamp(Zoom);
    }
}
