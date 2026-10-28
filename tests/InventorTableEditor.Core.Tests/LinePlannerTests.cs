using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Tests;

/// <summary>
/// Расстановка строки с <see cref="FixedMetrics"/>: символ шириной в высоту шрифта (5 мм),
/// индекс — 0,65 высоты (3,25 мм), выступ наклонных букв — 0,25 высоты, отступ ячейки — 1 мм.
/// </summary>
public class LinePlannerTests
{
    private const double Size = 5;
    private const double ScriptSize = 5 * 0.65;
    private const double ItalicOverhang = FixedMetrics.Overhang * Size;

    private static (LinePlan Plan, CellText Cell) Plan(double columnWidth, HorizontalTextAlignment? alignment, params TextRun[] runs)
    {
        var model = TableModel.CreateEmpty(1, 1);
        model.ColumnWidths[0] = columnWidth;
        model.SetCell(0, 0, new TableCell { Runs = runs.ToList(), HorizontalAlignment = alignment });
        var cell = TableLayout.Build(model, FixedMetrics.Instance, includeEmptyCells: true).Texts.Single();
        return (LinePlanner.PlanLine(cell.Lines[0], model.Style, cell, FixedMetrics.Instance), cell);
    }

    private static LinePlan Plan(params TextRun[] runs) => Plan(100, null, runs).Plan;

    private static TextRun Upright(string text) => new(text);

    private static TextRun Italic(string text) => new(text, italic: true);

    private static TextRun Script(string text, ScriptPosition script, bool italic = false) => new(text, italic) { Script = script };

    [Fact]
    public void UprightRuns_FollowEachOtherByAdvance()
    {
        var plan = Plan(Upright("ab"), new TextRun("cd") { Bold = true });

        Assert.Equal(new[] { 0.0, 10.0 }, plan.Runs.Select(r => r.PenMm));
        Assert.Equal(1.0, plan.WidthScale);
        Assert.Equal(1.0, plan.OriginMm, 6);
    }

    [Fact]
    public void ItalicBeforeUpright_GetsItalicCorrection()
    {
        var plan = Plan(Italic("ab"), Upright("c"));

        Assert.Equal(10 + ItalicOverhang, plan.Runs[1].PenMm, 6);
    }

    [Fact]
    public void UprightBeforeItalic_NoCorrection()
    {
        var plan = Plan(Upright("ab"), Italic("c"));

        Assert.Equal(10, plan.Runs[1].PenMm, 6);
    }

    [Fact]
    public void ItalicBeforeSuperscript_GetsCorrection_BeforeSubscript_DoesNot()
    {
        var super = Plan(Italic("D"), Script("2", ScriptPosition.Superscript));
        var sub = Plan(Italic("D"), Script("2", ScriptPosition.Subscript));

        Assert.Equal(Size + ItalicOverhang, super.Runs[1].PenMm, 6);
        Assert.Equal(Size, sub.Runs[1].PenMm, 6);
    }

    [Fact]
    public void SuperscriptThenSubscript_AreStackedAndTextContinuesAfterLongerOne()
    {
        var plan = Plan(
            Italic("D"),
            Script("2", ScriptPosition.Superscript),
            Script("max", ScriptPosition.Subscript),
            Upright("x"));

        var superscript = plan.Runs[1];
        var subscript = plan.Runs[2];
        // Верхний — с поправкой на наклон, нижний — с того же места без поправки.
        Assert.Equal(Size + ItalicOverhang, superscript.PenMm, 6);
        Assert.Equal(Size, subscript.PenMm, 6);
        // Нижний длиннее: «x» идёт после него.
        Assert.Equal(Size + 3 * ScriptSize, plan.Runs[3].PenMm, 6);
        Assert.True(superscript.BaselineMm < subscript.BaselineMm);
    }

    [Fact]
    public void SubscriptThenSuperscript_AreStackedToo()
    {
        var plan = Plan(Upright("D"), Script("i", ScriptPosition.Subscript), Script("2", ScriptPosition.Superscript));

        Assert.Equal(Size, plan.Runs[1].PenMm, 6);
        Assert.Equal(Size, plan.Runs[2].PenMm, 6);
    }

    [Fact]
    public void ThirdIndexAfterStack_GoesAfterTheStack()
    {
        var plan = Plan(
            Upright("D"),
            Script("2", ScriptPosition.Superscript),
            Script("i", ScriptPosition.Subscript),
            Script("3", ScriptPosition.Superscript));

        Assert.Equal(Size + ScriptSize, plan.Runs[3].PenMm, 6);
    }

    [Fact]
    public void LineWiderThanCell_IsCompressedToFit()
    {
        // 6 символов по 5 мм = 30 мм, доступно 20 − 2 × 1 = 18 мм.
        var (plan, cell) = Plan(20, null, Upright("abcdef"));

        Assert.Equal(0.6, plan.WidthScale, 6);
        Assert.Equal(cell.Left + 1, plan.OriginMm, 6);
        Assert.True(plan.ToTableX(30) <= cell.Left + cell.Width - 1 + 1e-9);
    }

    [Fact]
    public void CompressionIsRoundedDownAndLimited()
    {
        var (plan, _) = Plan(20, null, Upright("abcdefg"));
        // 18 / 35 = 0,514… → 0,51.
        Assert.Equal(0.51, plan.WidthScale, 6);

        var (tiny, _) = Plan(3, null, Upright(new string('a', 200)));
        Assert.Equal(LinePlanner.MinWidthScale, tiny.WidthScale, 6);
    }

    [Theory]
    [InlineData(HorizontalTextAlignment.Left, 1)]
    [InlineData(HorizontalTextAlignment.Center, 45)]
    [InlineData(HorizontalTextAlignment.Right, 89)]
    public void Alignment_PlacesLineStart(HorizontalTextAlignment alignment, double origin)
    {
        var (plan, _) = Plan(100, alignment, Upright("ab"));

        Assert.Equal(origin, plan.OriginMm, 6);
    }

    [Theory]
    [InlineData(HorizontalTextAlignment.Left, 1)]
    [InlineData(HorizontalTextAlignment.Center, 50)]
    [InlineData(HorizontalTextAlignment.Right, 99)]
    public void EmptyLine_StartsByAlignment(HorizontalTextAlignment alignment, double origin)
    {
        var (plan, _) = Plan(100, alignment);

        Assert.Empty(plan.Runs);
        Assert.Equal(origin, plan.OriginMm, 6);
    }

    [Fact]
    public void ItalicDescenderLeftOfStart_IsIncludedInCentering()
    {
        var metrics = new ShiftedLeftMetrics();
        var model = TableModel.CreateEmpty(1, 1);
        model.ColumnWidths[0] = 100;
        model.SetCell(0, 0, new TableCell { Runs = { Italic("y") }, HorizontalAlignment = HorizontalTextAlignment.Center });
        var cell = TableLayout.Build(model, metrics).Texts.Single();

        var plan = LinePlanner.PlanLine(cell.Lines[0], model.Style, cell, metrics);

        // Габарит от −1 до 5: ширина 6, левый край надписи — на (100 − 6) / 2 = 47.
        Assert.Equal(47, plan.ToTableX(plan.Runs[0].BoxLeftMm), 6);
    }

    [Fact]
    public void Underlines_MergeAcrossSpaces_SplitByBaseline()
    {
        var plan = Plan(
            new TextRun("ab") { Underline = true },
            new TextRun(" ") { Underline = true, Bold = true },
            new TextRun("cd") { Underline = true },
            new TextRun("2") { Script = ScriptPosition.Superscript, Underline = true },
            Upright("x"));

        var lines = LinePlanner.Underlines(plan, FixedMetrics.Instance);

        Assert.Equal(2, lines.Count);
        Assert.Equal(plan.OriginMm, lines[0].X1Mm, 6);
        Assert.Equal(plan.OriginMm + 25, lines[0].X2Mm, 6);
        Assert.Equal(plan.Runs[0].BaselineMm + 0.2 * Size, lines[0].YMm, 6);
        Assert.True(lines[1].YMm < lines[0].YMm);
    }

    [Fact]
    public void WithWidthCorrections_ShiftsFollowingRunsAndRecenters()
    {
        var (plan, cell) = Plan(100, HorizontalTextAlignment.Center, new TextRun("ab") { Bold = true }, Upright("cd"));
        var deltas = new Dictionary<int, double> { [plan.Runs[0].PenIndex] = -2 };

        var corrected = LinePlanner.WithWidthCorrections(plan, deltas, cell);

        Assert.Equal(0, corrected.Runs[0].PenMm, 6);
        Assert.Equal(8, corrected.Runs[1].PenMm, 6);
        Assert.Equal(8, corrected.Pens[0].AdvanceMm, 6);
        // Строка стала на 2 мм уже — начало сдвигается на 1 мм вправо.
        Assert.Equal(plan.OriginMm + 1, corrected.OriginMm, 6);
    }

    [Fact]
    public void WithWidthCorrections_NoDeltas_ReturnsSamePlan()
    {
        var (plan, cell) = Plan(100, null, Upright("ab"));

        Assert.Same(plan, LinePlanner.WithWidthCorrections(plan, new Dictionary<int, double>(), cell));
    }

    /// <summary>Наклонная «y»: хвост уходит на 1 мм левее начала, буквы до 5 мм.</summary>
    private sealed class ShiftedLeftMetrics : ITextMetrics
    {
        public TextExtents Measure(TextRun format, string text, double sizeMm) => new(sizeMm, -1, text.Length * 4, text.Length * 5);

        public double UnderlineOffsetMm(TextRun format, double sizeMm) => 1;
    }
}
