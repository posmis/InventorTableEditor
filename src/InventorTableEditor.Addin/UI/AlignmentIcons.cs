using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Addin.UI;

/// <summary>Значки кнопок выравнивания, нарисованные линиями.</summary>
internal static class AlignmentIcons
{
    private const double Size = 16;

    /// <summary>Четыре строки текста разной длины, выровненные влево, по центру или вправо.</summary>
    public static UIElement Horizontal(HorizontalTextAlignment alignment)
    {
        var canvas = new Canvas { Width = Size, Height = Size };
        double[] widths = { 14, 9, 14, 9 };
        for (var i = 0; i < widths.Length; i++)
        {
            var left = alignment switch
            {
                HorizontalTextAlignment.Center => (Size - widths[i]) / 2,
                HorizontalTextAlignment.Right => Size - 1 - widths[i],
                _ => 1,
            };
            AddBar(canvas, left, 2 + i * 3.5, widths[i], 2);
        }

        return canvas;
    }

    /// <summary>Рамка ячейки с полосой текста у верхнего края, посередине или у нижнего.</summary>
    public static UIElement Vertical(VerticalTextAlignment alignment)
    {
        var canvas = new Canvas { Width = Size, Height = Size };
        var frame = new Rectangle { Width = 14, Height = 14, Stroke = Brushes.DimGray, StrokeThickness = 1 };
        Canvas.SetLeft(frame, 1);
        Canvas.SetTop(frame, 1);
        canvas.Children.Add(frame);

        var top = alignment switch
        {
            VerticalTextAlignment.Top => 3.0,
            VerticalTextAlignment.Bottom => 10.0,
            _ => 6.5,
        };
        AddBar(canvas, 4, top, 8, 3);
        return canvas;
    }

    private static void AddBar(Canvas canvas, double left, double top, double width, double height)
    {
        var bar = new Rectangle { Width = width, Height = height, Fill = Brushes.Black };
        Canvas.SetLeft(bar, left);
        Canvas.SetTop(bar, top);
        canvas.Children.Add(bar);
    }
}
