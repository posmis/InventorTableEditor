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

    /// <summary>Расстояние от базовой линии вниз до подчёркивания при высоте текста <paramref name="heightMm"/>, мм.</summary>
    public double UnderlineOffsetMm(double heightMm)
    {
        var position = Math.Abs(_glyphs.UnderlinePosition);
        return (position > 0 ? position : 0.1) / _capHeightEm * heightMm;
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
    /// Наклон курсива Inventor: буквы сдвигаются вокруг базовой линии примерно на 15°
    /// (подобрано по ширинам надписей из журнала Inventor 2025).
    /// </summary>
    public static readonly double ItalicSlant = Math.Tan(15 * Math.PI / 180);

    /// <summary>
    /// Размеры текста при высоте шрифта <paramref name="heightMm"/>, мм, от начала текста (положения пера):
    /// подъём верха букв над базовой линией; левый край надписи Inventor (у наклонного текста нижние
    /// выносные элементы уходят левее начала); ширина текста по ширинам букв; на сколько наклонные
    /// буквы выступают правее этой ширины.
    /// </summary>
    public TextExtents Measure(string text, double heightMm, bool italic)
    {
        var scale = heightMm / _capHeightEm;
        var slant = italic ? ItalicSlant : 0.0;
        double pen = 0, left = 0, right = 0, top = 0;
        foreach (var ch in text)
        {
            if (!_glyphs.CharacterToGlyphMap.TryGetValue(ch, out var glyph))
                continue;

            if (SlantedBounds(glyph, slant) is { } box)
            {
                left = Math.Min(left, pen + box.Left);
                right = Math.Max(right, pen + box.Right);
                top = Math.Max(top, box.Top);
            }

            pen += _glyphs.AdvanceWidths[glyph];
        }

        return new TextExtents(top * scale, left * scale, pen * scale, Math.Max(0, right - pen) * scale);
    }

    /// <summary>Габарит буквы с наклоном, в долях кегля: X вправо от пера, Top — подъём над базовой линией.</summary>
    private (double Left, double Right, double Top)? SlantedBounds(ushort glyph, double slant)
    {
        try
        {
            var outline = _glyphs.GetGlyphOutline(glyph, 1.0, 1.0).Clone();
            var plain = outline.Bounds;
            if (plain.IsEmpty || plain.Width <= 0)
                return null;

            // Сдвиг вправо пропорционален высоте над базовой линией.
            var shear = _yDown ? -slant : slant;
            outline.Transform = new MatrixTransform(new Matrix(1, 0, shear, 1, 0, 0));
            var slanted = outline.Bounds;
            return (slanted.Left, slanted.Right, AboveBaseline(plain));
        }
        catch (Exception)
        {
            return null;
        }
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

/// <summary>
/// Размеры текста в мм от положения пера: верх букв над базовой линией, левый край надписи Inventor
/// (≤ 0), ширина по ширинам букв, выступ наклонных букв правее этой ширины (≥ 0).
/// </summary>
internal readonly record struct TextExtents(double TopMm, double BoxLeftMm, double AdvanceMm, double OverhangMm);
