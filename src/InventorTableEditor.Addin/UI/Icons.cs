using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Addin.UI;

/// <summary>
/// Значки окна редактора в стиле Inventor: векторные 16×16, тёмно-серые линии, синий акцент Autodesk,
/// зелёный «+» — вставка, красный «×» — удаление. Таблица нарисована сеткой 3×3, как на значках КОМПАС.
/// </summary>
internal static class Icons
{
    private const double Size = 16;

    private static readonly Brush Ink = Frozen(Color.FromRgb(0x40, 0x40, 0x40));
    private static readonly Brush GridLine = Frozen(Color.FromRgb(0x8C, 0x8C, 0x8C));
    private static readonly Brush Accent = Frozen(Color.FromRgb(0x06, 0x96, 0xD7));
    private static readonly Brush AccentFill = Frozen(Color.FromRgb(0xBF, 0xE3, 0xF5));
    private static readonly Brush Add = Frozen(Color.FromRgb(0x2E, 0x9E, 0x47));
    private static readonly Brush Remove = Frozen(Color.FromRgb(0xD9, 0x3B, 0x3B));
    private static readonly Brush RemoveFill = Frozen(Color.FromRgb(0xF6, 0xC9, 0xC9));

    public enum Badge
    {
        None,
        Add,
        Remove,
    }

    /// <summary>Таблица с выделенным столбцом (0–2) и значком действия.</summary>
    public static UIElement Column(int column, Badge badge) =>
        Table(new Rect(1.5 + column * 4.33, 2.5, 4.33, 11), badge);

    /// <summary>Таблица с выделенной строкой (0–2) и значком действия.</summary>
    public static UIElement Row(int row, Badge badge) =>
        Table(new Rect(1.5, 2.5 + row * 3.67, 13, 3.67), badge);

    /// <summary>Переносить текст: строка, которая заворачивает на следующую (как значок Excel).</summary>
    public static UIElement WrapText()
    {
        var drawing = new DrawingGroup();
        Line(drawing, Ink, 1.5, new Point(2, 3.5), new Point(14, 3.5));
        var pen = new Pen(Accent, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        drawing.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M2,7.5 H11.5 A2,2 0 0 1 11.5,11.5 H7.5")));
        Line(drawing, Accent, 1.5, new Point(7.5, 11.5), new Point(9.3, 9.8));
        Line(drawing, Accent, 1.5, new Point(7.5, 11.5), new Point(9.3, 13.2));
        Line(drawing, Ink, 1.5, new Point(2, 11.5), new Point(4.5, 11.5));
        return Image(drawing);
    }

    /// <summary>Объединить ячейки: две ячейки сливаются в одну (синяя), стрелки навстречу друг другу.</summary>
    public static UIElement Merge()
    {
        var drawing = new DrawingGroup();
        var cell = new Rect(1.5, 3.5, 13, 9);
        Fill(drawing, AccentFill, cell);
        Stroke(drawing, Ink, 1, cell);
        Arrow(drawing, new Point(3, 8), new Point(7, 8));
        Arrow(drawing, new Point(13, 8), new Point(9, 8));
        return Image(drawing);
    }

    /// <summary>Разделить ячейку: пунктирная граница посередине, стрелки в стороны.</summary>
    public static UIElement Split()
    {
        var drawing = new DrawingGroup();
        var cell = new Rect(1.5, 3.5, 13, 9);
        Fill(drawing, AccentFill, cell);
        Stroke(drawing, Ink, 1, cell);
        drawing.Children.Add(new GeometryDrawing(null, new Pen(Accent, 1) { DashStyle = new DashStyle(new[] { 1.5, 1.5 }, 0) },
            new LineGeometry(new Point(8, 3.5), new Point(8, 12.5))));
        Arrow(drawing, new Point(6.5, 8), new Point(3, 8));
        Arrow(drawing, new Point(9.5, 8), new Point(13, 8));
        return Image(drawing);
    }

    private static void Arrow(DrawingGroup drawing, Point from, Point to)
    {
        Line(drawing, Accent, 1.2, from, to);
        var direction = to.X > from.X ? -1 : 1;
        Line(drawing, Accent, 1.2, to, new Point(to.X + direction * 1.8, to.Y - 1.8));
        Line(drawing, Accent, 1.2, to, new Point(to.X + direction * 1.8, to.Y + 1.8));
    }

    /// <summary>Выравнивание по горизонтали: четыре строки текста разной длины.</summary>
    public static UIElement Horizontal(HorizontalTextAlignment alignment)
    {
        var drawing = new DrawingGroup();
        double[] widths = { 13, 8, 13, 8 };
        for (var i = 0; i < widths.Length; i++)
        {
            var left = alignment switch
            {
                HorizontalTextAlignment.Center => (Size - widths[i]) / 2,
                HorizontalTextAlignment.Right => Size - 1.5 - widths[i],
                _ => 1.5,
            };
            Fill(drawing, Ink, new Rect(left, 2.5 + i * 3.25, widths[i], 1.5));
        }

        return Image(drawing);
    }

    /// <summary>Выравнивание по вертикали: рамка ячейки и строка текста у верха, посередине или у низа.</summary>
    public static UIElement Vertical(VerticalTextAlignment alignment)
    {
        var drawing = new DrawingGroup();
        Stroke(drawing, GridLine, 1, new Rect(1.5, 1.5, 13, 13));
        var top = alignment switch
        {
            VerticalTextAlignment.Top => 3.5,
            VerticalTextAlignment.Bottom => 10.5,
            _ => 7,
        };
        Fill(drawing, Accent, new Rect(4, top, 8, 2));
        return Image(drawing);
    }

    /// <summary>Буква начертания (Ж, К, Ч) шрифтом интерфейса.</summary>
    public static UIElement Letter(string letter, FontWeight weight, FontStyle style, bool underline) => new TextBlock
    {
        Text = letter,
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 14,
        FontWeight = weight,
        FontStyle = style,
        Foreground = Ink,
        TextDecorations = underline ? TextDecorations.Underline : null,
        Width = Size,
        TextAlignment = TextAlignment.Center,
    };

    /// <summary>Индекс: «x» и синяя маленькая «2» вверху или внизу.</summary>
    public static UIElement Script(ScriptPosition script)
    {
        var canvas = new Canvas { Width = Size, Height = Size };
        var x = new TextBlock { Text = "x", FontFamily = new FontFamily("Segoe UI"), FontSize = 13, Foreground = Ink };
        Canvas.SetLeft(x, 1);
        Canvas.SetTop(x, -1);
        var index = new TextBlock { Text = "2", FontFamily = new FontFamily("Segoe UI"), FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = Accent };
        Canvas.SetLeft(index, 9);
        Canvas.SetTop(index, script == ScriptPosition.Superscript ? -1 : 7);
        canvas.Children.Add(x);
        canvas.Children.Add(index);
        return canvas;
    }

    private static UIElement Table(Rect highlight, Badge badge)
    {
        var drawing = new DrawingGroup();
        Fill(drawing, badge == Badge.Remove ? RemoveFill : AccentFill, highlight);

        var outline = new Rect(1.5, 2.5, 13, 11);
        for (var i = 1; i < 3; i++)
        {
            Line(drawing, GridLine, 1, new Point(outline.Left + outline.Width * i / 3, outline.Top), new Point(outline.Left + outline.Width * i / 3, outline.Bottom));
            Line(drawing, GridLine, 1, new Point(outline.Left, outline.Top + outline.Height * i / 3), new Point(outline.Right, outline.Top + outline.Height * i / 3));
        }

        Stroke(drawing, Ink, 1, outline);
        Stroke(drawing, badge == Badge.Remove ? Remove : Accent, 1, highlight);

        switch (badge)
        {
            case Badge.Add:
                BadgeCircle(drawing, Add);
                Line(drawing, Brushes.White, 1.4, new Point(10.2, 12.5), new Point(14.8, 12.5));
                Line(drawing, Brushes.White, 1.4, new Point(12.5, 10.2), new Point(12.5, 14.8));
                break;
            case Badge.Remove:
                BadgeCircle(drawing, Remove);
                Line(drawing, Brushes.White, 1.4, new Point(10.9, 10.9), new Point(14.1, 14.1));
                Line(drawing, Brushes.White, 1.4, new Point(14.1, 10.9), new Point(10.9, 14.1));
                break;
        }

        return Image(drawing);
    }

    private static void BadgeCircle(DrawingGroup drawing, Brush brush) =>
        drawing.Children.Add(new GeometryDrawing(brush, new Pen(Brushes.White, 1), new EllipseGeometry(new Point(12.5, 12.5), 3.6, 3.6)));

    private static void Fill(DrawingGroup drawing, Brush brush, Rect rect) =>
        drawing.Children.Add(new GeometryDrawing(brush, null, new RectangleGeometry(rect)));

    private static void Stroke(DrawingGroup drawing, Brush brush, double thickness, Rect rect) =>
        drawing.Children.Add(new GeometryDrawing(null, new Pen(brush, thickness), new RectangleGeometry(rect)));

    private static void Line(DrawingGroup drawing, Brush brush, double thickness, Point from, Point to) =>
        drawing.Children.Add(new GeometryDrawing(null, new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
            new LineGeometry(from, to)));

    /// <summary>Значок фиксированного размера: рисунок вписан в квадрат 16×16 (прозрачная рамка задаёт границы).</summary>
    private static UIElement Image(DrawingGroup drawing)
    {
        drawing.Children.Insert(0, new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, Size, Size))));
        drawing.Freeze();
        return new Image
        {
            Source = new DrawingImage(drawing),
            Width = Size,
            Height = Size,
            SnapsToDevicePixels = true,
        };
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
