using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Layout;

/// <summary>Измеритель ширины текста для переноса по словам.</summary>
public interface ITextMeasurer
{
    /// <summary>Ширина текста фрагмента с полями букв (как он займёт место в строке), мм.</summary>
    double AdvanceWidthMm(TextRun run, string text, double sizeMm);
}

/// <summary>
/// Перенос текста ячейки по словам в пределах ширины. Принудительные переносы ('\n') сохраняются.
/// Слово шире ячейки остаётся на отдельной строке и выходит за её границу.
/// </summary>
public static class TextWrapper
{
    public static List<List<TextRun>> Wrap(TableCell cell, TableStyle style, double availableMm, ITextMeasurer? measurer)
    {
        var result = new List<List<TextRun>>();
        foreach (var line in cell.SplitLines())
        {
            if (measurer == null || availableMm <= 0)
                result.Add(line);
            else
                result.AddRange(WrapLine(line, style, availableMm, measurer));
        }

        return result;
    }

    private static List<List<TextRun>> WrapLine(List<TextRun> line, TableStyle style, double availableMm, ITextMeasurer measurer)
    {
        var lines = new List<List<TextRun>>();
        var current = new List<TextRun>();
        var width = 0.0;
        foreach (var word in Words(line))
        {
            // Пробелы в конце слова не мешают ему поместиться: в конце строки они отбрасываются.
            var visible = Width(TrimEnd(word), style, measurer);
            if (current.Count > 0 && width + visible > availableMm + 1e-6)
            {
                lines.Add(Finish(current));
                current = new List<TextRun>();
                width = 0;
            }

            current.AddRange(word);
            width += Width(word, style, measurer);
        }

        lines.Add(Finish(current));
        return lines;
    }

    /// <summary>Слова: фрагменты до пробела включительно; слово может состоять из нескольких фрагментов (D и индекс 2).</summary>
    private static List<List<TextRun>> Words(List<TextRun> line)
    {
        var words = new List<List<TextRun>>();
        var current = new List<TextRun>();
        foreach (var run in line)
        {
            var start = 0;
            for (var i = 0; i < run.Text.Length; i++)
            {
                var endOfSpaces = run.Text[i] == ' ' && (i + 1 == run.Text.Length || run.Text[i + 1] != ' ');
                if (!endOfSpaces)
                    continue;

                current.Add(run.WithText(run.Text[start..(i + 1)]));
                words.Add(current);
                current = new List<TextRun>();
                start = i + 1;
            }

            if (start < run.Text.Length)
                current.Add(run.WithText(run.Text[start..]));
        }

        if (current.Count > 0)
            words.Add(current);
        return words;
    }

    private static double Width(List<TextRun> runs, TableStyle style, ITextMeasurer measurer) =>
        runs.Sum(r => measurer.AdvanceWidthMm(r, r.Text, TableLayout.RenderedSize(r, style)));

    /// <summary>Строка без пробелов в конце и с соседними фрагментами одного формата, склеенными обратно.</summary>
    private static List<TextRun> Finish(List<TextRun> runs)
    {
        var merged = new List<TextRun>();
        foreach (var run in TrimEnd(runs))
        {
            if (merged.Count > 0 && merged[^1].SameFormat(run))
                merged[^1] = merged[^1].WithText(merged[^1].Text + run.Text);
            else
                merged.Add(run);
        }

        return merged;
    }

    private static List<TextRun> TrimEnd(List<TextRun> runs)
    {
        var result = runs.Select(r => r.Clone()).ToList();
        while (result.Count > 0)
        {
            var last = result[^1];
            last.Text = last.Text.TrimEnd(' ');
            if (last.Text.Length > 0)
                break;
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }
}
