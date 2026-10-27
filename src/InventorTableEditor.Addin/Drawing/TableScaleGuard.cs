using System;
using System.Collections.Generic;
using Inventor;

namespace InventorTableEditor.Addin.Drawing;

/// <summary>
/// Запрет масштабирования таблиц: после любого изменения чертежа таблицам аддина
/// с масштабом, отличным от 1, возвращается масштаб 1. Размеры таблицы задаются только в редакторе.
/// </summary>
internal sealed class TableScaleGuard
{
    private const double Tolerance = 1e-9;

    private readonly Dictionary<string, (DocumentEvents Events, DocumentEventsSink_OnChangeEventHandler Handler)> _subscriptions = new();
    private bool _fixing;

    public void Attach(DrawingDocument document)
    {
        var key = document.InternalName;
        if (_subscriptions.ContainsKey(key))
            return;

        var events = document.DocumentEvents;
        DocumentEventsSink_OnChangeEventHandler handler =
            (CommandTypesEnum reasons, EventTimingEnum timing, NameValueMap context, out HandlingCodeEnum handlingCode) =>
            {
                handlingCode = HandlingCodeEnum.kEventNotHandled;
                if (timing == EventTimingEnum.kAfter)
                    ResetScale(document);
            };
        events.OnChange += handler;
        _subscriptions[key] = (events, handler);
        AddinLog.Write("Контроль масштаба подключён: " + document.DisplayName);
    }

    public void DetachAll()
    {
        foreach (var (events, handler) in _subscriptions.Values)
        {
            try
            {
                events.OnChange -= handler;
            }
            catch (Exception)
            {
                // Документ уже закрыт.
            }
        }

        _subscriptions.Clear();
    }

    private void ResetScale(DrawingDocument document)
    {
        if (_fixing)
            return;

        _fixing = true;
        try
        {
            foreach (Sheet sheet in document.Sheets)
            {
                foreach (SketchedSymbol symbol in sheet.SketchedSymbols)
                {
                    if (Math.Abs(symbol.Scale - 1.0) > Tolerance && TableStorage.IsTable(symbol.Definition))
                    {
                        AddinLog.Write($"Масштаб таблицы {symbol.Definition.Name} {symbol.Scale} -> 1");
                        symbol.Scale = 1.0;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AddinLog.Write("Не удалось вернуть масштаб таблицы: " + ex.Message);
        }
        finally
        {
            _fixing = false;
        }
    }
}
