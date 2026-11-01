using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using InventorTableEditor.Core.Layout;
using LineSegment = InventorTableEditor.Core.Layout.LineSegment;

namespace InventorTableEditor.Addin.UI;

/// <summary>
/// Линии таблицы и фон строки заголовков одним слоем под ячейками. Рамки дробной толщины у каждой
/// ячейки размывались противоречиво и сливались с белым фоном; эти линии привязаны к пикселям
/// устройства (с учётом зума и DPI) и остаются чёткими при любом масштабе, с сохранением
/// соотношения толщин: разделители — от 1 пикселя, рамка и линия под заголовками — толще.
/// Слой под ячейками: в чертеже текст выводится поверх линий (TableRenderer), здесь так же.
/// </summary>
internal sealed class GridLinesOverlay : FrameworkElement
{
    private readonly double _pxPerMm;
    private IReadOnlyList<LineSegment>? _lines;
    private IReadOnlyList<(double TopMm, double BottomMm)> _headerRows = Array.Empty<(double, double)>();
    private Brush? _headerBackground;
    private double _tableWidthMm;

    public GridLinesOverlay(double pxPerMm)
    {
        _pxPerMm = pxPerMm;
        IsHitTestVisible = false;
        Focusable = false;
    }

    public IReadOnlyList<LineSegment>? Lines
    {
        get => _lines;
        set
        {
            _lines = value;
            InvalidateVisual();
        }
    }

    /// <summary>Интервалы строк заголовков, мм от верха таблицы — заливаются общим фоном.</summary>
    public IReadOnlyList<(double TopMm, double BottomMm)> HeaderRows
    {
        get => _headerRows;
        set
        {
            _headerRows = value;
            InvalidateVisual();
        }
    }

    public Brush? HeaderBackground
    {
        get => _headerBackground;
        set
        {
            _headerBackground = value;
            InvalidateVisual();
        }
    }

    public double TableWidthMm
    {
        get => _tableWidthMm;
        set
        {
            _tableWidthMm = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext context)
    {
        if (_lines == null || _tableWidthMm <= 0)
            return;
        var toDevice = PresentationSource.FromVisual(this)?.CompositionTarget.TransformToDevice;
        if (toDevice == null || toDevice.Value.M11 <= 0 || toDevice.Value.M22 <= 0)
            return;
        var sx = toDevice.Value.M11;
        var sy = toDevice.Value.M22;

        if (_headerBackground != null)
            foreach (var (topMm, bottomMm) in _headerRows)
                context.DrawRectangle(_headerBackground, null,
                    new Rect(0, topMm * _pxPerMm, _tableWidthMm * _pxPerMm, (bottomMm - topMm) * _pxPerMm));

        foreach (var line in _lines)
        {
            if (line.X1 == line.X2)
            {
                // Вертикальная: толщина — по X, центр на линии; торцы — до целых пикселей по Y,
                // чтобы на стыках с горизонтальными не было щелей.
                var t = Math.Max(1.0, Math.Round(line.WeightMm * _pxPerMm * sx, MidpointRounding.AwayFromZero));
                var left = Math.Round(line.X1 * _pxPerMm * sx - t / 2, MidpointRounding.AwayFromZero) / sx;
                var d1 = Math.Min(line.Y1, line.Y2) * _pxPerMm * sy;
                var d2 = Math.Max(line.Y1, line.Y2) * _pxPerMm * sy;
                context.DrawRectangle(Brushes.Black, null,
                    new Rect(left, Math.Floor(d1) / sy, t / sx, (Math.Ceiling(d2) - Math.Floor(d1)) / sy));
            }
            else
            {
                var t = Math.Max(1.0, Math.Round(line.WeightMm * _pxPerMm * sy, MidpointRounding.AwayFromZero));
                var top = Math.Round(line.Y1 * _pxPerMm * sy - t / 2, MidpointRounding.AwayFromZero) / sy;
                var d1 = Math.Min(line.X1, line.X2) * _pxPerMm * sx;
                var d2 = Math.Max(line.X1, line.X2) * _pxPerMm * sx;
                context.DrawRectangle(Brushes.Black, null,
                    new Rect(Math.Floor(d1) / sx, top, (Math.Ceiling(d2) - Math.Floor(d1)) / sx, t / sy));
            }
        }
    }
}
