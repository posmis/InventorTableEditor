using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using InventorTableEditor.Core.Model;
using TableCell = InventorTableEditor.Core.Model.TableCell;

namespace InventorTableEditor.Addin.UI;

/// <summary>
/// Преобразование ячейки таблицы в документ WPF для редактирования и обратно.
/// Индексы в редакторе — это BaselineAlignment и уменьшенный размер шрифта.
/// </summary>
internal static class RichTextConverter
{
    /// <summary>
    /// Отношение размера шрифта WPF (em) к высоте шрифта на чертеже (высоте заглавной буквы).
    /// Задаётся по метрикам шрифта таблицы, чтобы переносы в редакторе совпадали с чертежом.
    /// </summary>
    public static double EmPerTextHeight { get; set; } = 1.4;

    public static double BaseFontSizePx(TableStyle style, double pxPerMm) => style.FontSizeMm * pxPerMm * EmPerTextHeight;

    public static FlowDocument ToDocument(TableCell cell, TableStyle style, double pxPerMm, TextAlignment alignment)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0), TextAlignment = alignment };
        foreach (var run in cell.Runs)
        {
            var parts = run.Text.Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                    paragraph.Inlines.Add(new LineBreak());
                if (parts[i].Length > 0)
                    paragraph.Inlines.Add(CreateRun(parts[i], run, style, pxPerMm));
            }
        }

        return new FlowDocument(paragraph)
        {
            PagePadding = new Thickness(0),
            FontFamily = FontFor(style.FontName),
            FontSize = BaseFontSizePx(style, pxPerMm),
            FontStyle = style.Italic ? FontStyles.Italic : FontStyles.Normal,
            TextAlignment = alignment,
        };
    }

    /// <summary>
    /// Читает ячейку из документа редактора. Выравнивание берётся из <paramref name="previous"/>:
    /// оно задаётся кнопками, а не текстом.
    /// </summary>
    public static TableCell FromDocument(FlowDocument document, TableStyle style, double pxPerMm, TableCell previous)
    {
        var cell = new TableCell
        {
            HorizontalAlignment = previous.HorizontalAlignment,
            VerticalAlignment = previous.VerticalAlignment,
        };
        var first = true;
        foreach (var block in document.Blocks)
        {
            if (!first)
                AppendLineBreak(cell, style);
            first = false;

            if (block is Paragraph paragraph)
                ReadInlines(paragraph.Inlines, cell, style, pxPerMm);
        }

        cell.Normalize();
        return cell;
    }

    public static FontFamily FontFor(string fontName) => new(fontName + ", ISOCPEUR, GOST type A, Arial");

    /// <summary>Основной шрифт из семейства вида «Имя, запасной1, запасной2».</summary>
    public static string FontNameOf(FontFamily family) => family.Source.Split(',')[0].Trim();

    /// <summary>Высота основного текста в мм по размеру шрифта WPF (для индекса — до уменьшения), с точностью 0,1 мм.</summary>
    public static double TextHeightMm(double fontSizePx, ScriptPosition script, TableStyle style, double pxPerMm)
    {
        var size = fontSizePx / (pxPerMm * EmPerTextHeight);
        if (script != ScriptPosition.Normal)
            size /= style.ScriptScale;
        return System.Math.Round(size, 1);
    }

    private static Run CreateRun(string text, TextRun source, TableStyle style, double pxPerMm)
    {
        var size = (source.FontSizeMm ?? style.FontSizeMm) * pxPerMm * EmPerTextHeight;
        var run = new Run(text)
        {
            FontWeight = source.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = source.Italic ? FontStyles.Italic : FontStyles.Normal,
            FontSize = source.Script == ScriptPosition.Normal ? size : size * style.ScriptScale,
            BaselineAlignment = source.Script switch
            {
                ScriptPosition.Superscript => BaselineAlignment.Superscript,
                ScriptPosition.Subscript => BaselineAlignment.Subscript,
                _ => BaselineAlignment.Baseline,
            },
        };

        if (source.Underline)
            run.TextDecorations = TextDecorations.Underline;
        if (source.FontName != null)
            run.FontFamily = FontFor(source.FontName);
        return run;
    }

    private static void ReadInlines(InlineCollection inlines, TableCell cell, TableStyle style, double pxPerMm)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    var script = ScriptOf(run);
                    var fontName = FontNameOf(run.FontFamily);
                    var sizeMm = TextHeightMm(run.FontSize, script, style, pxPerMm);
                    cell.Runs.Add(new TextRun(run.Text)
                    {
                        Bold = run.FontWeight.ToOpenTypeWeight() >= FontWeights.SemiBold.ToOpenTypeWeight(),
                        Italic = run.FontStyle != FontStyles.Normal,
                        Underline = HasUnderline(run),
                        Script = script,
                        FontName = string.Equals(fontName, style.FontName, System.StringComparison.OrdinalIgnoreCase) ? null : fontName,
                        FontSizeMm = System.Math.Abs(sizeMm - style.FontSizeMm) < 0.05 ? null : sizeMm,
                    });
                    break;
                case LineBreak:
                    AppendLineBreak(cell, style);
                    break;
                case Span span:
                    ReadInlines(span.Inlines, cell, style, pxPerMm);
                    break;
            }
        }
    }

    /// <summary>Перенос строки берёт форматирование предыдущего фрагмента, чтобы не дробить фрагменты.</summary>
    /// <summary>Все фрагменты текста документа по порядку.</summary>
    public static IEnumerable<Run> EnumerateRuns(FlowDocument document)
    {
        foreach (var block in document.Blocks)
        {
            if (block is Paragraph paragraph)
            {
                foreach (var run in EnumerateRuns(paragraph.Inlines))
                    yield return run;
            }
        }
    }

    private static IEnumerable<Run> EnumerateRuns(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run run)
                yield return run;
            else if (inline is Span span)
            {
                foreach (var inner in EnumerateRuns(span.Inlines))
                    yield return inner;
            }
        }
    }

    private static void AppendLineBreak(TableCell cell, TableStyle style)
    {
        var format = cell.Runs.Count > 0 ? cell.Runs[^1] : new TextRun(string.Empty, style.Italic);
        cell.Runs.Add(format.WithText("\n"));
    }

    public static ScriptPosition ScriptOf(Inline inline)
    {
        for (DependencyObject? current = inline; current is Inline element; current = element.Parent)
        {
            switch (element.BaselineAlignment)
            {
                case BaselineAlignment.Superscript:
                case BaselineAlignment.Top:
                case BaselineAlignment.TextTop:
                    return ScriptPosition.Superscript;
                case BaselineAlignment.Subscript:
                case BaselineAlignment.Bottom:
                case BaselineAlignment.TextBottom:
                    return ScriptPosition.Subscript;
            }
        }

        return ScriptPosition.Normal;
    }

    private static bool HasUnderline(Inline inline)
    {
        for (DependencyObject? current = inline; current is Inline element; current = element.Parent)
        {
            if (element.TextDecorations != null && element.TextDecorations.Any(d => d.Location == TextDecorationLocation.Underline))
                return true;
        }

        return false;
    }
}
