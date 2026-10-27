namespace InventorTableEditor.Core;

/// <summary>
/// Перевод единиц. Пользователь работает в миллиметрах, API Inventor — в сантиметрах.
/// </summary>
public static class Units
{
    public static double MmToCm(double mm) => mm / 10.0;

    public static double CmToMm(double cm) => cm * 10.0;
}
