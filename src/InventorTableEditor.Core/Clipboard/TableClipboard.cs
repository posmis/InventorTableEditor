using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Clipboard;

/// <summary>
/// Данные буфера обмена для обмена с Word и Excel: текст с табуляциями (как копирует Excel)
/// и HTML-таблица (как копирует Word) — с жирным, курсивом, подчёркиванием, индексами и объединениями.
/// Без зависимостей от WPF: сам буфер обмена — в аддине.
/// </summary>
public static class TableClipboard
{
    // ---------- Копирование ----------

    /// <summary>Ячейки прямоугольника как текст Excel: столбцы через Tab, строки через CRLF.</summary>
    public static string ToTsv(TableModel model, CellRange range)
    {
        var text = new StringBuilder();
        for (var r = range.Top; r <= range.Bottom; r++)
        {
            if (r > range.Top)
                text.Append("\r\n");
            for (var c = range.Left; c <= range.Right; c++)
            {
                if (c > range.Left)
                    text.Append('\t');
                if (!model.IsCovered(r, c))
                    text.Append(QuoteTsv(model.GetText(r, c)));
            }
        }

        return text.ToString();
    }

    /// <summary>Ячейки прямоугольника как HTML-таблица (фрагмент, без заголовка CF_HTML).</summary>
    public static string ToHtmlTable(TableModel model, CellRange range)
    {
        var html = new StringBuilder("<table border=\"1\" style=\"border-collapse:collapse\">");
        for (var r = range.Top; r <= range.Bottom; r++)
        {
            html.Append("<tr>");
            for (var c = range.Left; c <= range.Right; c++)
            {
                if (model.IsCovered(r, c))
                    continue;

                var merge = model.GetMerge(r, c);
                var rows = Math.Min(merge.Bottom, range.Bottom) - r + 1;
                var columns = Math.Min(merge.Right, range.Right) - c + 1;
                html.Append("<td");
                if (rows > 1)
                    html.Append($" rowspan=\"{rows}\"");
                if (columns > 1)
                    html.Append($" colspan=\"{columns}\"");
                html.Append('>').Append(ToHtml(model.GetCell(r, c).Runs)).Append("</td>");
            }

            html.Append("</tr>");
        }

        return html.Append("</table>").ToString();
    }

    /// <summary>Фрагменты текста как HTML: b, i, u, sup, sub; перенос строки — br.</summary>
    public static string ToHtml(IEnumerable<TextRun> runs)
    {
        var html = new StringBuilder();
        foreach (var run in runs)
        {
            var text = WebUtility.HtmlEncode(run.Text).Replace("\n", "<br>");
            if (run.Script == ScriptPosition.Superscript)
                text = $"<sup>{text}</sup>";
            else if (run.Script == ScriptPosition.Subscript)
                text = $"<sub>{text}</sub>";
            if (run.Underline)
                text = $"<u>{text}</u>";
            if (run.Italic)
                text = $"<i>{text}</i>";
            if (run.Bold)
                text = $"<b>{text}</b>";
            html.Append(text);
        }

        return html.ToString();
    }

    /// <summary>
    /// Формат «HTML Format» буфера обмена Windows: заголовок со смещениями в байтах UTF-8
    /// и фрагмент между StartFragment и EndFragment.
    /// </summary>
    public static string ToClipboardHtml(string fragment)
    {
        const string header = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string prefix = "<html><body><!--StartFragment-->";
        const string suffix = "<!--EndFragment--></body></html>";

        var headerLength = Encoding.UTF8.GetByteCount(string.Format(header, 0, 0, 0, 0));
        var startFragment = headerLength + Encoding.UTF8.GetByteCount(prefix);
        var endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        var endHtml = endFragment + Encoding.UTF8.GetByteCount(suffix);
        return string.Format(header, headerLength, endHtml, startFragment, endFragment) + prefix + fragment + suffix;
    }

    // ---------- Вставка ----------

    /// <summary>Текст Excel (Tab и переводы строк, кавычки для многострочных ячеек) → ячейки.</summary>
    public static List<List<TableCell>> FromTsv(string text, bool italic)
    {
        var rows = new List<List<TableCell>>();
        var row = new List<TableCell>();
        var cell = new StringBuilder();
        var quoted = false;
        var atCellStart = true;
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (text.EndsWith('\n'))
            text = text[..^1];

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(ch);
                }

                continue;
            }

            switch (ch)
            {
                case '"' when atCellStart:
                    quoted = true;
                    atCellStart = false;
                    break;
                case '\t':
                    row.Add(TableCell.FromText(cell.ToString(), italic));
                    cell.Clear();
                    atCellStart = true;
                    break;
                case '\n':
                    row.Add(TableCell.FromText(cell.ToString(), italic));
                    rows.Add(row);
                    row = new List<TableCell>();
                    cell.Clear();
                    atCellStart = true;
                    break;
                default:
                    cell.Append(ch);
                    atCellStart = false;
                    break;
            }
        }

        row.Add(TableCell.FromText(cell.ToString(), italic));
        rows.Add(row);
        return rows;
    }

    /// <summary>
    /// HTML из буфера (Word, Excel, браузер) → ячейки: таблица — по ячейкам (объединения раскрываются
    /// пустыми ячейками), без таблицы — одна ячейка. Сохраняются жирный, курсив, подчёркивание и индексы;
    /// курсив по умолчанию таблицы добавляется ко всему тексту (<paramref name="italic"/>).
    /// </summary>
    public static List<List<TableCell>> FromHtml(string html, bool italic)
    {
        var fragment = Fragment(html);
        var rows = new List<List<TableCell>>();
        var tableRows = Regex.Matches(fragment, @"<tr\b[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (tableRows.Count == 0)
        {
            rows.Add(new List<TableCell> { ParseCell(fragment, italic) });
            return rows;
        }

        // Ячейки, занятые объединениями из строк выше: (строка, столбец).
        var occupied = new HashSet<(int, int)>();
        for (var r = 0; r < tableRows.Count; r++)
        {
            var row = new List<TableCell>();
            var column = 0;
            foreach (Match cellMatch in Regex.Matches(tableRows[r].Groups[1].Value, @"<t[dh]\b([^>]*)>(.*?)</t[dh]>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                while (occupied.Contains((r, column)))
                {
                    row.Add(new TableCell());
                    column++;
                }

                var attributes = cellMatch.Groups[1].Value;
                var columnSpan = Span(attributes, "colspan");
                var rowSpan = Span(attributes, "rowspan");
                row.Add(ParseCell(cellMatch.Groups[2].Value, italic));
                for (var extra = 1; extra < columnSpan; extra++)
                    row.Add(new TableCell());
                for (var below = 1; below < rowSpan; below++)
                {
                    for (var c = column; c < column + columnSpan; c++)
                        occupied.Add((r + below, c));
                }

                column += columnSpan;
            }

            while (occupied.Contains((r, column)))
            {
                row.Add(new TableCell());
                column++;
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>Содержимое одной ячейки HTML → фрагменты с форматом.</summary>
    public static TableCell ParseCell(string html, bool italic)
    {
        var cell = new TableCell();
        var formats = new Stack<Format>();
        formats.Push(new Format(false, italic, false, ScriptPosition.Normal));
        var paragraphs = 0;

        // Отступы разметки вокруг абзацев и переносов (Word форматирует HTML с отступами) — не текст.
        html = Regex.Replace(html, @"<(style|script)\b.*?</\1\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        html = Regex.Replace(html, @"\s*(</?(?:p|div|li|br|ul|ol)\b[^>]*>)\s*", "$1", RegexOptions.IgnoreCase);

        foreach (Match token in Regex.Matches(html, @"<!--.*?-->|<![^>]*>|<(/?)([a-zA-Z0-9:]+)([^>]*)>|([^<]+)|<", RegexOptions.Singleline))
        {
            if (token.Value.StartsWith("<!"))
                continue;

            if (token.Value == "<")
            {
                Append(cell, formats.Peek(), "<");
                continue;
            }

            if (token.Groups[4].Success)
            {
                var text = WebUtility.HtmlDecode(Regex.Replace(token.Groups[4].Value, @"\s+", " ")).Replace('\u00A0', ' ');
                if (text.Length > 0)
                    Append(cell, formats.Peek(), text);
                continue;
            }

            var closing = token.Groups[1].Value == "/";
            var tag = token.Groups[2].Value.ToLowerInvariant();
            var attributes = token.Groups[3].Value;
            var selfClosing = attributes.TrimEnd().EndsWith('/');

            if (tag == "br")
            {
                Append(cell, formats.Peek(), "\n");
                continue;
            }

            if (tag is "p" or "div" or "li")
            {
                // Новый абзац внутри ячейки — новая строка (кроме первого).
                if (!closing && paragraphs++ > 0)
                    Append(cell, formats.Peek(), "\n");
                if (closing)
                    continue;
            }

            if (closing)
            {
                if (formats.Count > 1 && Formatting(tag))
                    formats.Pop();
                continue;
            }

            if (selfClosing || !Formatting(tag))
                continue;

            var current = formats.Peek();
            var style = Regex.Match(attributes, @"style\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase).Groups[1].Value.ToLowerInvariant();
            formats.Push(current with
            {
                Bold = current.Bold || tag is "b" or "strong" || Regex.IsMatch(style, @"font-weight\s*:\s*(bold|[6-9]00)"),
                Italic = current.Italic || tag is "i" or "em" || style.Contains("font-style:italic") || style.Contains("font-style: italic"),
                Underline = current.Underline || tag == "u" || Regex.IsMatch(style, @"text-decoration[^;]*underline"),
                Script = tag == "sup" || Regex.IsMatch(style, @"vertical-align\s*:\s*super") ? ScriptPosition.Superscript
                    : tag == "sub" || Regex.IsMatch(style, @"vertical-align\s*:\s*sub") ? ScriptPosition.Subscript
                    : current.Script,
            });
        }

        // Пробелы и переносы по краям ячейки (отступы разметки Word) не нужны.
        TrimCell(cell);
        cell.Normalize();
        return cell;
    }

    private static bool Formatting(string tag) => tag is "b" or "strong" or "i" or "em" or "u" or "sup" or "sub" or "span" or "font" or "p" or "div" or "li" or "a";

    private static void Append(TableCell cell, Format format, string text) =>
        cell.Runs.Add(new TextRun(text, format.Italic) { Bold = format.Bold, Underline = format.Underline, Script = format.Script });

    private static void TrimCell(TableCell cell)
    {
        while (cell.Runs.Count > 0 && cell.Runs[0].Text.TrimStart().Length == 0)
            cell.Runs.RemoveAt(0);
        if (cell.Runs.Count > 0)
            cell.Runs[0].Text = cell.Runs[0].Text.TrimStart();
        while (cell.Runs.Count > 0 && cell.Runs[^1].Text.TrimEnd().Length == 0)
            cell.Runs.RemoveAt(cell.Runs.Count - 1);
        if (cell.Runs.Count > 0)
            cell.Runs[^1].Text = cell.Runs[^1].Text.TrimEnd();
    }

    private static int Span(string attributes, string name)
    {
        var match = Regex.Match(attributes, name + @"\s*=\s*[""']?(\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var span) && span > 1 ? Math.Min(span, 1000) : 1;
    }

    /// <summary>Фрагмент из «HTML Format»: между StartFragment и EndFragment, иначе — весь текст.</summary>
    private static string Fragment(string html)
    {
        var start = html.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
        var end = html.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
        if (start >= 0 && end > start)
            return html[(start + "<!--StartFragment-->".Length)..end];

        var body = Regex.Match(html, @"<body[^>]*>(.*)</body>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return body.Success ? body.Groups[1].Value : html;
    }

    private static string QuoteTsv(string text) =>
        text.IndexOfAny(new[] { '\t', '\n', '"' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;

    private readonly record struct Format(bool Bold, bool Italic, bool Underline, ScriptPosition Script);
}
