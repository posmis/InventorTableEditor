using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Addin.UI;

/// <summary>
/// Преобразование ячейки таблицы в документ WPF для редактирования и обратно.
/// Индексы в редакторе — это BaselineAlignment и уменьшенный размер шрифта.
/// </summary>
internal static class RichTextConverter
{
    /// <summary>Отношение размера шрифта WPF (em) к высоте шрифта на чертеже.</summary>
    public const double EmPerTextHeight = 1.4;

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

    public static TableCell FromDocument(FlowDocument document, TableStyle style)
    {
        var cell = new TableCell();
        var first = true;
        foreach (var block in document.Blocks)
        {
            if (!first)
                AppendLineBreak(cell, style);
            first = false;

            if (block is Paragraph paragraph)
                ReadInlines(paragraph.Inlines, cell, style);
        }

        cell.Normalize();
        return cell;
    }

    public static FontFamily FontFor(string fontName) => new(fontName + ", ISOCPEUR, GOST type A, Arial");

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

    private static void ReadInlines(InlineCollection inlines, TableCell cell, TableStyle style)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    cell.Runs.Add(new TextRun(run.Text)
                    {
                        Bold = run.FontWeight.ToOpenTypeWeight() >= FontWeights.SemiBold.ToOpenTypeWeight(),
                        Italic = run.FontStyle != FontStyles.Normal,
                        Underline = HasUnderline(run),
                        Script = ScriptOf(run),
                    });
                    break;
                case LineBreak:
                    AppendLineBreak(cell, style);
                    break;
                case Span span:
                    ReadInlines(span.Inlines, cell, style);
                    break;
            }
        }
    }

    /// <summary>Перенос строки берёт форматирование предыдущего фрагмента, чтобы не дробить фрагменты.</summary>
    private static void AppendLineBreak(TableCell cell, TableStyle style)
    {
        var format = cell.Runs.Count > 0 ? cell.Runs[^1] : new TextRun(string.Empty, style.Italic);
        cell.Runs.Add(format.WithText("\n"));
    }

    private static ScriptPosition ScriptOf(Inline inline)
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
