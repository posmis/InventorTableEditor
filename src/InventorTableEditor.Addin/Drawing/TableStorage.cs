using Inventor;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Addin.Drawing;

/// <summary>
/// Хранение данных таблицы в атрибутах определения эскизного символа.
/// По наличию этих атрибутов аддин отличает свои таблицы от обычных символов.
/// </summary>
internal static class TableStorage
{
    private const string SetName = "InventorTableEditor";
    private const string DataName = "TableJson";

    public static bool IsTable(SketchedSymbolDefinition definition) =>
        definition.AttributeSets.get_NameIsUsed(SetName);

    public static TableModel Read(SketchedSymbolDefinition definition)
    {
        AttributeSet set = definition.AttributeSets[SetName];
        Inventor.Attribute data = set[DataName];
        return TableModel.FromJson((string)data.Value);
    }

    public static void Write(SketchedSymbolDefinition definition, TableModel model)
    {
        var json = model.ToJson();
        AttributeSets sets = definition.AttributeSets;
        AttributeSet set = sets.get_NameIsUsed(SetName) ? sets[SetName] : sets.Add(SetName);
        if (set.get_NameIsUsed(DataName))
            set[DataName].Value = json;
        else
            set.Add(DataName, ValueTypeEnum.kStringType, json);
    }

    /// <summary>Возвращает таблицу аддина среди выбранных объектов или null.</summary>
    public static SketchedSymbol? FindTable(ObjectsEnumerator? objects)
    {
        if (objects == null)
            return null;

        foreach (object item in objects)
        {
            if (item is SketchedSymbol symbol && IsTable(symbol.Definition))
                return symbol;
        }

        return null;
    }

    public static SketchedSymbol? FindTable(SelectSet selectSet)
    {
        foreach (object item in selectSet)
        {
            if (item is SketchedSymbol symbol && IsTable(symbol.Definition))
                return symbol;
        }

        return null;
    }
}
