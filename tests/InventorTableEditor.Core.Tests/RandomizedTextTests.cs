using System.Text;
using InventorTableEditor.Core.Clipboard;
using InventorTableEditor.Core.Editing;
using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Tests;

/// <summary>Случайные правки текста ячейки и обмен через буфер: сверка с простой строкой-образцом.</summary>
public class RandomizedTextTests
{
    private static readonly TableStyle Style = new();

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public void BufferEdits_MatchPlainStringModel(int seed)
    {
        var random = new Random(seed);
        var buffer = new CellTextBuffer(new TableCell(), Style);
        var expected = new StringBuilder();

        for (var i = 0; i < 400; i++)
        {
            var anchor = random.Next(expected.Length + 1);
            var caret = random.Next(expected.Length + 1);
            buffer.MoveTo(anchor, extend: false);
            buffer.MoveTo(caret, extend: true);
            var (start, end) = (Math.Min(anchor, caret), Math.Max(anchor, caret));

            switch (random.Next(6))
            {
                case 0:
                case 1:
                    var text = random.Next(5) == 0 ? "\n" : ((char)('a' + random.Next(26))).ToString();
                    buffer.Insert(text);
                    expected.Remove(start, end - start).Insert(start, text);
                    Assert.Equal(start + text.Length, buffer.Caret);
                    break;
                case 2:
                    buffer.Backspace();
                    if (end > start)
                        expected.Remove(start, end - start);
                    else if (start > 0)
                        expected.Remove(start - 1, 1);
                    break;
                case 3:
                    buffer.Delete();
                    if (end > start)
                        expected.Remove(start, end - start);
                    else if (start < expected.Length)
                        expected.Remove(start, 1);
                    break;
                case 4:
                    buffer.ToggleScript(random.Next(2) == 0 ? ScriptPosition.Superscript : ScriptPosition.Subscript);
                    break;
                default:
                    var runs = buffer.SelectedRuns();
                    Assert.Equal(expected.ToString(start, end - start), string.Concat(runs.Select(r => r.Text)));
                    buffer.InsertRuns(runs);
                    break;
            }

            Assert.Equal(expected.ToString(), buffer.Text);
            var cell = buffer.ToCell(new TableCell());
            Assert.Equal(expected.ToString(), cell.PlainText);
            // Соседние фрагменты всегда с разным форматом (склеены).
            Assert.All(cell.Runs.Zip(cell.Runs.Skip(1)), pair => Assert.False(pair.First.SameFormat(pair.Second)));
        }
    }

    [Theory]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    public void CaretMap_CoversEveryPositionInOrder(int seed)
    {
        var random = new Random(seed);
        for (var i = 0; i < 40; i++)
        {
            var cell = RandomCell(random, allowLineBreaks: true);
            var model = TableModel.CreateEmpty(1, 1);
            model.ColumnWidths[0] = random.Next(10, 60);
            model.SetCell(0, 0, cell);
            model.GetCell(0, 0).Wrap = random.Next(2) == 0;
            var layout = TableLayout.Build(model, FixedMetrics.Instance, includeEmptyCells: true).Texts.Single();

            var map = CaretMap.Build(model.GetCell(0, 0), layout, model.Style, FixedMetrics.Instance);
            var length = model.GetCell(0, 0).PlainText.Length;

            for (var index = 0; index <= length; index++)
                Assert.NotNull(map.LineOf(index));
            foreach (var line in map.Lines)
            {
                Assert.Equal(line.End - line.Start + 1, line.XMm.Count);
                Assert.All(line.XMm.Zip(line.XMm.Skip(1)), pair => Assert.True(pair.Second >= pair.First - 1e-9));
            }

            Assert.All(map.Lines.Zip(map.Lines.Skip(1)), pair => Assert.True(pair.Second.BaselineMm > pair.First.BaselineMm));
        }
    }

    [Theory]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    public void Html_RoundTripOfRandomFormattedText(int seed)
    {
        var random = new Random(seed);
        for (var i = 0; i < 60; i++)
        {
            var cell = RandomCell(random, allowLineBreaks: true);
            var model = TableModel.CreateEmpty(1, 1);
            model.SetCell(0, 0, cell);
            var html = TableClipboard.ToClipboardHtml(TableClipboard.ToHtmlTable(model, new CellRange(0, 0, 0, 0)));

            var pasted = TableClipboard.FromHtml(html, italic: false).Single().Single();

            Assert.True(model.GetCell(0, 0).SameContent(pasted),
                $"{Describe(model.GetCell(0, 0))} → {Describe(pasted)}");
        }
    }

    [Theory]
    [InlineData(41)]
    [InlineData(42)]
    public void Tsv_RoundTripOfRandomTables(int seed)
    {
        var random = new Random(seed);
        const string alphabet = "ab \"\t\n,;";
        for (var i = 0; i < 40; i++)
        {
            var model = TableModel.CreateEmpty(random.Next(1, 4), random.Next(1, 4));
            foreach (var (row, column) in new CellRange(0, 0, model.RowCount - 1, model.ColumnCount - 1).Cells())
            {
                var text = new string(Enumerable.Range(0, random.Next(0, 6)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
                model.SetText(row, column, text);
            }

            var range = new CellRange(0, 0, model.RowCount - 1, model.ColumnCount - 1);
            var rows = TableClipboard.FromTsv(TableClipboard.ToTsv(model, range), italic: true);

            // Пустая последняя строка из одной пустой ячейки неотличима от завершающего перевода строки.
            var lastRowEmpty = model.ColumnCount == 1 && model.GetText(model.RowCount - 1, 0) == "";
            Assert.Equal(model.RowCount - (lastRowEmpty && model.RowCount > 1 ? 1 : 0), rows.Count);
            for (var r = 0; r < rows.Count; r++)
            {
                for (var c = 0; c < model.ColumnCount; c++)
                    Assert.Equal(model.GetText(r, c), rows[r][c].PlainText);
            }
        }
    }

    /// <summary>Случайный текст из слов с разным форматом; пробелы только между словами, без пустых строк по краям.</summary>
    private static TableCell RandomCell(Random random, bool allowLineBreaks)
    {
        var cell = new TableCell();
        var words = random.Next(1, 8);
        for (var w = 0; w < words; w++)
        {
            if (w > 0)
                cell.Runs.Add(new TextRun(allowLineBreaks && random.Next(4) == 0 ? "\n" : " ", italic: true));
            const string letters = "abcxyzRσ<&";
            var word = new string(Enumerable.Range(0, random.Next(1, 5)).Select(_ => letters[random.Next(letters.Length)]).ToArray());
            cell.Runs.Add(new TextRun(word, italic: random.Next(3) > 0)
            {
                Bold = random.Next(3) == 0,
                Underline = random.Next(4) == 0,
                Script = (ScriptPosition)random.Next(3),
            });
        }

        cell.Normalize();
        return cell;
    }

    private static string Describe(TableCell cell) =>
        string.Join("|", cell.Runs.Select(r => $"{r.Text.Replace("\n", "\\n")}:{(r.Bold ? "b" : "")}{(r.Italic ? "i" : "")}{(r.Underline ? "u" : "")}{(int)r.Script}"));
}
