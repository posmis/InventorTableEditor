using System;
using System.Collections.Generic;
using System.Linq;
using Inventor;
using InventorTableEditor.Core;
using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;
using CellText = InventorTableEditor.Core.Layout.CellText;
using HorizontalTextAlignment = InventorTableEditor.Core.Model.HorizontalTextAlignment;
using TextLine = InventorTableEditor.Core.Layout.TextLine;
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
                var plan = PlanLine(line, style, cell.Width - 2 * style.PaddingMm);
                foreach (var item in plan.Runs)
                    StyleFor(styles, item.Run, style, plan.WidthScale);
            }
        }

        return styles;
    }

    private static TextStyle StyleFor(TextStyleCache styles, TextRun run, TableStyle style, double widthScale) =>
        styles.Get(run.FontName ?? style.FontName, TableLayout.RenderedSize(run, style), run.Bold, run.Italic, run.Underline, widthScale);

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

    /// <summary>Фрагмент строки: левый край надписи от начала строки и верх букв от верха таблицы, мм (до сжатия).</summary>
    private sealed record PlannedRun(TextRun Run, double LeftMm, double TopMm);

    /// <summary>Расстановка строки: фрагменты, габарит по горизонтали и коэффициент сжатия по ширине.</summary>
    private sealed record LinePlan(List<PlannedRun> Runs, double LeftMm, double RightMm, double WidthScale)
    {
        public double WidthMm => (RightMm - LeftMm) * WidthScale;
    }

    /// <summary>
    /// Расставляет фрагменты строки по метрикам шрифта. Фрагменты идут по ширинам букв, как в Word,
    /// а на стыке наклонного текста с прямым и перед верхним индексом добавляется поправка на наклон
    /// (правила — docs/typography.md). Если строка шире ячейки (длинное слово не переносится),
    /// она сжимается по ширине: высота букв не меняется.
    /// </summary>
    private static LinePlan PlanLine(TextLine line, TableStyle style, double availableMm)
    {
        var runs = new List<PlannedRun>();
        double pen = 0, left = double.PositiveInfinity, right = double.NegativeInfinity;
        TextRun? previous = null;
        var previousOverhang = 0.0;

        foreach (var segment in line.Segments)
        {
            foreach (var run in segment.Runs)
            {
                var extents = Measure(run, style);
                if (previous != null && NeedsItalicCorrection(previous, run))
                    pen += previousOverhang;

                previous = run;
                previousOverhang = extents.OverhangMm;
                if (!string.IsNullOrWhiteSpace(run.Text))
                {
                    runs.Add(new PlannedRun(run, pen + extents.BoxLeftMm, segment.BaselineMm - extents.TopMm));
                    left = Math.Min(left, pen + extents.BoxLeftMm);
                    right = Math.Max(right, pen + extents.AdvanceMm + extents.OverhangMm);
                }

                pen += extents.AdvanceMm;
            }
        }

        if (runs.Count == 0)
            return new LinePlan(runs, 0, 0, 1);

        var width = right - left;
        var scale = width > availableMm && availableMm > 0
            ? Math.Max(0.1, Math.Floor(availableMm / width * 100) / 100)
            : 1.0;
        return new LinePlan(runs, left, right, scale);
    }

    /// <summary>Размеры фрагмента по метрикам шрифта; если шрифт не найден в Windows — грубая оценка.</summary>
    private static TextExtents Measure(TextRun run, TableStyle style)
    {
        var size = TableLayout.RenderedSize(run, style);
        var metrics = FontMetrics.Get(run.FontName ?? style.FontName, run.Bold, run.Italic);
        return metrics?.Measure(run.Text, size, run.Italic) ?? new TextExtents(size, 0, run.Text.Length * size * 0.6, 0);
    }

    /// <summary>
    /// Выводит текст ячейки. Каждая строка — цепочка надписей (по одной на фрагмент с единым
    /// форматированием), расставленных <see cref="PlanLine"/>. Надпись Inventor — прямоугольник
    /// от левого края букв до конца текста, по высоте — по очертаниям букв; её Origin — левый верхний угол.
    /// Строка целиком сдвигается по выравниванию ячейки.
    /// </summary>
    private static void DrawCellText(DrawingSketch sketch, CellText cell, TableStyle style, TextStyleCache styles, TransientGeometry geometry)
    {
        foreach (var line in cell.Lines)
        {
            var plan = PlanLine(line, style, cell.Width - 2 * style.PaddingMm);
            if (plan.Runs.Count == 0)
                continue;

            var left = cell.Alignment switch
            {
                HorizontalTextAlignment.Center => cell.Left + (cell.Width - plan.WidthMm) / 2,
                HorizontalTextAlignment.Right => cell.Left + cell.Width - style.PaddingMm - plan.WidthMm,
                _ => cell.Left + style.PaddingMm,
            };

            foreach (var item in plan.Runs)
            {
                var origin = Point(geometry, left + (item.LeftMm - plan.LeftMm) * plan.WidthScale, item.TopMm);
                var box = sketch.TextBoxes.AddFitted(origin, EscapeText(item.Run.Text), StyleFor(styles, item.Run, style, plan.WidthScale));
                box.HorizontalJustification = HorizontalTextAlignmentEnum.kAlignTextLeft;
                box.VerticalJustification = VerticalTextAlignmentEnum.kAlignTextUpper;
                // После смены выравнивания точка привязки задаётся заново.
                box.Origin = origin;
            }
        }
    }

    /// <summary>
    /// Поправка на наклон (как italic correction в TeX): после наклонного фрагмента верх букв выступает
    /// вправо и налезает на следующий. Нужна перед прямым текстом и перед верхним индексом;
    /// перед нижним индексом не нужна — он стоит ниже выступа.
    /// </summary>
    private static bool NeedsItalicCorrection(TextRun previous, TextRun next) =>
        previous.Italic
        && next.Script != ScriptPosition.Subscript
        && (!next.Italic || next.Script != previous.Script);

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
