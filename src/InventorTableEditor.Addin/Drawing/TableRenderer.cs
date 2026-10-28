using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security;
using System.Text;
using Inventor;
using InventorTableEditor.Core;
using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;
using CellText = InventorTableEditor.Core.Layout.CellText;
using HorizontalTextAlignment = InventorTableEditor.Core.Model.HorizontalTextAlignment;
using TextSegment = InventorTableEditor.Core.Layout.TextSegment;
using TableStyle = InventorTableEditor.Core.Model.TableStyle;

namespace InventorTableEditor.Addin.Drawing;

/// <summary>
/// Строит таблицу в эскизе определения символа: линии и надписи.
/// Точка вставки — левый верхний угол таблицы (0, 0), таблица уходит вправо и вниз.
/// </summary>
internal static class TableRenderer
{
    public static void Draw(SketchedSymbolDefinition definition, TableModel model, TransientGeometry geometry)
    {
        definition.Edit(out DrawingSketch sketch);
        var saved = false;
        try
        {
            Clear(sketch);
            DrawLayout(sketch, model, TableLayout.Build(model), geometry);
            saved = true;
        }
        finally
        {
            definition.ExitEdit(saved);
        }
    }

    private static void Clear(DrawingSketch sketch)
    {
        for (var i = sketch.TextBoxes.Count; i >= 1; i--)
            sketch.TextBoxes[i].Delete();

        for (var i = sketch.SketchLines.Count; i >= 1; i--)
            sketch.SketchLines[i].Delete();

        for (var i = sketch.SketchPoints.Count; i >= 1; i--)
        {
            try
            {
                sketch.SketchPoints[i].Delete();
            }
            catch (Exception)
            {
                // Точка может быть удалена вместе с линией или быть точкой вставки.
            }
        }
    }

    private static void DrawLayout(DrawingSketch sketch, TableModel model, TableLayout layout, TransientGeometry geometry)
    {
        var insertion = sketch.SketchPoints.Add(Point(geometry, 0, 0), false);
        TrySet(() => ((dynamic)insertion).InsertionPoint = true);

        foreach (var segment in layout.Lines)
        {
            var line = sketch.SketchLines.AddByTwoPoints(
                Point(geometry, segment.X1, segment.Y1),
                Point(geometry, segment.X2, segment.Y2));
            var weightCm = Units.MmToCm(segment.WeightMm);
            TrySet(() => ((dynamic)line).LineWeight = weightCm);
        }

        foreach (var cell in layout.Texts)
            DrawCellText(sketch, cell, model.Style, geometry);
    }

    /// <summary>
    /// Выводит текст ячейки. Каждая строка — цепочка надписей (обычный текст и индексы),
    /// поставленных вплотную друг за другом. Ширину каждой надписи измеряет сам Inventor,
    /// после чего строка целиком сдвигается по выравниванию ячейки.
    /// </summary>
    private static void DrawCellText(DrawingSketch sketch, CellText cell, TableStyle style, TransientGeometry geometry)
    {
        foreach (var line in cell.Lines)
        {
            var placed = new List<(Inventor.TextBox Box, double OffsetMm, TextSegment Segment)>();
            var lineWidth = 0.0;
            foreach (var segment in line.Segments)
            {
                var box = AddText(sketch, Point(geometry, 0, segment.BaselineMm), FormatSegment(segment, style), PlainText(segment));
                box.HorizontalJustification = HorizontalTextAlignmentEnum.kAlignTextLeft;
                box.VerticalJustification = VerticalTextAlignmentEnum.kAlignTextLower;
                placed.Add((box, lineWidth, segment));
                lineWidth += MeasureWidthMm(box, segment);
            }

            var left = cell.Alignment switch
            {
                HorizontalTextAlignment.Center => cell.Left + (cell.Width - lineWidth) / 2,
                HorizontalTextAlignment.Right => cell.Left + cell.Width - style.PaddingMm - lineWidth,
                _ => cell.Left + style.PaddingMm,
            };

            foreach (var (box, offset, segment) in placed)
            {
                // Низ надписи лежит чуть ниже базовой линии — на величину нижних выносных элементов.
                var bottom = segment.BaselineMm + DescentRatio * segment.FontSizeMm;
                box.Origin = Point(geometry, left + offset, bottom);
            }
        }
    }

    /// <summary>Доля высоты шрифта, на которую низ надписи Inventor опущен ниже базовой линии.</summary>
    private const double DescentRatio = 0.2;

    /// <summary>
    /// Ширина надписи в мм. Сначала спрашиваем Inventor, при неудаче — грубая оценка по числу символов.
    /// </summary>
    private static double MeasureWidthMm(Inventor.TextBox box, TextSegment segment)
    {
        try
        {
            double width = ((dynamic)box).Width;
            if (width > 0)
                return Units.CmToMm(width);
        }
        catch (Exception)
        {
            // Нет свойства Width — пробуем габарит.
        }

        try
        {
            Box2d range = ((dynamic)box).RangeBox;
            var width = range.MaxPoint.X - range.MinPoint.X;
            if (width > 0)
                return Units.CmToMm(width);
        }
        catch (Exception)
        {
            // Нет и габарита — оценка ниже.
        }

        AddinLog.Write("Не удалось измерить ширину надписи, используется оценка.");
        return PlainText(segment).Length * segment.FontSizeMm * 0.75;
    }

    /// <summary>
    /// Создаёт надпись. Если Inventor не принял форматированный текст, создаёт надпись простым текстом
    /// и применяет форматирование отдельно, чтобы текст в таблице появился в любом случае.
    /// </summary>
    private static Inventor.TextBox AddText(DrawingSketch sketch, Point2d origin, string formatted, string plain)
    {
        try
        {
            return sketch.TextBoxes.AddFitted(origin, formatted);
        }
        catch (Exception ex)
        {
            AddinLog.Write($"AddFitted не принял форматированный текст ({ex.Message}): {formatted}");
        }

        Inventor.TextBox textBox;
        try
        {
            textBox = sketch.TextBoxes.AddFitted(origin, SecurityElement.Escape(plain));
        }
        catch (Exception ex)
        {
            // Последняя попытка: явно передать стиль текста примечаний из активного стандарта.
            AddinLog.Write($"AddFitted не принял и простой текст ({ex.Message}), пробую со стилем текста.");
            var document = (DrawingDocument)((dynamic)sketch.Parent).Parent;
            var textStyle = document.StylesManager.ActiveStandardStyle.ActiveObjectDefaults.GeneralNoteStyle;
            textBox = sketch.TextBoxes.AddFitted(origin, SecurityElement.Escape(plain), textStyle);
        }

        try
        {
            textBox.FormattedText = formatted;
        }
        catch (Exception ex)
        {
            AddinLog.Write($"FormattedText не принят ({ex.Message}), надпись оставлена со стилем по умолчанию.");
        }

        return textBox;
    }

    private static string PlainText(TextSegment segment) => string.Concat(segment.Runs.Select(r => r.Text));

    /// <summary>Форматированный текст Inventor: каждый фрагмент в своём &lt;StyleOverride&gt;.</summary>
    private static string FormatSegment(TextSegment segment, TableStyle style)
    {
        var body = new StringBuilder();
        foreach (var run in segment.Runs)
        {
            var size = Units.MmToCm(TableLayout.RenderedSize(run, style)).ToString(CultureInfo.InvariantCulture);
            body.Append($"<StyleOverride Font='{SecurityElement.Escape(run.FontName ?? style.FontName)}' FontSize='{size}'");
            if (run.Bold)
                body.Append(" Bold='True'");
            if (run.Italic)
                body.Append(" Italic='True'");
            if (run.Underline)
                body.Append(" Underline='True'");
            body.Append('>').Append(SecurityElement.Escape(run.Text)).Append("</StyleOverride>");
        }

        return body.ToString();
    }

    /// <summary>Точка эскиза: мм с осью Y вниз → см Inventor с осью Y вверх.</summary>
    private static Point2d Point(TransientGeometry geometry, double xMm, double yMm) =>
        geometry.CreatePoint2d(Units.MmToCm(xMm), -Units.MmToCm(yMm));

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
