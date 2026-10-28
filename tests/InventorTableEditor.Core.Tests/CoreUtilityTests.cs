using System.Globalization;
using InventorTableEditor.Core.Editing;
using InventorTableEditor.Core.IO;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Tests;

public class CoreUtilityTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ite-utility-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    // ---------- Запись файла ----------

    [Fact]
    public void AtomicFile_CreatesFoldersWritesUtf8WithoutBomAndLeavesNoTemp()
    {
        var path = Path.Combine(_directory, "a", "b", "file.json");

        AtomicFile.WriteAllText(path, "Ø ±");

        var bytes = File.ReadAllBytes(path);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal("Ø ±", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void AtomicFile_ReplacesExistingFile()
    {
        var path = Path.Combine(_directory, "file.txt");
        AtomicFile.WriteAllText(path, "long old content");

        AtomicFile.WriteAllText(path, "new");

        Assert.Equal("new", File.ReadAllText(path));
    }

    // ---------- Единицы, размеры, масштаб ----------

    [Theory]
    [InlineData(10, 1)]
    [InlineData(2.5, 0.25)]
    [InlineData(0, 0)]
    public void Units_RoundTrip(double mm, double cm)
    {
        Assert.Equal(cm, Units.MmToCm(mm), 9);
        Assert.Equal(mm, Units.CmToMm(cm), 9);
    }

    [Theory]
    [InlineData("1e1", 10)]
    [InlineData(" 7,25 ", 7.25)]
    public void MmParser_TryParse_AcceptsFloatForms(string text, double expected)
    {
        Assert.True(MmParser.TryParse(text, 0, 100, out var value));
        Assert.Equal(expected, value, 9);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1,2,3")]
    [InlineData("101")]
    [InlineData("-1")]
    public void MmParser_TryParse_RejectsOutOfRangeAndGarbage(string text)
    {
        Assert.False(MmParser.TryParse(text, 0, 100, out _));
    }

    [Fact]
    public void MmParser_Format_InvariantCulture()
    {
        Assert.Equal("12.35", MmParser.Format(12.345, CultureInfo.InvariantCulture));
        Assert.Equal("20", MmParser.Format(20.0, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(5, -10, 2)]
    [InlineData(999, 5, 1000)]
    [InlineData(10, 0.49, 10)]
    [InlineData(10, 0.5, 11)]
    public void SnapToMillimetre_ClampsAndRounds(double start, double delta, double expected)
    {
        Assert.Equal(expected, MmParser.SnapToMillimetre(start, delta));
    }

    [Fact]
    public void Zoom_NextFromLimitsStaysInRange()
    {
        Assert.Equal(Zoom.Max, Zoom.Next(Zoom.Max, +120));
        Assert.Equal(Zoom.Min, Zoom.Next(Zoom.Min, -120));
        Assert.Equal(1.0, Zoom.Clamp(double.NaN));
    }

    // ---------- Прямоугольник ячеек ----------

    [Fact]
    public void CellRange_CountAndSingleCell()
    {
        Assert.Equal(6, new CellRange(1, 1, 2, 3).Count);
        Assert.True(new CellRange(4, 2, 4, 2).IsSingleCell);
        Assert.False(new CellRange(4, 2, 4, 3).IsSingleCell);
        Assert.Single(new CellRange(4, 2, 4, 2).Cells());
    }

    [Fact]
    public void CellRange_ContainsOnlyInside()
    {
        var range = new CellRange(1, 1, 2, 2);

        Assert.False(range.Contains(0, 1));
        Assert.False(range.Contains(3, 1));
        Assert.False(range.Contains(1, 0));
        Assert.False(range.Contains(1, 3));
        Assert.True(range.Contains(2, 2));
    }

    // ---------- Оформление ----------

    [Fact]
    public void TableStyle_SameAs_ComparesEveryValue()
    {
        var style = new TableStyle();

        Assert.True(style.SameAs(style.Clone()));
        var changed = style.Clone();
        changed.SubscriptDrop = 0.3;
        Assert.False(style.SameAs(changed));
        changed = style.Clone();
        changed.VerticalAlignment = VerticalTextAlignment.Bottom;
        Assert.False(style.SameAs(changed));
    }

    [Fact]
    public void TableStyle_CheckMessagesNameLimitsInPercent()
    {
        var errors = new TableStyle { ScriptScale = 2 }.Check();

        Assert.Equal("Размер индекса: от 30 до 100 %.", errors.Single());
    }
}
