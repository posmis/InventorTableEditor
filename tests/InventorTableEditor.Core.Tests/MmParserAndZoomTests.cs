using System.Globalization;
using InventorTableEditor.Core.Editing;

namespace InventorTableEditor.Core.Tests;

public class MmParserAndZoomTests
{
    [Theory]
    [InlineData("20", 20)]
    [InlineData("12,5", 12.5)]
    [InlineData("12.5", 12.5)]
    [InlineData(" 8 ", 8)]
    [InlineData("2", 2)]
    [InlineData("1000", 1000)]
    public void TryParseCellSize_Accepts(string text, double expected)
    {
        Assert.True(MmParser.TryParseCellSize(text, out var value));
        Assert.Equal(expected, value, 6);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1,9")]
    [InlineData("1001")]
    [InlineData("-5")]
    [InlineData("NaN")]
    [InlineData("1e400")]
    public void TryParseCellSize_Rejects(string? text)
    {
        Assert.False(MmParser.TryParseCellSize(text, out _));
    }

    [Fact]
    public void Format_UsesCultureAndTrimsZeros()
    {
        Assert.Equal("12,5", MmParser.Format(12.5, CultureInfo.GetCultureInfo("ru-RU")));
        Assert.Equal("20", MmParser.Format(20.0, CultureInfo.InvariantCulture));
        Assert.Equal("0.33", MmParser.Format(1.0 / 3, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(0.1, Zoom.Min)]
    [InlineData(10, Zoom.Max)]
    [InlineData(1.234, 1.23)]
    [InlineData(double.NaN, 1.0)]
    public void Zoom_Clamp(double zoom, double expected)
    {
        Assert.Equal(expected, Zoom.Clamp(zoom), 6);
    }

    [Theory]
    [InlineData(1.0, 120, 1.1)]
    [InlineData(1.0, -120, 0.9)]
    [InlineData(Zoom.Max, 120, Zoom.Max)]
    [InlineData(Zoom.Min, -120, Zoom.Min)]
    [InlineData(1.5, 0, 1.5)]
    public void Zoom_NextByWheel(double zoom, int delta, double expected)
    {
        Assert.Equal(expected, Zoom.Next(zoom, delta), 6);
    }
}
