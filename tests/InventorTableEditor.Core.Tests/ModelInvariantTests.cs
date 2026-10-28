using InventorTableEditor.Core.Clipboard;
using InventorTableEditor.Core.Editing;
using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Tests;

/// <summary>
/// Случайные последовательности действий со структурой таблицы (как пользователь в редакторе):
/// после каждого действия таблица должна оставаться целой, а отмена — возвращать её к началу.
/// </summary>
public class ModelInvariantTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void RandomEdits_KeepTableConsistent_AndUndoAllRestoresStart(int seed)
    {
        var random = new Random(seed);
        var session = new TableSession(TableModel.CreateEmpty(3, 3), () => 0);
        var start = session.Model.ToJson();
        var steps = 0;

        for (var i = 0; i < 150; i++)
        {
            var before = session.Model.ToJson();
            var changed = RandomAction(session, random);
            AssertConsistent(session);
            if (changed && session.Model.ToJson() != before)
                steps++;
        }

        // Каждое действие — не больше одного шага; отменяется всё до исходной таблицы.
        var undone = 0;
        while (session.Undo() != null)
            undone++;
        Assert.Equal(start, session.Model.ToJson());
        Assert.True(undone >= steps, $"отменено {undone}, изменений {steps}");
    }

    private static bool RandomAction(TableSession session, Random random)
    {
        var model = session.Model;
        session.MoveTo(random.Next(model.RowCount), random.Next(model.ColumnCount));
        switch (random.Next(14))
        {
            case 0:
                session.InsertRowAbove();
                return true;
            case 1:
                session.InsertRowBelow();
                return true;
            case 2:
                session.InsertColumnLeft();
                return true;
            case 3:
                session.InsertColumnRight();
                return true;
            case 4 when model.RowCount > 1:
                session.DeleteRow();
                return true;
            case 5 when model.ColumnCount > 1:
                session.DeleteColumn();
                return true;
            case 6:
                session.BeginSelection(session.Row, session.Column);
                session.ExtendSelection(random.Next(model.RowCount), random.Next(model.ColumnCount));
                return session.MergeSelection();
            case 7:
                return session.SplitCurrent();
            case 8:
                session.SetHeader(!model.HasHeader);
                return true;
            case 9:
                var cell = TableCell.FromText($"t{random.Next(100)}", italic: true);
                return session.ReplaceCell(session.Row, session.Column, cell, EditKind.Other);
            case 10:
                var rows = random.Next(1, 3);
                var columns = random.Next(1, 3);
                var tsv = string.Join("\r\n", Enumerable.Range(0, rows).Select(r => string.Join("\t", Enumerable.Range(0, columns).Select(c => $"p{r}{c}"))));
                return session.PasteCells(TableClipboard.FromTsv(tsv, italic: true));
            case 11:
                return session.SetColumnWidth(random.Next(5, 40));
            case 12:
                return session.SetRowHeight(random.Next(5, 30));
            default:
                session.SetWrapText(!session.WrapText);
                return true;
        }
    }

    private static void AssertConsistent(TableSession session)
    {
        var model = session.Model;
        Assert.True(model.RowCount >= 1 && model.ColumnCount >= 1);
        Assert.Equal(model.RowCount, model.Cells.Count);
        Assert.All(model.Cells, row => Assert.Equal(model.ColumnCount, row.Count));
        Assert.All(model.ColumnWidths.Concat(model.RowHeights), size => Assert.InRange(size, MmParser.MinCellSize, MmParser.MaxCellSize));

        // Объединения внутри таблицы и не пересекаются; закрытые ими ячейки пусты.
        var taken = new bool[model.RowCount, model.ColumnCount];
        foreach (var merge in model.Merges())
        {
            Assert.InRange(merge.Bottom, 0, model.RowCount - 1);
            Assert.InRange(merge.Right, 0, model.ColumnCount - 1);
            foreach (var (row, column) in merge.Cells())
            {
                Assert.False(taken[row, column], $"ячейка {row},{column} в двух объединениях");
                taken[row, column] = true;
                if ((row, column) != (merge.Top, merge.Left))
                {
                    Assert.True(model.GetCell(row, column).IsEmpty);
                    Assert.False(model.GetCell(row, column).IsMergeAnchor);
                }
            }
        }

        // Текущая ячейка — видимая (не закрытая объединением); выделение — в таблице.
        Assert.False(model.IsCovered(session.Row, session.Column));
        if (session.Range is { } range)
        {
            Assert.InRange(range.Bottom, 0, model.RowCount - 1);
            Assert.InRange(range.Right, 0, model.ColumnCount - 1);
            Assert.Equal(range, model.ExpandToMerges(range));
        }

        // Данные сохраняются и читаются без потерь, раскладка строится.
        var json = model.ToJson();
        Assert.Equal(json, TableModel.FromJson(json).ToJson());
        var layout = TableLayout.Build(model, FixedMetrics.Instance, includeEmptyCells: true);
        Assert.Equal(model.RowCount + 1, layout.RowEdges.Count);
        Assert.Equal(model.Cells.SelectMany(r => r).Count() - model.Merges().Sum(m => m.Count - 1), layout.Texts.Count);
        if (model.HasHeader)
            Assert.True(model.RowCount >= 1);
    }
}
