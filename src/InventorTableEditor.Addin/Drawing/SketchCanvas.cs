using System;
using Inventor;
using InventorTableEditor.Core;

namespace InventorTableEditor.Addin.Drawing;

/// <summary>
/// Рисование в эскизе символа в координатах таблицы: мм от левого верхнего угла, ось Y вниз.
/// Переводит их в см Inventor с осью Y вверх и прячет особенности API.
/// </summary>
internal sealed class SketchCanvas
{
    private readonly DrawingSketch _sketch;
    private readonly TransientGeometry _geometry;

    public SketchCanvas(DrawingSketch sketch, TransientGeometry geometry)
    {
        _sketch = sketch;
        _geometry = geometry;
    }

    /// <summary>Точка вставки символа — левый верхний угол таблицы.</summary>
    public void AddInsertionPoint()
    {
        var point = _sketch.SketchPoints.Add(Point(0, 0), false);
        TrySet(() => ((dynamic)point).InsertionPoint = true);
    }

    public void AddLine(double x1Mm, double y1Mm, double x2Mm, double y2Mm, double weightMm)
    {
        var line = _sketch.SketchLines.AddByTwoPoints(Point(x1Mm, y1Mm), Point(x2Mm, y2Mm));
        var weightCm = Units.MmToCm(weightMm);
        TrySet(() => ((dynamic)line).LineWeight = weightCm);
    }

    /// <summary>Надпись по размеру текста; ставится на место через <see cref="Place"/>.</summary>
    public Inventor.TextBox AddText(string text, TextStyle style)
    {
        var box = _sketch.TextBoxes.AddFitted(Point(0, 0), EscapeText(text), style);
        box.HorizontalJustification = HorizontalTextAlignmentEnum.kAlignTextLeft;
        box.VerticalJustification = VerticalTextAlignmentEnum.kAlignTextUpper;
        return box;
    }

    /// <summary>
    /// Ставит надпись левым верхним углом в точку. Надпись Inventor — прямоугольник от левого края надписи
    /// до правого края очертаний букв, по высоте — по очертаниям букв; Origin — его левый верхний угол.
    /// </summary>
    public void Place(Inventor.TextBox box, double leftMm, double topMm) => box.Origin = Point(leftMm, topMm);

    /// <summary>Ширина надписи, как её сообщает Inventor (без коэффициента ширины стиля), мм; null — не удалось.</summary>
    public double? TryMeasureWidthMm(Inventor.TextBox box)
    {
        try
        {
            double width = ((dynamic)box).Width;
            return width > 0 ? Units.CmToMm(width) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Габарит надписи после установки, мм таблицы (Y вниз): левый, верхний, правый, нижний края; null — недоступен.</summary>
    public (double Left, double Top, double Right, double Bottom)? TryGetBoundsMm(Inventor.TextBox box)
    {
        try
        {
            Box2d range = ((dynamic)box).RangeBox;
            return (Units.CmToMm(range.MinPoint.X), -Units.CmToMm(range.MaxPoint.Y),
                    Units.CmToMm(range.MaxPoint.X), -Units.CmToMm(range.MinPoint.Y));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Текст надписи Inventor разбирается как разметка: экранируются только &amp;, &lt; и &gt;.</summary>
    internal static string EscapeText(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private Point2d Point(double xMm, double yMm) => _geometry.CreatePoint2d(Units.MmToCm(xMm), -Units.MmToCm(yMm));

    private static void TrySet(Action action)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            // Свойство недоступно в этой версии API — таблица всё равно строится.
        }
    }
}
