using InventorTableEditor.Core.Editing;
using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Tests;

/// <summary>Кнопка «Переносить текст», как в Excel (метрики — <see cref="FixedMetrics"/>: символ = высота шрифта).</summary>
public class WrapTextTests
{
    private static TableModel LongText(bool wrap)
    {
        var model = TableModel.CreateEmpty(1, 1);
        // «aa bb cc» = 40 мм при доступных 18 мм.
        model.SetText(0, 0, "aa bb cc");
        model.GetCell(0, 0).Wrap = wrap;
        return model;
    }

    [Fact]
    public void Default_IsWrapOff()
    {
        Assert.False(new TableCell().Wrap);
        Assert.False(new TableSession(TableModel.CreateEmpty(1, 1)).WrapText);
    }

    [Fact]
    public void WrapOn_WrapsAndRowGrows()
    {
        var layout = TableLayout.Build(LongText(wrap: true), FixedMetrics.Instance);

        Assert.Equal(3, layout.Texts.Single().Lines.Count);
        Assert.True(layout.Height > 10);
        Assert.True(layout.Texts.Single().Wrap);
    }

    [Fact]
    public void WrapOff_OneLineCompressed_RowKeepsHeight()
    {
        var model = LongText(wrap: false);
        var layout = TableLayout.Build(model, FixedMetrics.Instance);
        var cell = layout.Texts.Single();

        Assert.Single(cell.Lines);
        Assert.False(cell.Wrap);
        Assert.Equal(10, layout.Height, 6);

        // 40 мм текста + 1,25 мм выступа наклонной «c» = 41,25 мм в 18 мм: 0,436… → 0,43.
        var plan = LinePlanner.PlanLine(cell.Lines[0], model.Style, cell, FixedMetrics.Instance);
        Assert.Equal(0.43, plan.WidthScale, 6);
    }

    [Fact]
    public void WrapOff_HardBreaksStillWork()
    {
        var model = LongText(wrap: false);
        model.SetText(0, 0, "aa\nbb");
        model.GetCell(0, 0).Wrap = false;

        Assert.Equal(2, TableLayout.Build(model, FixedMetrics.Instance).Texts.Single().Lines.Count);
    }

    [Fact]
    public void WrapOff_CaretMapHasOneLine()
    {
        var model = LongText(wrap: false);
        var layout = TableLayout.Build(model, FixedMetrics.Instance, includeEmptyCells: true).Texts.Single();

        var map = CaretMap.Build(model.GetCell(0, 0), layout, model.Style, FixedMetrics.Instance);

        Assert.Single(map.Lines);
        Assert.Equal(9, map.Lines[0].XMm.Count);
    }

    [Fact]
    public void Session_SetWrapText_TargetsSelection_AndUndoes()
    {
        var session = new TableSession(TableModel.CreateEmpty(2, 2));
        session.BeginSelection(0, 0);
        session.ExtendSelection(0, 1);

        session.SetWrapText(true);

        Assert.True(session.Model.GetCell(0, 0).Wrap);
        Assert.True(session.Model.GetCell(0, 1).Wrap);
        Assert.False(session.Model.GetCell(1, 0).Wrap);
        Assert.True(session.WrapText);

        session.Undo();
        Assert.False(session.Model.GetCell(0, 1).Wrap);
    }

    [Fact]
    public void TypingKeepsWrap_AndJsonRoundTrip()
    {
        var session = new TableSession(TableModel.CreateEmpty(1, 1));
        session.SetWrapText(true);

        session.ReplaceCell(0, 0, TableCell.FromText("x", italic: false), EditKind.Typing);
        var restored = TableModel.FromJson(session.Model.ToJson());

        Assert.True(session.Model.GetCell(0, 0).Wrap);
        Assert.True(restored.GetCell(0, 0).Wrap);
        Assert.DoesNotContain("\"Wrap\"", TableModel.CreateEmpty(1, 1).ToJson());
    }

    [Fact]
    public void DefaultRowHeight_Is10mm()
    {
        Assert.Equal(10, new TableStyle().DefaultRowHeightMm);
        Assert.All(TableModel.CreateEmpty(3, 1).RowHeights, h => Assert.Equal(10, h));
    }

    [Fact]
    public void WrapOn_CompressesUpToLimitBeforeWrapping()
    {
        var model = TableModel.CreateEmpty(1, 1);
        model.Style.Italic = false;
        // «ab c» = 20 мм текста в 18 мм: сжатие 0,9 ≥ 0,75 — не переносится, а сжимается.
        model.SetText(0, 0, "ab c");
        model.GetCell(0, 0).Runs.ForEach(r => r.Italic = false);
        model.GetCell(0, 0).Wrap = true;
        var layout = TableLayout.Build(model, FixedMetrics.Instance);
        var cell = layout.Texts.Single();

        Assert.Single(cell.Lines);
        Assert.Equal(0.9, LinePlanner.PlanLine(cell.Lines[0], model.Style, cell, FixedMetrics.Instance).WidthScale, 6);
        Assert.Equal(10, layout.Height, 6);
    }

    [Fact]
    public void WrapOn_BeyondLimitWraps_AndWrappedLinesStayWithinLimit()
    {
        var model = TableModel.CreateEmpty(1, 1);
        // 40 мм текста в 18 мм: сжатие 0,45 < 0,75 — переносится.
        model.SetText(0, 0, "aa bb cc");
        model.GetCell(0, 0).Wrap = true;
        var layout = TableLayout.Build(model, FixedMetrics.Instance);
        var cell = layout.Texts.Single();

        Assert.True(cell.Lines.Count > 1);
        foreach (var line in cell.Lines)
            Assert.True(LinePlanner.PlanLine(line, model.Style, cell, FixedMetrics.Instance).WidthScale >= model.Style.MinScaleBeforeWrap);
    }

    [Theory]
    [InlineData(1.0, 2)]
    [InlineData(0.5, 1)]
    public void WrapWidth_FollowsLimit(double limit, int expectedLines)
    {
        var model = TableModel.CreateEmpty(1, 1);
        model.Style.MinScaleBeforeWrap = limit;
        // «ab cd» = 25 мм в 18 мм: без сжатия переносится, с пределом 0,5 (36 мм) — нет.
        model.SetText(0, 0, "ab cd");
        model.GetCell(0, 0).Wrap = true;

        Assert.Equal(expectedLines, TableLayout.Build(model, FixedMetrics.Instance).Texts.Single().Lines.Count);
        Assert.Equal(18 / limit, TableLayout.WrapWidth(20, model.Style), 6);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1.5")]
    [InlineData("-1")]
    public void FromJson_InvalidLimitResetToDefault(string value)
    {
        var json = $$"""{"Style":{"MinScaleBeforeWrap":{{value}}},"ColumnWidths":[20],"RowHeights":[10],"Cells":[[{"Runs":[]}]]}""";

        Assert.Equal(0.75, TableModel.FromJson(json).Style.MinScaleBeforeWrap);
    }

    [Fact]
    public void OldTablesWithoutLimit_GetDefault()
    {
        var json = """{"Style":{"FontSizeMm":5},"ColumnWidths":[20],"RowHeights":[8],"Cells":[[{"Runs":[]}]]}""";

        Assert.Equal(0.75, TableModel.FromJson(json).Style.MinScaleBeforeWrap);
    }

    [Fact]
    public void Version2Tables_KeepWrapOnUnlessMarkedNoWrap()
    {
        var json = """{"Version":2,"ColumnWidths":[20,20],"RowHeights":[10],"Cells":[[{"Runs":[]},{"NoWrap":true,"Runs":[]}]]}""";

        var model = TableModel.FromJson(json);

        Assert.True(model.GetCell(0, 0).Wrap);
        Assert.False(model.GetCell(0, 1).Wrap);
        Assert.Equal(TableModel.CurrentVersion, model.Version);
        Assert.DoesNotContain("NoWrap", model.ToJson());
    }

    [Fact]
    public void Version1Tables_GetWrapOn()
    {
        var json = """{"Version":1,"ColumnWidths":[20],"RowHeights":[8],"Cells":[["a"]]}""";

        Assert.True(TableModel.FromJson(json).GetCell(0, 0).Wrap);
    }

    [Fact]
    public void Version3Tables_KeepWrapOff()
    {
        var model = TableModel.CreateEmpty(1, 1);

        Assert.False(TableModel.FromJson(model.ToJson()).GetCell(0, 0).Wrap);
    }
}
