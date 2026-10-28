using System.Text;
using InventorTableEditor.Core.Clipboard;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Tests;

public class TableClipboardTests
{
    private static TableModel Table(int rows = 3, int columns = 3)
    {
        var model = TableModel.CreateEmpty(rows, columns);
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < columns; c++)
                model.SetText(r, c, $"{r}{c}");
        }

        return model;
    }

    private static string[][] Texts(List<List<TableCell>> rows) =>
        rows.Select(row => row.Select(cell => cell.PlainText).ToArray()).ToArray();

    // ---------- Текст Excel ----------

    [Fact]
    public void ToTsv_TabsAndCrLf()
    {
        Assert.Equal("11\t12\r\n21\t22", TableClipboard.ToTsv(Table(), new CellRange(1, 1, 2, 2)));
    }

    [Fact]
    public void ToTsv_QuotesMultilineAndQuotes()
    {
        var model = Table(1, 2);
        model.SetText(0, 0, "a\nb");
        model.SetText(0, 1, "say \"hi\"");

        Assert.Equal("\"a\nb\"\t\"say \"\"hi\"\"\"", TableClipboard.ToTsv(model, new CellRange(0, 0, 0, 1)));
    }

    [Fact]
    public void ToTsv_CoveredCellsAreEmpty()
    {
        var model = Table();
        model.Merge(new CellRange(0, 0, 0, 1));

        Assert.Equal("00\n01\t\t02", TableClipboard.ToTsv(model, new CellRange(0, 0, 0, 2)).Replace("\"", ""));
    }

    [Fact]
    public void FromTsv_ParsesExcelText()
    {
        var rows = TableClipboard.FromTsv("a\tb\r\nc\td\r\n", italic: true);

        Assert.Equal(new[] { new[] { "a", "b" }, new[] { "c", "d" } }, Texts(rows));
        Assert.True(rows[0][0].Runs[0].Italic);
    }

    [Fact]
    public void FromTsv_QuotedCellsKeepLineBreaksTabsAndQuotes()
    {
        var rows = TableClipboard.FromTsv("\"a\nb\"\t\"x\ty\"\t\"q\"\"q\"", italic: false);

        Assert.Equal(new[] { new[] { "a\nb", "x\ty", "q\"q" } }, Texts(rows));
        Assert.False(rows[0][0].Runs[0].Italic);
    }

    [Fact]
    public void FromTsv_QuoteInsideTextIsLiteral()
    {
        var rows = TableClipboard.FromTsv("5\" pipe\tb", italic: false);

        Assert.Equal(new[] { new[] { "5\" pipe", "b" } }, Texts(rows));
    }

    [Fact]
    public void FromTsv_EmptyCellsKept()
    {
        var rows = TableClipboard.FromTsv("a\t\tc", italic: false);

        Assert.Equal(new[] { new[] { "a", "", "c" } }, Texts(rows));
    }

    [Fact]
    public void Tsv_RoundTrip()
    {
        var model = Table(2, 2);
        model.SetText(0, 0, "line 1\nline \"2\"");
        model.SetText(1, 1, "tab\there");
        var range = new CellRange(0, 0, 1, 1);

        var rows = TableClipboard.FromTsv(TableClipboard.ToTsv(model, range), italic: true);

        foreach (var (r, c) in range.Cells())
            Assert.Equal(model.GetText(r, c), rows[r][c].PlainText);
    }

    // ---------- HTML: копирование ----------

    [Fact]
    public void ToHtml_FormatsRuns()
    {
        var runs = new[]
        {
            new TextRun("R", italic: true) { Bold = true },
            new TextRun("1") { Script = ScriptPosition.Subscript },
            new TextRun("2") { Script = ScriptPosition.Superscript, Underline = true },
            new TextRun("a<b\nc"),
        };

        Assert.Equal("<b><i>R</i></b><sub>1</sub><u><sup>2</sup></u>a&lt;b<br>c", TableClipboard.ToHtml(runs));
    }

    [Fact]
    public void ToHtmlTable_WritesSpansClippedToRange()
    {
        var model = Table();
        model.Merge(new CellRange(0, 0, 1, 2));

        var html = TableClipboard.ToHtmlTable(model, new CellRange(0, 0, 1, 1));

        Assert.Contains("<tr><td rowspan=\"2\" colspan=\"2\">", html);
        Assert.Equal(2, CountOf(html, "<tr>"));
        Assert.Equal(1, CountOf(html, "<td"));
    }

    [Fact]
    public void ToClipboardHtml_OffsetsPointToFragmentInUtf8()
    {
        const string fragment = "<table><tr><td>Ø±°</td></tr></table>";

        var data = TableClipboard.ToClipboardHtml(fragment);
        var bytes = Encoding.UTF8.GetBytes(data);

        int Offset(string name) => int.Parse(System.Text.RegularExpressions.Regex.Match(data, name + @":(\d+)").Groups[1].Value);
        var start = Offset("StartFragment");
        var end = Offset("EndFragment");
        Assert.Equal(fragment, Encoding.UTF8.GetString(bytes, start, end - start));
        Assert.Equal(bytes.Length, Offset("EndHTML"));
        Assert.StartsWith("<html>", Encoding.UTF8.GetString(bytes, Offset("StartHTML"), 6));
    }

    // ---------- HTML: вставка ----------

    [Fact]
    public void Html_RoundTripKeepsFormatsAndText()
    {
        var model = Table(2, 2);
        var cell = new TableCell();
        cell.Runs.Add(new TextRun("σ", italic: true));
        cell.Runs.Add(new TextRun("max", italic: true) { Script = ScriptPosition.Subscript });
        cell.Runs.Add(new TextRun(" = 10", italic: true));
        cell.Runs.Add(new TextRun("-3", italic: true) { Script = ScriptPosition.Superscript, Bold = true });
        cell.Runs.Add(new TextRun("\nм", italic: true) { Underline = true });
        model.SetCell(0, 1, cell);
        var range = new CellRange(0, 0, 1, 1);

        var rows = TableClipboard.FromHtml(TableClipboard.ToClipboardHtml(TableClipboard.ToHtmlTable(model, range)), italic: false);

        Assert.Equal(new[] { new[] { "00", "σmax = 10-3\nм" }, new[] { "10", "11" } }, Texts(rows));
        Assert.True(rows[0][1].SameContent(cell));
    }

    [Fact]
    public void FromHtml_WordTableWithIndices()
    {
        const string html = """
            Version:0.9
            StartHTML:0000000105
            <html><body>
            <!--StartFragment-->
            <table class=MsoTableGrid border=1>
             <tr>
              <td width=100 valign=top style='width:100pt'>
              <p class=MsoNormal><span lang=EN-US>R</span><sub><span lang=EN-US>a</span></sub><span
              lang=EN-US>&nbsp;=&nbsp;3,2</span><o:p></o:p></p>
              </td>
              <td width=100 valign=top>
              <p class=MsoNormal>мм<sup>2</sup><o:p></o:p></p>
              <p class=MsoNormal><b>итого</b><o:p></o:p></p>
              </td>
             </tr>
            </table>
            <!--EndFragment-->
            </body></html>
            """;

        var rows = TableClipboard.FromHtml(html, italic: true);

        Assert.Equal(new[] { new[] { "Ra = 3,2", "мм2\nитого" } }, Texts(rows));
        var first = rows[0][0].Runs;
        Assert.Equal(new[] { "R", "a", " = 3,2" }, first.Select(r => r.Text));
        Assert.Equal(ScriptPosition.Subscript, first[1].Script);
        Assert.All(first, r => Assert.True(r.Italic));
        var second = rows[0][1].Runs;
        Assert.Equal(ScriptPosition.Superscript, second.Single(r => r.Text == "2").Script);
        Assert.True(second.Single(r => r.Text.Contains("итого")).Bold);
    }

    [Fact]
    public void FromHtml_SpanStylesAndVerticalAlign()
    {
        const string html = "<table><tr><td><span style=\"font-weight:700\">B</span><span style='font-style: italic'>I</span>" +
                            "<span style=\"text-decoration:underline\">U</span>x<span style=\"vertical-align:super\">2</span>" +
                            "y<span style=\"vertical-align:sub\">0</span></td></tr></table>";

        var runs = TableClipboard.FromHtml(html, italic: false)[0][0].Runs;

        Assert.Equal(new[] { "B", "I", "U", "x", "2", "y", "0" }, runs.Select(r => r.Text));
        Assert.True(runs[0].Bold);
        Assert.False(runs[0].Italic);
        Assert.True(runs[1].Italic);
        Assert.True(runs[2].Underline);
        Assert.Equal(ScriptPosition.Superscript, runs[4].Script);
        Assert.Equal(ScriptPosition.Subscript, runs[6].Script);
        Assert.Equal(ScriptPosition.Normal, runs[5].Script);
    }

    [Fact]
    public void FromHtml_SpansExpandToEmptyCells()
    {
        const string html = "<table>" +
                            "<tr><td rowspan=2>A</td><td colspan=\"2\">B</td></tr>" +
                            "<tr><td>C</td><td>D</td></tr>" +
                            "<tr><td>E</td><td>F</td><td>G</td></tr>" +
                            "</table>";

        var rows = TableClipboard.FromHtml(html, italic: false);

        Assert.Equal(new[]
        {
            new[] { "A", "B", "" },
            new[] { "", "C", "D" },
            new[] { "E", "F", "G" },
        }, Texts(rows));
    }

    [Fact]
    public void FromHtml_ExcelHeaderCellsAndBreaks()
    {
        const string html = "<html><head><style>td {color:red}</style></head><body><table><col width=64>" +
                            "<tr><th>Name</th><td>a<br>b</td></tr></table></body></html>";

        var rows = TableClipboard.FromHtml(html, italic: false);

        Assert.Equal(new[] { new[] { "Name", "a\nb" } }, Texts(rows));
    }

    [Fact]
    public void FromHtml_WithoutTable_IsSingleCell()
    {
        const string html = "<!--StartFragment--><p>H<sub>2</sub>O</p><![if !supportLists]>x<![endif]><p>2 &lt; 3 &amp; 4</p><!--EndFragment-->";

        var rows = TableClipboard.FromHtml(html, italic: false);

        Assert.Single(rows);
        Assert.Single(rows[0]);
        Assert.Equal("H2Ox\n2 < 3 & 4", rows[0][0].PlainText);
        Assert.Equal(ScriptPosition.Subscript, rows[0][0].Runs[1].Script);
    }

    [Fact]
    public void ParseCell_TrimsMarkupWhitespaceAndEmptyCell()
    {
        Assert.True(TableClipboard.ParseCell("  <p>&nbsp;</p>  ", italic: true).IsEmpty);
        Assert.Empty(TableClipboard.ParseCell("  <p>&nbsp;</p>  ", italic: true).Runs);
        Assert.Equal("a b", TableClipboard.ParseCell("\n  a\n   b \n", italic: true).PlainText);
    }

    [Fact]
    public void ParseCell_RawLessThanIsText()
    {
        Assert.Equal("a < b", TableClipboard.ParseCell("a < b", italic: false).PlainText);
    }

    private static int CountOf(string text, string part) =>
        (text.Length - text.Replace(part, "").Length) / part.Length;
}
