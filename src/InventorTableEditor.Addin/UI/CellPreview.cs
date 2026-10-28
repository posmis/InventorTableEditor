using System.Globalization;
using System.Windows;
using System.Windows.Media;
using InventorTableEditor.Addin.Drawing;
using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Addin.UI;

/// <summary>
/// Текст ячейки так, как он будет на чертеже: та же расстановка (<see cref="TextPlanner"/>),
/// индексы, наклон курсива как в Inventor и сжатие по ширине. Показывается, пока ячейка не редактируется.
/// </summary>
internal sealed class CellPreview : FrameworkElement
{
    private readonly TableStyle _style;
    private readonly double _pxPerMm;
    private CellText? _cell;

    public CellPreview(TableStyle style, double pxPerMm)
    {
        _style = style;
        _pxPerMm = pxPerMm;
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    /// <summary>Ячейка из раскладки таблицы (координаты в мм от угла таблицы); null — пустая.</summary>
    public CellText? Cell
    {
        get => _cell;
        set
        {
            _cell = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext context)
    {
        if (_cell == null)
            return;

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var line in _cell.Lines)
        {
            var plan = TextPlanner.PlanLine(line, _style, _cell);
            foreach (var item in plan.Runs)
            {
                var run = item.Run;
                var fontName = run.FontName ?? _style.FontName;
                var emPerCap = FontMetrics.Get(fontName, run.Bold, italic: false)?.EmPerCapHeight ?? RichTextConverter.EmPerTextHeight;
                var typeface = new Typeface(RichTextConverter.FontFor(fontName), FontStyles.Normal,
                    run.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
                var text = new FormattedText(run.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface,
                    item.SizeMm * emPerCap * _pxPerMm, Brushes.Black, pixelsPerDip);
                if (run.Underline)
                    text.SetTextDecorations(TextDecorations.Underline);

                // Начало фрагмента на базовой линии; сжатие по ширине и наклон — относительно него.
                var x = (plan.OriginMm + item.PenMm * plan.WidthScale - _cell.Left) * _pxPerMm;
                var baseline = (item.BaselineMm - _cell.Top) * _pxPerMm;
                var slant = run.Italic ? FontMetrics.ItalicSlant : 0.0;
                var scale = plan.WidthScale;

                context.PushTransform(new MatrixTransform(scale, 0, -slant * scale, 1, x, baseline));
                context.DrawText(text, new Point(0, -text.Baseline));
                context.Pop();
            }
        }
    }
}
