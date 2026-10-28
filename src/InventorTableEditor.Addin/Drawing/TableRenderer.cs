using System;
using System.Collections.Generic;
using System.Linq;
using Inventor;
using InventorTableEditor.Core;
using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;
using CellText = InventorTableEditor.Core.Layout.CellText;
using TableStyle = InventorTableEditor.Core.Model.TableStyle;

namespace InventorTableEditor.Addin.Drawing;

/// <summary>
/// Строит таблицу в эскизе определения символа: линии и надписи.
/// Точка вставки — левый верхний угол таблицы (0, 0), таблица уходит вправо и вниз.
/// </summary>
internal static class TableRenderer
{
    /// <summary>Идёт перестроение таблицы: изменения чертежа вызваны самим аддином.</summary>
    public static bool IsDrawing { get; private set; }

    public static void Draw(DrawingDocument document, SketchedSymbolDefinition definition, TableModel model, TransientGeometry geometry)
    {
        var layout = TableLayout.Build(model, new FontMetricsMeasurer(model.Style));
        var styles = PrepareStyles(document, layout, model.Style);

        IsDrawing = true;
        definition.Edit(out DrawingSketch sketch);
        var saved = false;
        try
        {
            Clear(sketch);
            DrawLayout(sketch, model, layout, styles, geometry);
            saved = true;
        }
        finally
        {
            definition.ExitEdit(saved);
            IsDrawing = false;
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

    /// <summary>Стили текста создаются до входа в эскиз символа, чтобы не менять стили во время его редактирования.</summary>
    private static TextStyleCache PrepareStyles(DrawingDocument document, TableLayout layout, TableStyle style)
    {
        var styles = new TextStyleCache(document);
        foreach (var cell in layout.Texts)
        {
            foreach (var line in cell.Lines)
            {
                var plan = TextPlanner.PlanLine(line, style, cell);
                foreach (var item in plan.Runs)
                    StyleFor(styles, item.Run, style, plan.WidthScale);
            }
        }

        return styles;
    }

    private static TextStyle StyleFor(TextStyleCache styles, TextRun run, TableStyle style, double widthScale) =>
        styles.Get(run.FontName ?? style.FontName, TableLayout.RenderedSize(run, style), run.Bold, run.Italic, underline: false, widthScale);

    private static void DrawLayout(DrawingSketch sketch, TableModel model, TableLayout layout, TextStyleCache styles, TransientGeometry geometry)
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
            DrawCellText(sketch, cell, model.Style, styles, geometry);
    }

    /// <summary>
    /// Выводит текст ячейки. Каждая строка — цепочка надписей (по одной на фрагмент с единым
    /// форматированием), расставленных <see cref="TextPlanner.PlanLine"/>. Надпись Inventor — прямоугольник
    /// от левого края букв до конца текста, по высоте — по очертаниям букв; её Origin — левый верхний угол.
    /// Строка целиком сдвигается по выравниванию ячейки.
    /// </summary>
    private static void DrawCellText(DrawingSketch sketch, CellText cell, TableStyle style, TextStyleCache styles, TransientGeometry geometry)
    {
        foreach (var line in cell.Lines)
        {
            var plan = TextPlanner.PlanLine(line, style, cell);
            foreach (var item in plan.Runs)
            {
                var origin = Point(geometry, plan.OriginMm + item.BoxLeftMm * plan.WidthScale, item.TopMm);
                var box = sketch.TextBoxes.AddFitted(origin, EscapeText(item.Run.Text), StyleFor(styles, item.Run, style, plan.WidthScale));
                box.HorizontalJustification = HorizontalTextAlignmentEnum.kAlignTextLeft;
                box.VerticalJustification = VerticalTextAlignmentEnum.kAlignTextUpper;
                // После смены выравнивания точка привязки задаётся заново.
                box.Origin = origin;
            }

            foreach (var underline in TextPlanner.Underlines(plan, style))
            {
                var stroke = sketch.SketchLines.AddByTwoPoints(
                    Point(geometry, underline.X1Mm, underline.YMm),
                    Point(geometry, underline.X2Mm, underline.YMm));
                var weightCm = Units.MmToCm(style.UnderlineMm);
                TrySet(() => ((dynamic)stroke).LineWeight = weightCm);
            }
        }
    }

    /// <summary>Текст надписи Inventor разбирается как разметка: экранируются только &amp;, &lt; и &gt;.</summary>
    private static string EscapeText(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

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
