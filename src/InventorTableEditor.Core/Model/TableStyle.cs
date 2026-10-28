namespace InventorTableEditor.Core.Model;

/// <summary>
/// Оформление таблицы. Все размеры в миллиметрах.
/// </summary>
public sealed class TableStyle
{
    public string FontName { get; set; } = "Gost merged";

    public double FontSizeMm { get; set; } = 5.0;

    public bool Italic { get; set; } = true;

    public double OuterLineMm { get; set; } = 0.5;

    public double InnerLineMm { get; set; } = 0.25;

    public double PaddingMm { get; set; } = 1.0;

    /// <summary>Толщина линии подчёркивания, мм (подчёркивание шрифта Inventor слишком толстое).</summary>
    public double UnderlineMm { get; set; } = 0.2;

    /// <summary>Шаг строк текста внутри ячейки в долях высоты шрифта.</summary>
    public double LineSpacing { get; set; } = 1.6;

    /// <summary>Размер индекса в долях размера основного текста.</summary>
    public double ScriptScale { get; set; } = 0.65;

    /// <summary>Подъём базовой линии верхнего индекса в долях размера основного текста.</summary>
    public double SuperscriptRise { get; set; } = 0.5;

    /// <summary>Опускание базовой линии нижнего индекса в долях размера основного текста.</summary>
    public double SubscriptDrop { get; set; } = 0.25;

    public double DefaultColumnWidthMm { get; set; } = 20.0;

    public double DefaultRowHeightMm { get; set; } = 8.0;

    public TableStyle Clone() => (TableStyle)MemberwiseClone();
}
