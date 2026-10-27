using InventorTableEditor.Core;

namespace InventorTableEditor.Core.Tests;

public class UnitsTests
{
    [Theory]
    [InlineData(5.0, 0.5)]
    [InlineData(20.0, 2.0)]
    [InlineData(0.25, 0.025)]
    public void MmToCm_ConvertsToInventorUnits(double mm, double cm)
    {
        Assert.Equal(cm, Units.MmToCm(mm), 10);
        Assert.Equal(mm, Units.CmToMm(cm), 10);
    }
}
