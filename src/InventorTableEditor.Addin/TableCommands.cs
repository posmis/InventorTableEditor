using System;
using System.Windows.Interop;
using Inventor;
using InventorTableEditor.Addin.Drawing;
using InventorTableEditor.Addin.UI;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Addin;

/// <summary>
/// Команды аддина: новая таблица, редактирование выбранной, редактирование по двойному клику.
/// </summary>
internal sealed class TableCommands
{
    private const string Caption = "Редактор таблиц";

    private readonly Inventor.Application _inventor;
    private TablePlacement? _placement;

    public TableCommands(Inventor.Application inventor)
    {
        _inventor = inventor;
    }

    public void NewTable()
    {
        Run(() =>
        {
            if (_inventor.ActiveDocument is not DrawingDocument document)
            {
                ShowMessage("Откройте чертёж, чтобы вставить таблицу.");
                return;
            }

            var editor = new TableEditorWindow(TableModel.CreateEmpty(3, 3), isNew: true);
            if (!ShowDialog(editor))
                return;

            // Держим ссылку, пока идёт интерактивная вставка: иначе события соберёт GC.
            _placement = new TablePlacement(_inventor, document, editor.Model);
            _placement.Start();
        });
    }

    public void EditSelected()
    {
        Run(() =>
        {
            if (_inventor.ActiveDocument is not DrawingDocument document)
            {
                ShowMessage("Откройте чертёж с таблицей.");
                return;
            }

            var symbol = TableStorage.FindTable(document.SelectSet);
            if (symbol == null)
            {
                ShowMessage("Выберите таблицу на листе или откройте её двойным щелчком.");
                return;
            }

            Edit(document, symbol);
        });
    }

    /// <summary>Обработчик двойного щелчка: свою таблицу открываем в редакторе, остальное отдаём Inventor.</summary>
    public void OnDoubleClick(ObjectsEnumerator selectedEntities, SelectionDeviceEnum selectionDevice, MouseButtonEnum button,
        ShiftStateEnum shiftKeys, Point modelPosition, Point2d viewPosition, View view, NameValueMap additionalInfo,
        out HandlingCodeEnum handlingCode)
    {
        handlingCode = HandlingCodeEnum.kEventNotHandled;
        try
        {
            if (_inventor.ActiveDocument is not DrawingDocument document)
                return;

            var symbol = TableStorage.FindTable(selectedEntities);
            if (symbol == null)
                return;

            handlingCode = HandlingCodeEnum.kEventHandled;
            Edit(document, symbol);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void Edit(DrawingDocument document, SketchedSymbol symbol)
    {
        var definition = symbol.Definition;
        var editor = new TableEditorWindow(TableStorage.Read(definition), isNew: false);
        if (!ShowDialog(editor))
            return;

        var transaction = _inventor.TransactionManager.StartTransaction((_Document)document, "Редактирование таблицы");
        try
        {
            TableRenderer.Draw(definition, editor.Model, _inventor.TransientGeometry);
            TableStorage.Write(definition, editor.Model);
            transaction.End();
        }
        catch
        {
            transaction.Abort();
            throw;
        }

        document.ActiveSheet.Update();
    }

    private bool ShowDialog(System.Windows.Window window)
    {
        new WindowInteropHelper(window).Owner = new IntPtr(_inventor.MainFrameHWND);
        return window.ShowDialog() == true;
    }

    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private static void ShowMessage(string text) =>
        System.Windows.MessageBox.Show(text, Caption, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

    private static void ShowError(Exception ex) =>
        System.Windows.MessageBox.Show(ex.ToString(), Caption, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
}
