using InventorTableEditor.Core.Editing;
using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Tests;

/// <summary>Позиции курсора с <see cref="FixedMetrics"/>: символ 5 мм, отступ ячейки 1 мм, выравнивание влево.</summary>
public class CaretMapTests
{
    private static CaretMap Map(string text, double columnWidth = 100)
    {
        var model = TableModel.CreateEmpty(1, 1);
        model.ColumnWidths[0] = columnWidth;
        model.SetText(0, 0, text);
        var layout = TableLayout.Build(model, FixedMetrics.Instance, includeEmptyCells: true).Texts.Single();
        return CaretMap.Build(model.GetCell(0, 0), layout, model.Style, FixedMetrics.Instance);
    }

    [Fact]
    public void Positions_FollowCharacterWidths()
    {
        // Текст таблицы наклонный, но ширины букв от наклона не зависят.
        var line = Map("abc").Lines.Single();

        Assert.Equal(new[] { 1.0, 6, 11, 16 }, line.XMm);
    }

    [Fact]
    public void SoftWrap_CaretAtBoundaryBelongsToNextLine()
    {
        // Ширина 20: доступно 18 мм, с пределом сжатия 75 % — 24 мм; «ab cd» (25 мм) переносится после пробела.
        var map = Map("ab cd", 20);

        Assert.Equal(2, map.Lines.Count);
        Assert.Equal((0, 3), (map.Lines[0].Start, map.Lines[0].End));
        Assert.Equal((3, 5), (map.Lines[1].Start, map.Lines[1].End));
        Assert.Same(map.Lines[1], map.LineOf(3));
        // Пробел в конце первой строки не рисуется, но курсор после него стоит правее текста.
        Assert.Equal(new[] { 1.0, 6, 11, 16 }, map.Lines[0].XMm);
    }

    [Fact]
    public void HardBreak_CaretBeforeBreakStaysOnItsLine()
    {
        var map = Map("a\nb");

        Assert.Equal((0, 1), (map.Lines[0].Start, map.Lines[0].End));
        Assert.Equal((2, 3), (map.Lines[1].Start, map.Lines[1].End));
        Assert.Same(map.Lines[0], map.LineOf(1));
        Assert.Same(map.Lines[1], map.LineOf(2));
    }

    [Fact]
    public void HitTest_PicksLineByHeightAndNearestPosition()
    {
        var map = Map("ab cd", 20);
        var second = map.Lines[1];

        Assert.Equal(4, map.HitTest(7, second.BaselineMm));
        Assert.Equal(1, map.HitTest(7, map.Lines[0].BaselineMm - 1));
        // Ниже последней строки — последняя строка; правее текста — конец строки.
        Assert.Equal(5, map.HitTest(500, 1000));
    }

    [Fact]
    public void VerticalMove_KeepsXAndStopsAtEdges()
    {
        var map = Map("ab cd", 20);

        Assert.Equal(4, map.VerticalMove(1, +1));
        Assert.Equal(1, map.VerticalMove(4, -1));
        Assert.Null(map.VerticalMove(4, +1));
        Assert.Null(map.VerticalMove(1, -1));
    }

    [Fact]
    public void EmptyCell_HasOneLineWithCaretAtCellStart()
    {
        var map = Map(string.Empty);

        var line = map.Lines.Single();
        Assert.Equal(new[] { 1.0 }, line.XMm);
        Assert.Equal(0, map.HitTest(50, 50));
    }

    [Fact]
    public void Indices_CaretFollowsRunPens()
    {
        var model = TableModel.CreateEmpty(1, 1);
        model.SetCell(0, 0, new TableCell
        {
            Runs = { new TextRun("D"), new TextRun("2") { Script = ScriptPosition.Superscript }, new TextRun("x") },
        });
        var layout = TableLayout.Build(model, FixedMetrics.Instance, includeEmptyCells: true).Texts.Single();

        var line = CaretMap.Build(model.GetCell(0, 0), layout, model.Style, FixedMetrics.Instance).Lines.Single();

        // D — 5 мм, индекс 2 — 3,25 мм.
        Assert.Equal(new[] { 1.0, 6, 9.25, 14.25 }, line.XMm.Select(x => Math.Round(x, 6)));
    }
}
