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

    /// <summary>Толщина линии под строкой заголовков, мм (по умолчанию как у рамки).</summary>
    public double HeaderLineMm { get; set; } = 0.5;

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

    /// <summary>
    /// Предел сжатия по ширине перед переносом (при включённом переносе): строка сначала сжимается
    /// до этой доли, и только если не помещается и так — переносится по словам. Настраивается в окне настроек.
    /// </summary>
    public double MinScaleBeforeWrap { get; set; } = 0.75;

    public double DefaultColumnWidthMm { get; set; } = 20.0;

    public double DefaultRowHeightMm { get; set; } = 10.0;

    /// <summary>Выравнивание по горизонтали в строке заголовков, если у ячейки не задано своё.</summary>
    public HorizontalTextAlignment HeaderAlignment { get; set; } = HorizontalTextAlignment.Center;

    /// <summary>Выравнивание по горизонтали в остальных строках, если у ячейки не задано своё.</summary>
    public HorizontalTextAlignment DataAlignment { get; set; } = HorizontalTextAlignment.Left;

    /// <summary>Выравнивание по вертикали, если у ячейки не задано своё.</summary>
    public VerticalTextAlignment VerticalAlignment { get; set; } = VerticalTextAlignment.Middle;

    public TableStyle Clone() => (TableStyle)MemberwiseClone();

    /// <summary>Совпадают ли все параметры оформления.</summary>
    public bool SameAs(TableStyle other) =>
        System.Text.Json.JsonSerializer.Serialize(this) == System.Text.Json.JsonSerializer.Serialize(other);

    /// <summary>Допустимые значения параметров — для окна настроек и проверки файла настроек.</summary>
    public static class Limits
    {
        public const double MinFontSize = 1;
        public const double MaxFontSize = 50;
        public const double MinLine = 0.05;
        public const double MaxLine = 3;
        public const double MaxPadding = 10;
        public const double MinLineSpacing = 1;
        public const double MaxLineSpacing = 3;
        public const double MinScriptScale = 0.3;
        public const double MaxScriptScale = 1;
        public const double MaxScriptShift = 1;
        public const double MinCompression = 0.3;
    }

    /// <summary>Ошибки в параметрах (по-русски, для окна настроек); пустой список — всё в порядке.</summary>
    public List<string> Check()
    {
        var errors = new List<string>();

        void Range(string name, double value, double min, double max, string unit)
        {
            if (!double.IsFinite(value) || value < min || value > max)
                errors.Add($"{name}: от {min:0.##} до {max:0.##} {unit}.");
        }

        if (string.IsNullOrWhiteSpace(FontName))
            errors.Add("Шрифт: не задан.");
        Range("Высота шрифта", FontSizeMm, Limits.MinFontSize, Limits.MaxFontSize, "мм");
        Range("Межстрочный интервал", LineSpacing, Limits.MinLineSpacing, Limits.MaxLineSpacing, "высоты шрифта");
        Range("Отступ текста", PaddingMm, 0, Limits.MaxPadding, "мм");
        Range("Ширина столбца", DefaultColumnWidthMm, Editing.MmParser.MinCellSize, Editing.MmParser.MaxCellSize, "мм");
        Range("Высота строки", DefaultRowHeightMm, Editing.MmParser.MinCellSize, Editing.MmParser.MaxCellSize, "мм");
        Range("Рамка", OuterLineMm, Limits.MinLine, Limits.MaxLine, "мм");
        Range("Разделители", InnerLineMm, Limits.MinLine, Limits.MaxLine, "мм");
        Range("Линия под заголовками", HeaderLineMm, Limits.MinLine, Limits.MaxLine, "мм");
        Range("Подчёркивание", UnderlineMm, Limits.MinLine, Limits.MaxLine, "мм");
        Range("Размер индекса", ScriptScale * 100, Limits.MinScriptScale * 100, Limits.MaxScriptScale * 100, "%");
        Range("Подъём верхнего индекса", SuperscriptRise * 100, 0, Limits.MaxScriptShift * 100, "%");
        Range("Опускание нижнего индекса", SubscriptDrop * 100, 0, Limits.MaxScriptShift * 100, "%");
        Range("Предел сжатия перед переносом", MinScaleBeforeWrap * 100, Limits.MinCompression * 100, 100, "%");
        if (!Enum.IsDefined(HeaderAlignment) || !Enum.IsDefined(DataAlignment) || !Enum.IsDefined(VerticalAlignment))
            errors.Add("Выравнивание: недопустимое значение.");
        return errors;
    }

    /// <summary>
    /// Приводит к допустимым значениям (повреждённый файл настроек, данные старой версии): каждый
    /// неверный параметр заменяется заводским.
    /// </summary>
    public void Repair()
    {
        var factory = new TableStyle();
        if (string.IsNullOrWhiteSpace(FontName)) FontName = factory.FontName;
        FontSizeMm = Fix(FontSizeMm, Limits.MinFontSize, Limits.MaxFontSize, factory.FontSizeMm);
        LineSpacing = Fix(LineSpacing, Limits.MinLineSpacing, Limits.MaxLineSpacing, factory.LineSpacing);
        PaddingMm = Fix(PaddingMm, 0, Limits.MaxPadding, factory.PaddingMm);
        DefaultColumnWidthMm = Fix(DefaultColumnWidthMm, Editing.MmParser.MinCellSize, Editing.MmParser.MaxCellSize, factory.DefaultColumnWidthMm);
        DefaultRowHeightMm = Fix(DefaultRowHeightMm, Editing.MmParser.MinCellSize, Editing.MmParser.MaxCellSize, factory.DefaultRowHeightMm);
        OuterLineMm = Fix(OuterLineMm, Limits.MinLine, Limits.MaxLine, factory.OuterLineMm);
        InnerLineMm = Fix(InnerLineMm, Limits.MinLine, Limits.MaxLine, factory.InnerLineMm);
        HeaderLineMm = Fix(HeaderLineMm, Limits.MinLine, Limits.MaxLine, factory.HeaderLineMm);
        UnderlineMm = Fix(UnderlineMm, Limits.MinLine, Limits.MaxLine, factory.UnderlineMm);
        ScriptScale = Fix(ScriptScale, Limits.MinScriptScale, Limits.MaxScriptScale, factory.ScriptScale);
        SuperscriptRise = Fix(SuperscriptRise, 0, Limits.MaxScriptShift, factory.SuperscriptRise);
        SubscriptDrop = Fix(SubscriptDrop, 0, Limits.MaxScriptShift, factory.SubscriptDrop);
        MinScaleBeforeWrap = Fix(MinScaleBeforeWrap, Limits.MinCompression, 1, factory.MinScaleBeforeWrap);
        if (!Enum.IsDefined(HeaderAlignment)) HeaderAlignment = factory.HeaderAlignment;
        if (!Enum.IsDefined(DataAlignment)) DataAlignment = factory.DataAlignment;
        if (!Enum.IsDefined(VerticalAlignment)) VerticalAlignment = factory.VerticalAlignment;
    }

    private static double Fix(double value, double min, double max, double fallback) =>
        double.IsFinite(value) && value >= min && value <= max ? value : fallback;
}
