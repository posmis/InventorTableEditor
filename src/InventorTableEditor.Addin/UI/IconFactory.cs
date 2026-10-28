using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace InventorTableEditor.Addin.UI;

/// <summary>
/// Значки кнопок ленты Inventor (16 и 32 пикселя) в стиле Autodesk: белая таблица с серой сеткой
/// и синей строкой заголовков, значок действия в правом нижнем углу — зелёный «+» (новая таблица)
/// или карандаш (редактирование). Передаются в Inventor как IPictureDisp.
/// </summary>
internal sealed class IconFactory : AxHost
{
    private static readonly Color Ink = Color.FromArgb(0x40, 0x40, 0x40);
    private static readonly Color GridLine = Color.FromArgb(0x9A, 0x9A, 0x9A);
    private static readonly Color Header = Color.FromArgb(0x06, 0x96, 0xD7);
    private static readonly Color HeaderLight = Color.FromArgb(0xBF, 0xE3, 0xF5);
    private static readonly Color Add = Color.FromArgb(0x2E, 0x9E, 0x47);
    private static readonly Color Pencil = Color.FromArgb(0xF2, 0x9B, 0x1D);

    private IconFactory()
        : base("59EE46BA-677D-4d20-BF10-8D8067CB8B33")
    {
    }

    /// <summary>Значок таблицы; <paramref name="pencil"/> — карандаш (редактирование) вместо «+».</summary>
    public static object CreateTableIcon(int size, bool pencil)
    {
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // Фон ленты Inventor светлый: прозрачные пиксели при переводе в IPictureDisp станут этим цветом.
            g.Clear(Color.FromArgb(0xF5, 0xF5, 0xF5));

            var unit = size / 16f;
            var table = new RectangleF(1.5f * unit, 2.5f * unit, 12f * unit, 10.5f * unit);
            using (var white = new SolidBrush(Color.White))
                g.FillRectangle(white, table);
            using (var header = new SolidBrush(HeaderLight))
                g.FillRectangle(header, table.X, table.Y, table.Width, table.Height / 3);

            using (var grid = new Pen(GridLine, unit))
            {
                for (var i = 1; i < 3; i++)
                {
                    var x = table.X + table.Width * i / 3;
                    g.DrawLine(grid, x, table.Top, x, table.Bottom);
                }

                var y = table.Y + table.Height * 2 / 3;
                g.DrawLine(grid, table.Left, y, table.Right, y);
            }

            using (var headerLine = new Pen(Header, unit * 1.2f))
                g.DrawLine(headerLine, table.Left, table.Y + table.Height / 3, table.Right, table.Y + table.Height / 3);
            using (var frame = new Pen(Ink, unit))
                g.DrawRectangle(frame, table.X, table.Y, table.Width, table.Height);

            if (pencil)
                DrawPencil(g, unit);
            else
                DrawPlus(g, unit);
        }

        return GetIPictureDispFromPicture(bitmap);
    }

    private static void DrawPlus(Graphics g, float unit)
    {
        var center = new PointF(12.2f * unit, 12.2f * unit);
        var radius = 3.6f * unit;
        using (var badge = new SolidBrush(Add))
            g.FillEllipse(badge, center.X - radius, center.Y - radius, radius * 2, radius * 2);
        using (var rim = new Pen(Color.White, unit))
            g.DrawEllipse(rim, center.X - radius, center.Y - radius, radius * 2, radius * 2);
        using var cross = new Pen(Color.White, 1.4f * unit) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(cross, center.X - 2f * unit, center.Y, center.X + 2f * unit, center.Y);
        g.DrawLine(cross, center.X, center.Y - 2f * unit, center.X, center.Y + 2f * unit);
    }

    private static void DrawPencil(Graphics g, float unit)
    {
        // Карандаш наискосок из правого верхнего угла значка действия в левый нижний.
        var tip = new PointF(8.6f * unit, 15f * unit);
        var end = new PointF(15f * unit, 8.6f * unit);
        using (var outline = new Pen(Color.White, 4.2f * unit) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(outline, tip, end);
        using (var body = new Pen(Pencil, 2.6f * unit) { StartCap = LineCap.Triangle, EndCap = LineCap.Flat })
            g.DrawLine(body, tip, end);
        using var lead = new SolidBrush(Ink);
        g.FillEllipse(lead, tip.X - 0.9f * unit, tip.Y - 0.9f * unit, 1.8f * unit, 1.8f * unit);
    }
}
