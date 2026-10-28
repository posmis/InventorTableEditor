using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using InventorTableEditor.Core.Clipboard;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Addin.UI;

/// <summary>
/// Буфер обмена Windows для таблицы: кладёт одновременно текст с табуляциями (его понимает Excel и
/// Блокнот) и HTML (Word и Excel сохраняют форматирование и индексы). При вставке HTML важнее текста.
/// Разбор и сборка данных — <see cref="TableClipboard"/>.
/// </summary>
internal static class ClipboardData
{
    /// <summary>Сколько раз пробовать открыть буфер: его на мгновение может держать другая программа.</summary>
    private const int Attempts = 5;

    /// <summary>Ячейки прямоугольника таблицы.</summary>
    public static bool SetCells(TableModel model, CellRange range) =>
        Set(TableClipboard.ToTsv(model, range), TableClipboard.ToHtmlTable(model, range));

    /// <summary>Выделенный текст одной ячейки с форматом.</summary>
    public static bool SetRuns(IReadOnlyList<TextRun> runs) =>
        Set(string.Concat(runs.Select(r => r.Text)).Replace("\n", Environment.NewLine), TableClipboard.ToHtml(runs));

    /// <summary>Есть ли что вставить (для пункта «Вставить» контекстного меню).</summary>
    public static bool HasData() => Try(() => Clipboard.ContainsText() || Clipboard.ContainsData(DataFormats.Html));

    /// <summary>
    /// Содержимое буфера как ячейки: HTML-таблица по ячейкам, текст Excel по табуляциям и строкам.
    /// null — в буфере нет текста.
    /// </summary>
    public static List<List<TableCell>>? GetCells(bool italic)
    {
        string? html = null;
        string? text = null;
        if (!Try(() =>
            {
                html = Clipboard.ContainsData(DataFormats.Html) ? Clipboard.GetData(DataFormats.Html) as string : null;
                text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
                return true;
            }))
            return null;

        if (!string.IsNullOrEmpty(html))
        {
            var cells = TableClipboard.FromHtml(html, italic);
            // HTML без текста (картинка, непонятная разметка) — берётся простой текст.
            if (cells.Any(row => row.Any(cell => !cell.IsEmpty)) || string.IsNullOrWhiteSpace(text))
                return cells;
        }

        return string.IsNullOrEmpty(text) ? null : TableClipboard.FromTsv(text, italic);
    }

    private static bool Set(string text, string htmlFragment)
    {
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        data.SetData(DataFormats.Html, TableClipboard.ToClipboardHtml(htmlFragment));
        return Try(() =>
        {
            Clipboard.SetDataObject(data, copy: true);
            return true;
        });
    }

    private static bool Try(Func<bool> action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (COMException ex) when (attempt < Attempts)
            {
                AddinLog.Write($"Буфер обмена занят, попытка {attempt}: {ex.Message}");
                System.Threading.Thread.Sleep(50);
            }
            catch (ExternalException ex)
            {
                AddinLog.Write($"Буфер обмена недоступен: {ex.Message}");
                return false;
            }
        }
    }
}
