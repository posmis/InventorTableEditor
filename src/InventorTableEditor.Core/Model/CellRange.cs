namespace InventorTableEditor.Core.Model;

/// <summary>Прямоугольник выделенных ячеек, как в Excel: строки Top..Bottom, столбцы Left..Right включительно.</summary>
public readonly record struct CellRange(int Top, int Left, int Bottom, int Right)
{
    /// <summary>Прямоугольник между двумя ячейками в любом порядке (начало выделения и текущая ячейка).</summary>
    public static CellRange Between((int Row, int Column) from, (int Row, int Column) to) =>
        new(Math.Min(from.Row, to.Row), Math.Min(from.Column, to.Column), Math.Max(from.Row, to.Row), Math.Max(from.Column, to.Column));

    public bool IsSingleCell => Top == Bottom && Left == Right;

    public int Count => (Bottom - Top + 1) * (Right - Left + 1);

    public bool Contains(int row, int column) => row >= Top && row <= Bottom && column >= Left && column <= Right;

    /// <summary>Ячейки по строкам слева направо.</summary>
    public IEnumerable<(int Row, int Column)> Cells()
    {
        for (var row = Top; row <= Bottom; row++)
        {
            for (var column = Left; column <= Right; column++)
                yield return (row, column);
        }
    }
}
