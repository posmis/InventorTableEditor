using System;
using System.Collections.Generic;
using Inventor;
using InventorTableEditor.Core;
using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;
using CellText = InventorTableEditor.Core.Layout.CellText;
using TableStyle = InventorTableEditor.Core.Model.TableStyle;
using TextRun = InventorTableEditor.Core.Model.TextRun;

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
        var metrics = new WpfTextMetrics(model.Style);
        var layout = TableLayout.Build(model, metrics);
        var styles = PrepareStyles(document, layout, model.Style, metrics);

        IsDrawing = true;
        definition.Edit(out DrawingSketch sketch);
        var saved = false;
        try
        {
            Clear(sketch);
            var canvas = new SketchCanvas(sketch, geometry);
            DrawGrid(canvas, layout);
            foreach (var cell in layout.Texts)
                DrawCellText(canvas, cell, model.Style, styles, metrics);
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
    private static TextStyleCache PrepareStyles(DrawingDocument document, TableLayout layout, TableStyle style, ITextMetrics metrics)
    {
        var styles = new TextStyleCache(document);
        foreach (var cell in layout.Texts)
        {
            foreach (var line in cell.Lines)
            {
                var plan = LinePlanner.PlanLine(line, style, cell, metrics);
                foreach (var item in plan.Runs)
                    StyleFor(styles, item.Run, style, plan.WidthScale);
            }
        }

        return styles;
    }

    private static TextStyle StyleFor(TextStyleCache styles, TextRun run, TableStyle style, double widthScale) =>
        styles.Get(run.FontName ?? style.FontName, TableLayout.RenderedSize(run, style), run.Bold, run.Italic, underline: false, widthScale);

    private static void DrawGrid(SketchCanvas canvas, TableLayout layout)
    {
        canvas.AddInsertionPoint();
        foreach (var segment in layout.Lines)
            canvas.AddLine(segment.X1, segment.Y1, segment.X2, segment.Y2, segment.WeightMm);
    }

    /// <summary>
    /// Выводит текст ячейки: каждая строка — цепочка надписей (по одной на фрагмент с единым
    /// форматированием), расставленных <see cref="LinePlanner.PlanLine"/>. Жирный текст Inventor рисует
    /// немного уже, чем по метрикам шрифта: ширина жирных надписей измеряется у Inventor, и строка
    /// переставляется с этой поправкой. Подчёркивание — линиями эскиза.
    /// </summary>
    private static void DrawCellText(SketchCanvas canvas, CellText cell, TableStyle style, TextStyleCache styles, ITextMetrics metrics)
    {
        foreach (var line in cell.Lines)
        {
            var plan = LinePlanner.PlanLine(line, style, cell, metrics);
            var boxes = new List<(PlannedRun Item, Inventor.TextBox Box)>();
            var deltas = new Dictionary<int, double>();
            foreach (var item in plan.Runs)
            {
                var box = canvas.AddText(item.Run.Text, StyleFor(styles, item.Run, style, plan.WidthScale));
                boxes.Add((item, box));
                if (item.Run.Bold && canvas.TryMeasureWidthMm(box) is { } actual)
                {
                    var expected = metrics.Measure(item.Run, item.Run.Text, item.SizeMm).BoxWidthMm;
                    deltas[item.PenIndex] = actual - expected;
                }
            }

            // Inventor сообщает ширину без сжатия стиля, как и метрики: поправка — до сжатия.
            var corrected = LinePlanner.WithWidthCorrections(plan, deltas, cell);
            for (var i = 0; i < boxes.Count; i++)
            {
                var item = corrected.Runs[i];
                canvas.Place(boxes[i].Box, corrected.ToTableX(item.BoxLeftMm), item.TopMm);
            }

            foreach (var underline in LinePlanner.Underlines(corrected, metrics))
                canvas.AddLine(underline.X1Mm, underline.YMm, underline.X2Mm, underline.YMm, style.UnderlineMm);
        }
    }
}
