using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Layout;

/// <summary>
/// Перенос текста ячейки по словам в пределах ширины. Принудительные переносы ('\n') сохраняются.
/// Слово шире ячейки остаётся на отдельной строке (в чертеже оно сжимается по ширине).
/// </summary>
public static class TextWrapper
{
    /// <summary>
    /// Строки после переноса как диапазоны символов текста ячейки (<see cref="TableCell.PlainText"/>):
    /// Start — первый символ, End — за последним; '\n' в диапазон не входит, пробелы в конце строки входят.
    /// </summary>
    public static List<(int Start, int End)> WrapRanges(TableCell cell, TableStyle style, double availableMm, ITextMetrics? metrics)
    {
        var chars = Flatten(cell);
        var result = new List<(int Start, int End)>();
        var lineStart = 0;
        for (var i = 0; i <= chars.Count; i++)
        {
            if (i < chars.Count && chars[i].Ch != '\n')
                continue;

            if (metrics == null || availableMm <= 0)
                result.Add((lineStart, i));
            else
                result.AddRange(WrapLine(chars, lineStart, i, style, availableMm, metrics));
            lineStart = i + 1;
        }

        return result;
    }

    /// <summary>Строки после переноса как фрагменты: без пробелов в конце, соседние фрагменты одного формата склеены.</summary>
    public static List<List<TextRun>> Wrap(TableCell cell, TableStyle style, double availableMm, ITextMetrics? metrics)
    {
        var chars = Flatten(cell);
        return WrapRanges(cell, style, availableMm, metrics)
            .Select(range => ToRuns(chars, range.Start, TrimmedEnd(chars, range.Start, range.End)))
            .ToList();
    }

    /// <summary>Конец диапазона без пробелов в конце строки.</summary>
    public static int TrimmedEnd(TableCell cell, int start, int end) => TrimmedEnd(Flatten(cell), start, end);

    private static IEnumerable<(int Start, int End)> WrapLine(List<(char Ch, TextRun Format)> chars, int start, int end,
        TableStyle style, double availableMm, ITextMetrics metrics)
    {
        var lineStart = start;
        var width = 0.0;
        foreach (var (wordStart, wordEnd) in Words(chars, start, end))
        {
            // Пробелы в конце слова не мешают ему поместиться: в конце строки они отбрасываются.
            var visible = Width(chars, wordStart, TrimmedEnd(chars, wordStart, wordEnd), style, metrics);
            if (wordStart > lineStart && width + visible > availableMm + 1e-6)
            {
                yield return (lineStart, wordStart);
                lineStart = wordStart;
                width = 0;
            }

            width += Width(chars, wordStart, wordEnd, style, metrics);
        }

        yield return (lineStart, end);
    }

    /// <summary>Слова: символы до пробела включительно; слово может состоять из нескольких фрагментов (D и индекс 2).</summary>
    private static IEnumerable<(int Start, int End)> Words(List<(char Ch, TextRun Format)> chars, int start, int end)
    {
        var wordStart = start;
        for (var i = start; i < end; i++)
        {
            var endOfSpaces = chars[i].Ch == ' ' && (i + 1 == end || chars[i + 1].Ch != ' ');
            if (!endOfSpaces)
                continue;

            yield return (wordStart, i + 1);
            wordStart = i + 1;
        }

        if (wordStart < end)
            yield return (wordStart, end);
    }

    private static double Width(List<(char Ch, TextRun Format)> chars, int start, int end, TableStyle style, ITextMetrics metrics) =>
        ToRuns(chars, start, end).Sum(r => metrics.Measure(r, r.Text, TableLayout.RenderedSize(r, style)).AdvanceMm);

    private static int TrimmedEnd(List<(char Ch, TextRun Format)> chars, int start, int end)
    {
        while (end > start && chars[end - 1].Ch == ' ')
            end--;
        return end;
    }

    /// <summary>Фрагменты для символов [start, end): соседние символы одного формата — один фрагмент.</summary>
    private static List<TextRun> ToRuns(List<(char Ch, TextRun Format)> chars, int start, int end)
    {
        var runs = new List<TextRun>();
        for (var i = start; i < end; i++)
        {
            var (ch, format) = chars[i];
            if (runs.Count > 0 && runs[^1].SameFormat(format))
                runs[^1].Text += ch;
            else
                runs.Add(format.WithText(ch.ToString()));
        }

        return runs;
    }

    private static List<(char Ch, TextRun Format)> Flatten(TableCell cell) =>
        cell.Runs.SelectMany(run => run.Text.Select(ch => (ch, run))).ToList();
}
