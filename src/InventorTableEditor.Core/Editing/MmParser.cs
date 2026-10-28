using System.Globalization;

namespace InventorTableEditor.Core.Editing;

/// <summary>Разбор размеров в мм из полей ввода: и с запятой, и с точкой.</summary>
public static class MmParser
{
    /// <summary>Наименьшая ширина столбца и высота строки, мм.</summary>
    public const double MinCellSize = 2;

    /// <summary>Наибольшая ширина столбца и высота строки, мм.</summary>
    public const double MaxCellSize = 1000;

    public static bool TryParse(string? text, double min, double max, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               && double.IsFinite(value) && value >= min && value <= max;
    }

    public static bool TryParseCellSize(string? text, out double value) => TryParse(text, MinCellSize, MaxCellSize, out value);

    /// <summary>Размер для поля ввода: до сотых, без лишних нулей, с десятичным разделителем Windows.</summary>
    public static string Format(double value, CultureInfo? culture = null) =>
        value.ToString("0.##", culture ?? CultureInfo.CurrentCulture);
}
