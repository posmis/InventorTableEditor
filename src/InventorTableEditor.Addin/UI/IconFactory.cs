using System.Drawing;
using System.Windows.Forms;

namespace InventorTableEditor.Addin.UI;

/// <summary>
/// Иконки кнопок ленты, нарисованные кодом, в формате IPictureDisp для Inventor.
/// </summary>
internal sealed class IconFactory : AxHost
{
    private IconFactory()
        : base("59EE46BA-677D-4d20-BF10-8D8067CB8B33")
    {
    }

    /// <summary>Значок таблицы; <paramref name="pencil"/> добавляет карандаш (редактирование).</summary>
    public static object CreateTableIcon(int size, bool pencil)
    {
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            var margin = size / 8f;
            var rect = new RectangleF(margin, margin, size - 2 * margin, size - 2 * margin);
            using var header = new SolidBrush(Color.FromArgb(0x9C, 0xC3, 0xE6));
            g.FillRectangle(Brushes.White, rect);
            g.FillRectangle(header, rect.X, rect.Y, rect.Width, rect.Height / 3);

            using var thin = new Pen(Color.FromArgb(0x40, 0x40, 0x40), 1);
            for (var i = 1; i < 3; i++)
            {
                var y = rect.Y + rect.Height * i / 3;
                g.DrawLine(thin, rect.Left, y, rect.Right, y);
                var x = rect.X + rect.Width * i / 3;
                g.DrawLine(thin, x, rect.Top, x, rect.Bottom);
            }

            using var thick = new Pen(Color.Black, size >= 32 ? 2 : 1);
            g.DrawRectangle(thick, rect.X, rect.Y, rect.Width, rect.Height);

            if (pencil)
            {
                using var pen = new Pen(Color.FromArgb(0xE0, 0x70, 0x10), size / 6f);
                g.DrawLine(pen, size * 0.45f, size * 0.95f, size * 0.95f, size * 0.45f);
            }
        }

        return GetIPictureDispFromPicture(bitmap);
    }
}
