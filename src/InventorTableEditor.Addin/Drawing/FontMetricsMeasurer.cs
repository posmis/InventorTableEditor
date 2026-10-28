using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Addin.Drawing;

/// <summary>Ширина текста для переноса по словам — по метрикам шрифта, как его нарисует Inventor.</summary>
internal sealed class FontMetricsMeasurer : ITextMeasurer
{
    private readonly TableStyle _style;

    public FontMetricsMeasurer(TableStyle style)
    {
        _style = style;
    }

    public double AdvanceWidthMm(TextRun run, string text, double sizeMm)
    {
        var metrics = FontMetrics.Get(run.FontName ?? _style.FontName, run.Bold, run.Italic);
        return metrics?.Measure(text, sizeMm, run.Italic).AdvanceMm ?? text.Length * sizeMm * 0.6;
    }
}
