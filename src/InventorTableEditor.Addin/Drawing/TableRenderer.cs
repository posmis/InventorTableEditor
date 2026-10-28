using System;
using System.Collections.Generic;
using System.Linq;
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
                foreach (var segment in line.Segments)
                {
                    foreach (var run in segment.Runs)
                        StyleFor(styles, run, style);
                }
            }
        }

        return styles;
    }

    private static TextStyle StyleFor(TextStyleCache styles, TextRun run, TableStyle style) =>
        styles.Get(run.FontName ?? style.FontName, TableLayout.RenderedSize(run, style), run.Bold, run.Italic, run.Underline);

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
    /// форматированием). Надпись Inventor — это прямоугольник по очертаниям букв, его Origin —
    /// левый верхний угол. Поэтому по метрикам шрифта считаем, насколько верх букв выше базовой
    /// линии и какие пустые поля у букв по краям, а ширину самих букв берём у Inventor.
    /// После расстановки строка целиком сдвигается по выравниванию ячейки.
    /// </summary>
    private static void DrawCellText(DrawingSketch sketch, CellText cell, TableStyle style, TextStyleCache styles, TransientGeometry geometry)
    {
        foreach (var line in cell.Lines)
        {
            // Надпись, левый край букв от начала строки и верх букв от верха таблицы, мм.
            var placed = new List<(Inventor.TextBox Box, double InkLeftMm, double TopMm)>();
            double pen = 0, inkStart = double.NaN, inkEnd = 0;

            foreach (var segment in line.Segments)
            {
                foreach (var run in segment.Runs)
                {
                    var size = TableLayout.RenderedSize(run, style);
                    var extents = FontMetrics.Get(run.FontName ?? style.FontName, run.Bold, run.Italic)?.Measure(run.Text, size);
                    if (string.IsNullOrWhiteSpace(run.Text))
                    {
                        pen += extents?.AdvanceMm ?? run.Text.Length * size * 0.4;
                        continue;
                    }

                    var textStyle = StyleFor(styles, run, style);
                    var box = sketch.TextBoxes.AddFitted(Point(geometry, 0, segment.BaselineMm), EscapeText(run.Text), textStyle);
                    box.HorizontalJustification = HorizontalTextAlignmentEnum.kAlignTextLeft;
                    box.VerticalJustification = VerticalTextAlignmentEnum.kAlignTextUpper;

                    var inkWidth = MeasureWidthMm(box, run.Text, size) * styles.WidthScale(textStyle);
                    var inkLeft = pen + (extents?.LeftBearingMm ?? 0);
                    var top = extents is { } known
                        ? segment.BaselineMm - known.TopMm
                        : segment.BaselineMm - MeasureHeightMm(box, size);
                    placed.Add((box, inkLeft, top));

                    if (double.IsNaN(inkStart))
                        inkStart = inkLeft;
                    inkEnd = inkLeft + inkWidth;
                    pen = inkEnd + (extents?.RightBearingMm ?? size * 0.1);
                }
            }

            if (placed.Count == 0)
                continue;

            var lineWidth = inkEnd - inkStart;
            var left = cell.Alignment switch
            {
                HorizontalTextAlignment.Center => cell.Left + (cell.Width - lineWidth) / 2,
                HorizontalTextAlignment.Right => cell.Left + cell.Width - style.PaddingMm - lineWidth,
                _ => cell.Left + style.PaddingMm,
            };

            foreach (var (box, inkLeft, top) in placed)
                box.Origin = Point(geometry, left + inkLeft - inkStart, top);
        }
    }

    /// <summary>Текст надписи Inventor разбирается как разметка: экранируются только &amp;, &lt; и &gt;.</summary>
    private static string EscapeText(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>Высота надписи в мм; при неудаче — оценка по размеру шрифта.</summary>
    private static double MeasureHeightMm(Inventor.TextBox box, double sizeMm)
    {
        try
        {
            double height = ((dynamic)box).Height;
            if (height > 0)
                return Units.CmToMm(height);
        }
        catch (Exception)
        {
            // Нет свойства Height — пробуем габарит.
        }

        try
        {
            Box2d range = ((dynamic)box).RangeBox;
            var height = range.MaxPoint.Y - range.MinPoint.Y;
            if (height > 0)
                return Units.CmToMm(height);
        }
        catch (Exception)
        {
            // Нет и габарита — оценка ниже.
        }

        AddinLog.Write("Не удалось измерить высоту надписи, используется оценка.");
        return sizeMm;
    }

    /// <summary>
    /// Ширина надписи в мм. Сначала спрашиваем Inventor, при неудаче — грубая оценка по числу символов.
    /// </summary>
    private static double MeasureWidthMm(Inventor.TextBox box, string text, double sizeMm)
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
        return text.Length * sizeMm * 0.75;
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
