using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Templates;

/// <summary>
/// Шаблоны в комплекте: таблицы параметров по ЕСКД. Три столбца — наименование, обозначение, значение;
/// значения заполняет пользователь. Ширина 110 мм, высота строк 8 мм, шрифт 3,5 мм.
/// </summary>
public static class BuiltInTemplates
{
    public const string Sprocket = "Звёздочка (ГОСТ 2.421)";
    public const string Gear = "Зубчатое колесо (ГОСТ 2.403)";

    public static IReadOnlyList<string> Names { get; } = new[] { Sprocket, Gear };

    public static TableModel Create(string name) => name switch
    {
        Sprocket => Build(new[]
        {
            Row("Число зубьев", ("z", null)),
            Row("Сопрягаемая цепь", ("—", null)),
            Row("Шаг цепи", ("t", null)),
            Row("Диаметр ролика цепи", ("d", "1")),
            Row("Профиль зуба по ГОСТ 591—69", ("—", null)),
            Row("Группа точности", ("—", null)),
            Row("Диаметр окружности впадин", ("D", "i")),
            Row("Наибольшая хорда", ("L", "x")),
            Row("Диаметр делительной окружности", ("d", "д")),
        }),
        Gear => Build(new[]
        {
            Row("Модуль", ("m", null)),
            Row("Число зубьев", ("z", null)),
            Row("Угол наклона зуба", ("β", null)),
            Row("Направление линии зуба", ("—", null)),
            Row("Нормальный исходный контур", ("—", null), "ГОСТ 13755—2015"),
            Row("Коэффициент смещения", ("x", null)),
            Row("Степень точности по ГОСТ 1643—81", ("—", null)),
            Row("Делительный диаметр", ("d", null)),
        }),
        _ => throw new ArgumentException($"Нет встроенного шаблона «{name}».", nameof(name)),
    };

    private static (string Name, (string Symbol, string? Index) Designation, string Value) Row(
        string name, (string Symbol, string? Index) designation, string value = "") => (name, designation, value);

    private static TableModel Build(IReadOnlyList<(string Name, (string Symbol, string? Index) Designation, string Value)> rows)
    {
        var style = new TableStyle { FontSizeMm = 3.5, DefaultRowHeightMm = 8 };
        var model = TableModel.CreateEmpty(rows.Count, 3, style);
        model.ColumnWidths[0] = 70;
        model.ColumnWidths[1] = 15;
        model.ColumnWidths[2] = 25;

        for (var r = 0; r < rows.Count; r++)
        {
            var (name, (symbol, index), value) = rows[r];
            model.SetText(r, 0, name);

            var designation = new TableCell { HorizontalAlignment = HorizontalTextAlignment.Center };
            designation.Runs.Add(new TextRun(symbol, style.Italic));
            if (index != null)
                designation.Runs.Add(new TextRun(index, style.Italic) { Script = ScriptPosition.Subscript });
            model.SetCell(r, 1, designation);

            var cell = TableCell.FromText(value, style.Italic);
            cell.HorizontalAlignment = HorizontalTextAlignment.Center;
            model.SetCell(r, 2, cell);
            // Длинные наименования переносятся (после сжатия до предела), как принято в таблицах параметров.
            model.GetCell(r, 0).Wrap = true;
        }

        return model;
    }
}
