namespace InventorTableEditor.Core.Editing;

/// <summary>Масштаб окна редактора, как в Excel: 25–400 %, шаг 10 %.</summary>
public static class Zoom
{
    public const double Min = 0.25;
    public const double Max = 4.0;
    public const double Step = 0.1;

    /// <summary>Масштаб в допустимых пределах, округлённый до процента.</summary>
    public static double Clamp(double zoom) => double.IsFinite(zoom) ? Math.Round(Math.Clamp(zoom, Min, Max), 2) : 1.0;

    /// <summary>Следующий масштаб при прокрутке колеса: вверх — крупнее, вниз — мельче.</summary>
    public static double Next(double zoom, int wheelDelta) => Clamp(zoom + Math.Sign(wheelDelta) * Step);
}
