using System;
using Inventor;
using InventorTableEditor.Core;
using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Addin.Drawing;

/// <summary>
/// Интерактивная вставка: за курсором (левым верхним углом) движется лёгкий контур таблицы,
/// щелчок создаёт таблицу в чертеже одним действием отмены, Esc отменяет вставку.
/// </summary>
internal sealed class TablePlacement
{
    private readonly Inventor.Application _inventor;
    private readonly DrawingDocument _document;
    private readonly TableModel _model;

    private InteractionEvents? _interaction;
    private MouseEvents? _mouse;
    private GraphicsNode? _previewNode;
    private bool _finished;

    public TablePlacement(Inventor.Application inventor, DrawingDocument document, TableModel model)
    {
        _inventor = inventor;
        _document = document;
        _model = model;
    }

    public void Start()
    {
        _interaction = _inventor.CommandManager.CreateInteractionEvents();
        _interaction.StatusBarText = "Укажите положение левого верхнего угла таблицы. Esc — отмена.";
        _mouse = _interaction.MouseEvents;
        _mouse.MouseMoveEnabled = true;
        _mouse.OnMouseMove += OnMouseMove;
        _mouse.OnMouseClick += OnMouseClick;
        _interaction.OnTerminate += OnTerminate;
        _interaction.Start();

        CreatePreview();
    }

    /// <summary>Контур таблицы (все линии) во временной графике, без изменения чертежа.</summary>
    private void CreatePreview()
    {
        try
        {
            var graphics = _interaction!.InteractionGraphics;
            var geometry = _inventor.TransientGeometry;
            var coordinates = graphics.GraphicsDataSets.CreateCoordinateSet(1);

            var index = 1;
            foreach (var line in TableLayout.Build(_model).Lines)
            {
                coordinates.Add(index++, geometry.CreatePoint(Units.MmToCm(line.X1), -Units.MmToCm(line.Y1), 0));
                coordinates.Add(index++, geometry.CreatePoint(Units.MmToCm(line.X2), -Units.MmToCm(line.Y2), 0));
            }

            _previewNode = graphics.OverlayClientGraphics.AddNode(1);
            var lines = _previewNode.AddLineGraphics();
            lines.CoordinateSet = coordinates;
            _previewNode.Visible = false;
        }
        catch (Exception ex)
        {
            // Без контура вставка всё равно работает — просто щелчок по листу.
            AddinLog.Write("Не удалось создать контур таблицы: " + ex.Message);
            _previewNode = null;
        }
    }

    private void OnMouseMove(MouseButtonEnum button, ShiftStateEnum shiftKeys, Point modelPosition, Point2d viewPosition, View view)
    {
        if (_previewNode == null || _interaction == null)
            return;

        var geometry = _inventor.TransientGeometry;
        var matrix = geometry.CreateMatrix();
        matrix.SetTranslation(geometry.CreateVector(modelPosition.X, modelPosition.Y, 0));
        _previewNode.Transformation = matrix;
        _previewNode.Visible = true;
        _interaction.InteractionGraphics.UpdateOverlayGraphics(view);
    }

    private void OnMouseClick(MouseButtonEnum button, ShiftStateEnum shiftKeys, Point modelPosition, Point2d viewPosition, View view)
    {
        if (button != MouseButtonEnum.kLeftMouseButton || _finished)
            return;

        _finished = true;
        var position = _inventor.TransientGeometry.CreatePoint2d(modelPosition.X, modelPosition.Y);
        _interaction?.Stop();

        try
        {
            CreateTable(position);
        }
        catch (Exception ex)
        {
            AddinLog.Write("Ошибка вставки таблицы: " + ex);
            System.Windows.MessageBox.Show("Не удалось вставить таблицу:\n\n" + ex, "Редактор таблиц",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void CreateTable(Point2d position)
    {
        var transaction = _inventor.TransactionManager.StartTransaction((_Document)_document, "Вставка таблицы");
        try
        {
            var definition = _document.SketchedSymbolDefinitions.Add(UniqueName());
            TableRenderer.Draw(definition, _model, _inventor.TransientGeometry);
            TableStorage.Write(definition, _model);
            _document.ActiveSheet.SketchedSymbols.Add(definition, position, 0, 1, Type.Missing);
            transaction.End();
        }
        catch
        {
            transaction.Abort();
            throw;
        }
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

        _previewNode = null;
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
