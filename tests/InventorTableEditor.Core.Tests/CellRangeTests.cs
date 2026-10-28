using InventorTableEditor.Core.Editing;

namespace InventorTableEditor.Core.Tests;

public class CellRangeTests
{
    [Fact]
    public void Between_NormalizesCorners()
    {
        var range = CellRange.Between((3, 1), (1, 2));

        Assert.Equal(new CellRange(1, 1, 3, 2), range);
        Assert.Equal(6, range.Count);
        Assert.False(range.IsSingleCell);
    }

    [Fact]
    public void Cells_GoRowByRow()
    {
        var cells = CellRange.Between((0, 0), (1, 1)).Cells().ToArray();

        Assert.Equal(new[] { (0, 0), (0, 1), (1, 0), (1, 1) }, cells);
    }

    [Fact]
    public void Contains_IsInclusive()
    {
        var range = new CellRange(1, 1, 2, 3);

        Assert.True(range.Contains(1, 1));
        Assert.True(range.Contains(2, 3));
        Assert.False(range.Contains(0, 1));
        Assert.False(range.Contains(2, 4));
        Assert.True(CellRange.Between((5, 5), (5, 5)).IsSingleCell);
    }
}
