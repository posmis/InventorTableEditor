using System;
using Inventor;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Addin.Drawing;

/// <summary>
/// Интерактивная вставка: таблица следует за курсором (за левый верхний угол),
/// щелчок фиксирует её, Esc отменяет вставку.
/// </summary>
internal sealed class TablePlacement
{
    private readonly Inventor.Application _inventor;
    private readonly DrawingDocument _document;
    private readonly TableModel _model;

    private SketchedSymbolDefinition? _definition;
    private SketchedSymbol? _symbol;
    private InteractionEvents? _interaction;
    private MouseEvents? _mouse;
    private bool _placed;

    public TablePlacement(Inventor.Application inventor, DrawingDocument document, TableModel model)
    {
        _inventor = inventor;
        _document = document;
        _model = model;
    }

    public void Start()
    {
        var transaction = _inventor.TransactionManager.StartTransaction((_Document)_document, "Создание таблицы");
        try
        {
            _definition = _document.SketchedSymbolDefinitions.Add(UniqueName());
            TableRenderer.Draw(_definition, _model, _inventor.TransientGeometry);
            TableStorage.Write(_definition, _model);
            transaction.End();
        }
        catch
        {
            transaction.Abort();
            throw;
        }

        _interaction = _inventor.CommandManager.CreateInteractionEvents();
        _interaction.StatusBarText = "Укажите положение левого верхнего угла таблицы. Esc — отмена.";
        _mouse = _interaction.MouseEvents;
        _mouse.MouseMoveEnabled = true;
        _mouse.OnMouseMove += OnMouseMove;
        _mouse.OnMouseClick += OnMouseClick;
        _interaction.OnTerminate += OnTerminate;
        _interaction.Start();
    }

    private void OnMouseMove(MouseButtonEnum button, ShiftStateEnum shiftKeys, Point modelPosition, Point2d viewPosition, View view)
    {
        MoveTo(modelPosition);
    }

    private void OnMouseClick(MouseButtonEnum button, ShiftStateEnum shiftKeys, Point modelPosition, Point2d viewPosition, View view)
    {
        if (button != MouseButtonEnum.kLeftMouseButton)
            return;

        MoveTo(modelPosition);
        _placed = _symbol != null;
        _interaction?.Stop();
    }

    private void MoveTo(Point modelPosition)
    {
        var position = _inventor.TransientGeometry.CreatePoint2d(modelPosition.X, modelPosition.Y);
        if (_symbol == null)
            _symbol = _document.ActiveSheet.SketchedSymbols.Add(_definition, position, 0, 1, Type.Missing);
        else
            _symbol.Position = position;
    }

    private void OnTerminate()
    {
        if (_mouse != null)
        {
            _mouse.OnMouseMove -= OnMouseMove;
            _mouse.OnMouseClick -= OnMouseClick;
        }

        if (_interaction != null)
            _interaction.OnTerminate -= OnTerminate;

        if (!_placed)
        {
            try
            {
                _symbol?.Delete();
                _definition?.Delete();
            }
            catch (Exception)
            {
                // Отмена не должна ронять Inventor; лишнее определение можно удалить вручную.
            }
        }

        _mouse = null;
        _interaction = null;
    }

    private string UniqueName()
    {
        var used = new System.Collections.Generic.HashSet<string>();
        foreach (SketchedSymbolDefinition definition in _document.SketchedSymbolDefinitions)
            used.Add(definition.Name);

        for (var i = 1; ; i++)
        {
            var name = $"Таблица {i}";
            if (!used.Contains(name))
                return name;
        }
    }
}
