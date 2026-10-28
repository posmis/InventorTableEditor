using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using InventorTableEditor.Addin.Drawing;
using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Addin.UI;

/// <summary>
/// Предпросмотр таблицы (для списка шаблонов): линии и текст по той же раскладке, что и в чертеже.
/// Текст рисуют <see cref="CellEditor"/> без возможности правки.
/// </summary>
internal static class TablePreview
{
    private const double PxPerMm = 96.0 / 25.4;

    public static FrameworkElement Create(TableModel model, Brush accent)
    {
        var layout = TableLayout.Build(model, new WpfTextMetrics(model.Style), includeEmptyCells: true);
        var canvas = new Canvas
        {
            Width = layout.Width * PxPerMm,
            Height = layout.Height * PxPerMm,
            Background = Brushes.White,
            IsHitTestVisible = false,
        };

        foreach (var text in layout.Texts)
        {
            var editor = new CellEditor(model.GetCell(text.Row, text.Column), model.Style, PxPerMm, Brushes.White, accent)
            {
                Width = text.Width * PxPerMm,
                Height = text.Height * PxPerMm,
                Focusable = false,
                Layout = text,
            };
            Canvas.SetLeft(editor, text.Left * PxPerMm);
            Canvas.SetTop(editor, text.Top * PxPerMm);
            canvas.Children.Add(editor);
        }

        foreach (var line in layout.Lines)
        {
            canvas.Children.Add(new Line
            {
                X1 = line.X1 * PxPerMm,
                Y1 = line.Y1 * PxPerMm,
                X2 = line.X2 * PxPerMm,
                Y2 = line.Y2 * PxPerMm,
                Stroke = Brushes.Black,
                StrokeThickness = Math.Max(0.75, line.WeightMm * PxPerMm),
                StrokeStartLineCap = PenLineCap.Square,
                StrokeEndLineCap = PenLineCap.Square,
            });
        }

        return canvas;
    }
}
