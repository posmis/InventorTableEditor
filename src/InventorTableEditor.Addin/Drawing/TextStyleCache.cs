using System;
using System.Collections.Generic;
using System.Globalization;
using Inventor;
using InventorTableEditor.Core;

namespace InventorTableEditor.Addin.Drawing;

/// <summary>
/// Стили текста чертежа для надписей таблиц: по одному на сочетание шрифта, размера,
/// жирного, курсива и подчёркивания. Форматированный текст Inventor (StyleOverride)
/// на практике отклоняется, поэтому форматирование задаётся стилем надписи.
/// </summary>
internal sealed class TextStyleCache
{
    private const string Prefix = "Таблица";

    private readonly DrawingDocument _document;
    private readonly Dictionary<string, TextStyle> _styles = new();

    public TextStyleCache(DrawingDocument document)
    {
        _document = document;
    }

    public TextStyle Get(string font, double sizeMm, bool bold, bool italic, bool underline)
    {
        var name = string.Create(CultureInfo.InvariantCulture,
            $"{Prefix} {font} {sizeMm:0.###}{(bold ? " Ж" : string.Empty)}{(italic ? " К" : string.Empty)}{(underline ? " Ч" : string.Empty)}");
        if (_styles.TryGetValue(name, out var cached))
            return cached;

        var style = Find(name) ?? Create(name);
        // Свойства задаются и у найденного стиля: его могли изменить вручную.
        style.Font = font;
        style.FontSize = Units.MmToCm(sizeMm);
        style.Bold = bold;
        style.Italic = italic;
        style.Underline = underline;
        _styles[name] = style;
        return style;
    }

    private TextStyle? Find(string name)
    {
        foreach (TextStyle style in _document.StylesManager.TextStyles)
        {
            if (style.Name == name)
                return style;
        }

        return null;
    }

    private TextStyle Create(string name)
    {
        var source = _document.StylesManager.ActiveStandardStyle.ActiveObjectDefaults.GeneralNoteStyle;
        var style = (TextStyle)source.Copy(name);
        AddinLog.Write("Создан стиль текста: " + name);
        return style;
    }
}
