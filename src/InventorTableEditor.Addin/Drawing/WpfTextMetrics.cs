using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Addin.Drawing;

/// <summary>
/// Метрики текста по шрифтам Windows (<see cref="FontMetrics"/>) для расстановки и переноса.
/// Если шрифт не установлен — грубая оценка <see cref="ApproximateTextMetrics"/>.
/// </summary>
internal sealed class WpfTextMetrics : ITextMetrics
{
    private readonly TableStyle _style;

    public WpfTextMetrics(TableStyle style)
    {
        _style = style;
    }

    public TextExtents Measure(TextRun format, string text, double sizeMm) =>
        Font(format)?.Measure(text, sizeMm, format.Italic) ?? ApproximateTextMetrics.Instance.Measure(format, text, sizeMm);

    public double UnderlineOffsetMm(TextRun format, double sizeMm) =>
        Font(format)?.UnderlineOffsetMm(sizeMm) ?? ApproximateTextMetrics.Instance.UnderlineOffsetMm(format, sizeMm);

    private FontMetrics? Font(TextRun format) => FontMetrics.Get(format.FontName ?? _style.FontName);
}
