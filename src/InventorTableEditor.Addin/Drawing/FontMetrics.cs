using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace InventorTableEditor.Addin.Drawing;

/// <summary>
/// Метрики шрифта для расстановки надписей. Inventor сообщает размеры надписи по очертаниям букв
/// (без пустых полей), а высота текста в Inventor равна высоте заглавной буквы. По метрикам
/// узнаём, где у надписи базовая линия и какие поля у первой и последней буквы.
/// </summary>
internal sealed class FontMetrics
{
    private static readonly Dictionary<(string Font, bool Bold, bool Italic), FontMetrics?> Cache = new();

    private readonly GlyphTypeface _glyphs;
    private readonly double _capHeightEm;

    /// <summary>Ось Y очертаний направлена вниз (как в WPF); проверяется по заглавной букве.</summary>
    private readonly bool _yDown;

    private FontMetrics(GlyphTypeface glyphs)
    {
        _glyphs = glyphs;
        var cap = Bounds('H') ?? Bounds('D') ?? Bounds('Н');
        _yDown = cap is not { } box || box.Top < 0;
        _capHeightEm = cap is { } capBox && !capBox.IsEmpty ? AboveBaseline(capBox) : 0.7;
        if (_capHeightEm <= 0)
            _capHeightEm = 0.7;
    }

    /// <summary>Кегль (em) в долях высоты заглавной буквы: высота текста Inventor × это число = размер шрифта WPF.</summary>
    public double EmPerCapHeight => 1.0 / _capHeightEm;

    /// <summary>Метрики шрифта или null, если шрифт не найден в Windows.</summary>
    public static FontMetrics? Get(string font, bool bold, bool italic)
    {
        var key = (font, bold, italic);
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        FontMetrics? metrics = null;
        var typeface = new Typeface(new FontFamily(font), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
        if (typeface.TryGetGlyphTypeface(out var glyphs))
            metrics = new FontMetrics(glyphs);
        else
            AddinLog.Write($"Шрифт «{font}» не найден в Windows: положение надписей будет приблизительным.");

        Cache[key] = metrics;
        return metrics;
    }

    /// <summary>
    /// Размеры текста при высоте шрифта <paramref name="heightMm"/>, мм:
    /// подъём верха букв над базовой линией, поле до первой буквы (с ведущими пробелами),
    /// поле после последней буквы (с хвостовыми пробелами) и полная ширина с полями.
    /// </summary>
    public TextExtents Measure(string text, double heightMm)
    {
        var scale = heightMm / _capHeightEm;
        double pen = 0, inkLeft = double.NaN, inkRight = 0, top = 0;
        foreach (var ch in text)
        {
            if (!_glyphs.CharacterToGlyphMap.TryGetValue(ch, out var glyph))
                continue;

            var advance = _glyphs.AdvanceWidths[glyph];
            var bounds = GlyphBounds(glyph);
            if (bounds is { } box && !box.IsEmpty && box.Width > 0)
            {
                if (double.IsNaN(inkLeft))
                    inkLeft = pen + box.Left;
                inkRight = pen + box.Right;
                top = Math.Max(top, AboveBaseline(box));
            }

            pen += advance;
        }

        if (double.IsNaN(inkLeft))
            return new TextExtents(0, pen * scale, 0, pen * scale);

        return new TextExtents(top * scale, inkLeft * scale, (pen - inkRight) * scale, pen * scale);
    }

    private double AboveBaseline(Rect box) => _yDown ? -box.Top : box.Bottom;

    private Rect? Bounds(char ch) =>
        _glyphs.CharacterToGlyphMap.TryGetValue(ch, out var glyph) ? GlyphBounds(glyph) : null;

    /// <summary>Очертания буквы в долях кегля, начало координат на базовой линии, ось Y вниз.</summary>
    private Rect? GlyphBounds(ushort glyph)
    {
        try
        {
            return _glyphs.GetGlyphOutline(glyph, 1.0, 1.0).Bounds;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>Размеры текста в мм: верх букв над базовой линией, поля слева и справа, ширина с полями.</summary>
internal readonly record struct TextExtents(double TopMm, double LeftBearingMm, double RightBearingMm, double AdvanceMm);
