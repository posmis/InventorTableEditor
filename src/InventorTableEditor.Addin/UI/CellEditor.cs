using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using InventorTableEditor.Addin.Drawing;
using InventorTableEditor.Core.Editing;
using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Addin.UI;

/// <summary>
/// Редактор текста ячейки, который и при наборе рисует текст ровно как на чертеже: расстановка
/// <see cref="LinePlanner"/>, индексы, наклон как в Inventor, перенос и сжатие по ширине.
/// Курсор и выделение ставятся по той же расстановке. Правка текста — <see cref="CellTextBuffer"/>.
/// </summary>
internal sealed class CellEditor : FrameworkElement
{
    private readonly TableStyle _style;
    private readonly double _pxPerMm;
    private readonly Brush _background;
    private readonly Pen _focusPen;
    private readonly DispatcherTimer _blink = new() { Interval = TimeSpan.FromMilliseconds(530) };
    /// <summary>Толщина обводки жирного текста в долях кегля.</summary>
    private const double BoldStroke = 0.035;

    private readonly ITextMetrics _metrics;
    private CellText? _layout;
    private CaretMap? _caretMap;
    private bool _caretVisible;
    private bool _selecting;

    public CellEditor(TableCell cell, TableStyle style, double pxPerMm, Brush background, Brush accent)
    {
        _style = style;
        _pxPerMm = pxPerMm;
        _background = background;
        _focusPen = new Pen(accent.CloneCurrentValue(), 2);
        _focusPen.Freeze();
        _metrics = new WpfTextMetrics(style);
        Buffer = new CellTextBuffer(cell, style);
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.IBeam;
        ClipToBounds = true;
        _blink.Tick += (_, _) =>
        {
            _caretVisible = !_caretVisible;
            InvalidateVisual();
        };
    }

    public CellTextBuffer Buffer { get; }

    /// <summary>Текст изменился — нужно обновить модель и раскладку таблицы.</summary>
    public event Action<CellEditor>? ContentChanged;

    /// <summary>Сдвинулся курсор или изменился формат набираемого текста — обновить панель.</summary>
    public event Action<CellEditor>? StateChanged;

    /// <summary>Переход в соседнюю ячейку: строка и столбец относительно текущей.</summary>
    public event Action<CellEditor, int, int>? NavigateRequested;

    /// <summary>Ячейка из раскладки таблицы (мм от угла таблицы) — с пустыми ячейками.</summary>
    public CellText? Layout
    {
        get => _layout;
        set
        {
            _layout = value;
            _caretMap = value == null ? null : CaretMap.Build(Buffer.ToCell(new TableCell()), value, _style, _metrics);
            InvalidateVisual();
        }
    }

    /// <summary>Ставит курсор в конец текста (при переходе в ячейку с клавиатуры).</summary>
    public void FocusAtEnd()
    {
        Buffer.MoveTo(Buffer.Length, extend: false);
        Focus();
    }

    /// <summary>Применяет изменение формата или текста из панели инструментов.</summary>
    public void Edit(Action<CellTextBuffer> action)
    {
        var text = Buffer.Text;
        var cell = Buffer.ToCell(new TableCell());
        action(Buffer);
        AfterChange(contentChanged: text != Buffer.Text || !SameRuns(cell, Buffer.ToCell(new TableCell())));
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        RestartBlink();
        StateChanged?.Invoke(this);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        _blink.Stop();
        _caretVisible = false;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var index = HitTest(e.GetPosition(this));
        if (e.ClickCount == 2)
        {
            Buffer.MoveTo(Buffer.WordLeft(Math.Min(index + 1, Buffer.Length)), extend: false);
            Buffer.MoveTo(Buffer.WordRight(index), extend: true);
        }
        else
        {
            Buffer.MoveTo(index, extend: (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            _selecting = CaptureMouse();
        }

        AfterChange(contentChanged: false);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_selecting)
            return;

        Buffer.MoveTo(HitTest(e.GetPosition(this)), extend: true);
        AfterChange(contentChanged: false);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_selecting)
        {
            _selecting = false;
            ReleaseMouseCapture();
        }
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        var text = new string(e.Text.Where(ch => !char.IsControl(ch)).ToArray());
        if (text.Length == 0)
            return;

        Buffer.Insert(text);
        AfterChange(contentChanged: true);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        e.Handled = ctrl ? HandleShortcut(key, shift) : HandleKey(key, shift);
    }

    private bool HandleKey(Key key, bool shift)
    {
        var contentChanged = false;
        switch (key)
        {
            case Key.Left:
                Buffer.MoveTo(!shift && Buffer.HasSelection ? Buffer.SelectionStart : Buffer.Caret - 1, shift);
                break;
            case Key.Right:
                Buffer.MoveTo(!shift && Buffer.HasSelection ? Buffer.SelectionEnd : Buffer.Caret + 1, shift);
                break;
            case Key.Up:
            case Key.Down:
                var target = _caretMap?.VerticalMove(Buffer.Caret, key == Key.Up ? -1 : 1);
                if (target == null)
                {
                    NavigateRequested?.Invoke(this, key == Key.Up ? -1 : 1, 0);
                    return true;
                }

                Buffer.MoveTo(target.Value, shift);
                break;
            case Key.Home:
                Buffer.MoveTo(_caretMap?.LineOf(Buffer.Caret)?.Start ?? 0, shift);
                break;
            case Key.End:
                Buffer.MoveTo(_caretMap?.LineOf(Buffer.Caret)?.End ?? Buffer.Length, shift);
                break;
            case Key.Back:
                Buffer.Backspace();
                contentChanged = true;
                break;
            case Key.Delete:
                Buffer.Delete();
                contentChanged = true;
                break;
            case Key.Enter:
                Buffer.Insert("\n");
                contentChanged = true;
                break;
            case Key.Tab:
                NavigateRequested?.Invoke(this, 0, shift ? -1 : 1);
                return true;
            default:
                return false;
        }

        AfterChange(contentChanged);
        return true;
    }

    /// <summary>Сочетания с Ctrl, как в Word.</summary>
    private bool HandleShortcut(Key key, bool shift)
    {
        var contentChanged = true;
        switch (key)
        {
            case Key.B:
                Buffer.ToggleBold();
                break;
            case Key.I:
                Buffer.ToggleItalic();
                break;
            case Key.U:
                Buffer.ToggleUnderline();
                break;
            case Key.OemPlus:
                Buffer.ToggleScript(shift ? ScriptPosition.Superscript : ScriptPosition.Subscript);
                break;
            case Key.Space:
                Buffer.ResetScript();
                break;
            case Key.Z:
                Buffer.Undo();
                break;
            case Key.Y:
                Buffer.Redo();
                break;
            case Key.A:
                Buffer.SelectAll();
                contentChanged = false;
                break;
            case Key.C:
                CopySelection();
                contentChanged = false;
                break;
            case Key.X:
                CopySelection();
                Buffer.DeleteSelection();
                break;
            case Key.V:
                if (Clipboard.ContainsText())
                    Buffer.Insert(Clipboard.GetText());
                break;
            case Key.Left:
                Buffer.MoveTo(Buffer.WordLeft(Buffer.Caret), shift);
                contentChanged = false;
                break;
            case Key.Right:
                Buffer.MoveTo(Buffer.WordRight(Buffer.Caret), shift);
                contentChanged = false;
                break;
            case Key.Home:
                Buffer.MoveTo(0, shift);
                contentChanged = false;
                break;
            case Key.End:
                Buffer.MoveTo(Buffer.Length, shift);
                contentChanged = false;
                break;
            default:
                return false;
        }

        AfterChange(contentChanged);
        return true;
    }

    private void CopySelection()
    {
        if (Buffer.HasSelection)
            Clipboard.SetText(Buffer.SelectedText.Replace("\n", Environment.NewLine));
    }

    private void AfterChange(bool contentChanged)
    {
        RestartBlink();
        if (contentChanged)
            ContentChanged?.Invoke(this);
        else
            InvalidateVisual();
        StateChanged?.Invoke(this);
    }

    private void RestartBlink()
    {
        _caretVisible = IsKeyboardFocused;
        _blink.Stop();
        if (IsKeyboardFocused)
            _blink.Start();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        context.DrawRectangle(_background, null, new Rect(RenderSize));
        if (_layout != null)
        {
            DrawSelection(context);
            DrawText(context);
            if (_caretVisible && IsKeyboardFocused)
                DrawCaret(context);
        }

        // Текущая ячейка — синей рамкой, как в Excel.
        if (IsKeyboardFocused && RenderSize.Width > 2 && RenderSize.Height > 2)
            context.DrawRectangle(null, _focusPen, new Rect(1, 1, RenderSize.Width - 2, RenderSize.Height - 2));
    }

    private void DrawText(DrawingContext context)
    {
        if (_caretMap == null)
            return;

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var line in _caretMap.Lines)
        {
            var plan = line.Plan;
            foreach (var item in plan.Runs)
            {
                var run = item.Run;
                var fontName = run.FontName ?? _style.FontName;
                var emPerCap = FontMetrics.Get(fontName)?.EmPerCapHeight ?? EditorFonts.EmPerTextHeight;
                // Всегда обычное начертание: жирное WPF у шрифтов без своего жирного расширяет буквы,
                // и курсор (он ставится по ширинам обычных букв, как в чертеже) оказывался внутри букв.
                var typeface = new Typeface(EditorFonts.FontFor(fontName), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                var emSize = item.SizeMm * emPerCap * _pxPerMm;
                var text = new FormattedText(run.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface,
                    emSize, Brushes.Black, pixelsPerDip);

                // Начало фрагмента на базовой линии; сжатие по ширине и наклон — относительно него.
                var x = X(plan.ToTableX(item.PenMm));
                var baseline = Y(item.BaselineMm);
                var slant = run.Italic ? FontMetrics.ItalicSlant : 0.0;
                var scale = plan.WidthScale;

                context.PushTransform(new MatrixTransform(scale, 0, -slant * scale, 1, x, baseline));
                if (run.Bold)
                {
                    // Жирный — утолщённый контур тех же букв: ширина букв не меняется.
                    var outline = text.BuildGeometry(new Point(0, -text.Baseline));
                    context.DrawGeometry(Brushes.Black, new Pen(Brushes.Black, emSize * BoldStroke), outline);
                }
                else
                {
                    context.DrawText(text, new Point(0, -text.Baseline));
                }

                context.Pop();
            }

            // Подчёркивание — линиями заданной толщины, как в чертеже.
            var underlinePen = new Pen(Brushes.Black, Math.Max(1, _style.UnderlineMm * _pxPerMm));
            foreach (var underline in LinePlanner.Underlines(plan, _metrics))
                context.DrawLine(underlinePen, new Point(X(underline.X1Mm), Y(underline.YMm)), new Point(X(underline.X2Mm), Y(underline.YMm)));
        }
    }

    private void DrawSelection(DrawingContext context)
    {
        if (!Buffer.HasSelection || _caretMap == null)
            return;

        var brush = new SolidColorBrush(Color.FromArgb(0x60, 0x33, 0x99, 0xFF));
        var text = Buffer.Text;
        foreach (var line in _caretMap.Lines)
        {
            var start = Math.Max(Buffer.SelectionStart, line.Start);
            var end = Math.Min(Buffer.SelectionEnd, line.End);
            // Выделенный перенос строки (Enter) показываем узкой полосой после текста строки.
            var includesBreak = Buffer.SelectionEnd > line.End && Buffer.SelectionStart <= line.End
                                && line.End < text.Length && text[line.End] == '\n';
            if (start > end || (start == end && !includesBreak))
                continue;

            var left = X(line.XOf(start));
            var right = X(line.XOf(end)) + (includesBreak ? line.SizeMm * 0.5 * _pxPerMm : 0);
            var (top, bottom) = VerticalSpan(line);
            context.DrawRectangle(brush, null, new Rect(left, top, Math.Max(1, right - left), bottom - top));
        }
    }

    private void DrawCaret(DrawingContext context)
    {
        var line = _caretMap?.LineOf(Buffer.Caret);
        if (line == null)
            return;

        var x = X(line.XOf(Math.Clamp(Buffer.Caret, line.Start, line.End)));
        var (top, bottom) = VerticalSpan(line);
        var slant = Buffer.TypingFormat.Italic ? FontMetrics.ItalicSlant : 0.0;
        var baseline = Y(line.BaselineMm);
        // Курсор наклоняется вместе с наклонным текстом.
        context.DrawLine(new Pen(Brushes.Black, 1.2),
            new Point(x + slant * (baseline - bottom), bottom),
            new Point(x + slant * (baseline - top), top));
    }

    private (double Top, double Bottom) VerticalSpan(CaretLine line) =>
        (Y(line.BaselineMm - line.SizeMm * 1.15), Y(line.BaselineMm + line.SizeMm * 0.35));

    private double X(double mm) => (mm - _layout!.Left) * _pxPerMm;

    private double Y(double mm) => (mm - _layout!.Top) * _pxPerMm;

    private int HitTest(Point point) =>
        _caretMap == null || _layout == null
            ? Buffer.Caret
            : _caretMap.HitTest(point.X / _pxPerMm + _layout.Left, point.Y / _pxPerMm + _layout.Top);

    private static bool SameRuns(TableCell a, TableCell b) =>
        a.Runs.Count == b.Runs.Count && a.Runs.Zip(b.Runs).All(p => p.First.Text == p.Second.Text && p.First.SameFormat(p.Second));
}
