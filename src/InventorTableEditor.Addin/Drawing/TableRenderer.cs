using System;
using System.Security;
using System.Text;
using Inventor;
using InventorTableEditor.Core;
using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;
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

        var style = model.Style;
        foreach (var cell in layout.Texts)
        {
            var x = cell.Alignment switch
            {
                TextAlignment.Center => cell.Left + cell.Width / 2,
                TextAlignment.Right => cell.Left + cell.Width - style.PaddingMm,
                _ => cell.Left + style.PaddingMm,
            };
            var origin = Point(geometry, x, cell.Top + cell.Height / 2);

            var textBox = AddText(sketch, origin, cell.Text, style);
            textBox.HorizontalJustification = cell.Alignment switch
            {
                TextAlignment.Center => HorizontalTextAlignmentEnum.kAlignTextCenter,
                TextAlignment.Right => HorizontalTextAlignmentEnum.kAlignTextRight,
                _ => HorizontalTextAlignmentEnum.kAlignTextLeft,
            };
            textBox.VerticalJustification = VerticalTextAlignmentEnum.kAlignTextMiddle;
            // После смены выравнивания точка привязки задаётся заново.
            textBox.Origin = origin;
        }
    }

    /// <summary>
    /// Создаёт надпись. Если Inventor не принял форматированный текст, создаёт надпись простым текстом
    /// и применяет форматирование отдельно, чтобы текст в таблице появился в любом случае.
    /// </summary>
    private static Inventor.TextBox AddText(DrawingSketch sketch, Point2d origin, string text, TableStyle style)
    {
        var formatted = FormatText(text, style);
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
            textBox = sketch.TextBoxes.AddFitted(origin, SecurityElement.Escape(text.Replace('\n', ' ')));
        }
        catch (Exception ex)
        {
            // Последняя попытка: явно передать стиль текста примечаний из активного стандарта.
            AddinLog.Write($"AddFitted не принял и простой текст ({ex.Message}), пробую со стилем текста.");
            var document = (DrawingDocument)((dynamic)sketch.Parent).Parent;
            var textStyle = document.StylesManager.ActiveStandardStyle.ActiveObjectDefaults.GeneralNoteStyle;
            textBox = sketch.TextBoxes.AddFitted(origin, SecurityElement.Escape(text.Replace('\n', ' ')), textStyle);
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

    /// <summary>Форматированный текст Inventor: шрифт и стиль, строки через &lt;Br/&gt;.</summary>
    private static string FormatText(string text, TableStyle style)
    {
        var body = new StringBuilder();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                body.Append("<Br/>");
            body.Append(SecurityElement.Escape(lines[i]));
        }

        var fontSizeCm = Units.MmToCm(style.FontSizeMm).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var italic = style.Italic ? " Italic='True'" : string.Empty;
        return $"<StyleOverride Font='{SecurityElement.Escape(style.FontName)}' FontSize='{fontSizeCm}'{italic}>{body}</StyleOverride>";
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
