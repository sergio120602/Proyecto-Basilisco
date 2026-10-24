using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Basilisco.Carousel;

/// <summary>Lo que va en la bandeja de la caja abierta: un disco o un cartucho, con su tamaño en la escena.</summary>
/// <param name="Size">Ancho y alto en unidades de la escena (19 cm = 1).</param>
public sealed record CaseContent(BitmapSource Image, Size Size, bool Cartridge);

/// <summary>
/// Cartuchos dibujados para las consolas que no usan disco: Nintendo 64, Nintendo DS y Nintendo Switch. La pegatina
/// lleva la imagen del disco del juego (si se ha elegido una) o su portada.
/// </summary>
public static class CartridgeArt
{
    /// <summary>
    /// Medidas reales de cada cartucho en centímetros. Los de DS y Switch son tan pequeños que en la caja se agrandan
    /// un poco (<c>Scale</c>) para que se vean bien.
    /// </summary>
    static (double Width, double Height, double Scale)? Measures(string? platform) => platform switch
    {
        "n64" => (11.6, 7.5, 1.0),
        "nds" => (3.5, 3.3, 1.6),
        "switch" => (2.1, 3.1, 2.0),
        _ => null,
    };

    public static bool HasCartridge(string? platform) => Measures(platform) is not null;

    /// <summary>Disco o cartucho para la caja abierta de esa consola.</summary>
    /// <param name="discFrame">Encuadre de la imagen del disco (los cartuchos no lo usan).</param>
    public static CaseContent ContentFor(string? platform, BoxShape shape, string? labelPath, string? coverPath, Models.ImageFrame? discFrame = null)
    {
        if (Measures(platform) is { } m)
        {
            var label = labelPath is not null ? CoverArt.LoadImage(labelPath, 1400) : coverPath is not null ? CoverArt.LoadImage(coverPath, 1400) : null;
            var size = new Size(m.Width * m.Scale / 19.0, m.Height * m.Scale / 19.0);
            return new CaseContent(Render(platform!, label), size, Cartridge: true);
        }
        var diameter = Math.Min(shape.Width, shape.Height) * 0.88;
        return new CaseContent(OpenCaseArt.RenderDisc(labelPath, coverPath, discFrame), new Size(diameter, diameter), Cartridge: false);
    }

    static BitmapSource Render(string platform, BitmapSource? label) => platform switch
    {
        "n64" => RenderN64(label),
        "nds" => RenderDs(label),
        _ => RenderSwitch(label),
    };

    // Nintendo 64: gris, con la parte central más alta y redondeada arriba, dos alas a los lados separadas por
    // ranuras y la pegatina en el centro.
    static BitmapSource RenderN64(BitmapSource? label)
    {
        const int w = 1160, h = 750;
        var grey = Color.FromRgb(0xA4, 0xA7, 0xAD);
        return Draw(w, h, dc =>
        {
            // Alas de los lados (un poco más bajas, con las esquinas de arriba redondeadas).
            dc.DrawRoundedRectangle(new LinearGradientBrush(Shade(grey, 1.06), Shade(grey, 0.86), 90), new Pen(new SolidColorBrush(Shade(grey, 0.62)), 3),
                new Rect(2, 60, w - 4, h - 62), 70, 70);
            // Parte central, con el borde de arriba en arco.
            var center = new StreamGeometry();
            using (var g = center.Open())
            {
                g.BeginFigure(new Point(150, h - 2), true, true);
                g.LineTo(new Point(150, 50), true, true);
                g.QuadraticBezierTo(new Point(w / 2.0, -14), new Point(w - 150, 50), true, true);
                g.LineTo(new Point(w - 150, h - 2), true, true);
            }
            dc.DrawGeometry(new LinearGradientBrush(Shade(grey, 1.12), Shade(grey, 0.92), 90), new Pen(new SolidColorBrush(Shade(grey, 0.62)), 3), center);
            // Ranuras entre el centro y las alas, y los escalones de abajo.
            var groove = new Pen(new SolidColorBrush(Shade(grey, 0.55)), 6);
            dc.DrawLine(groove, new Point(150, 70), new Point(150, h - 4));
            dc.DrawLine(groove, new Point(w - 150, 70), new Point(w - 150, h - 4));
            var step = new SolidColorBrush(Shade(grey, 0.78));
            dc.DrawRectangle(step, null, new Rect(10, h - 150, 70, 146));
            dc.DrawRectangle(step, null, new Rect(w - 80, h - 150, 70, 146));
            // Pegatina.
            Label(dc, label, new Rect(205, 120, w - 410, h - 190), 16, Color.FromRgb(0xF4, 0xF1, 0xE8));
            Shine(dc, w, h);
        });
    }

    // Nintendo DS: cuadradito gris oscuro con la pegatina blanca (con su cabecera), una muesca abajo a la
    // izquierda y la flecha que indica cómo meterlo.
    static BitmapSource RenderDs(BitmapSource? label)
    {
        const int w = 700, h = 660;
        var body = Color.FromRgb(0x4A, 0x4D, 0x53);
        return Draw(w, h, dc =>
        {
            var shape = new StreamGeometry();
            using (var g = shape.Open())
            {
                g.BeginFigure(new Point(40, 2), true, true);
                g.LineTo(new Point(w - 40, 2), true, true);
                g.ArcTo(new Point(w - 2, 40), new Size(38, 38), 0, false, SweepDirection.Clockwise, true, true);
                g.LineTo(new Point(w - 2, h - 40), true, true);
                g.ArcTo(new Point(w - 40, h - 2), new Size(38, 38), 0, false, SweepDirection.Clockwise, true, true);
                g.LineTo(new Point(90, h - 2), true, true);
                g.LineTo(new Point(90, h - 40), true, true);
                g.LineTo(new Point(2, h - 40), true, true);
                g.LineTo(new Point(2, 40), true, true);
                g.ArcTo(new Point(40, 2), new Size(38, 38), 0, false, SweepDirection.Clockwise, true, true);
            }
            dc.DrawGeometry(new LinearGradientBrush(Shade(body, 1.15), Shade(body, 0.85), 90), new Pen(new SolidColorBrush(Shade(body, 0.6)), 3), shape);
            var sticker = new Rect(42, 34, w - 84, h - 140);
            dc.DrawRoundedRectangle(Brushes.White, null, sticker, 14, 14);
            var header = new Rect(sticker.X, sticker.Y, sticker.Width, sticker.Height * 0.2);
            var title = Text("NINTENDO DS", header.Height * 0.42, new SolidColorBrush(Color.FromRgb(0x30, 0x32, 0x36)), FontWeights.Black);
            dc.DrawText(title, new Point(header.X + (header.Width - title.Width) / 2, header.Y + (header.Height - title.Height) / 2));
            Label(dc, label, new Rect(sticker.X + 18, header.Bottom, sticker.Width - 36, sticker.Height - header.Height - 18), 8, Colors.White);
            Arrow(dc, new Point(w / 2.0, h - 58), 22, Shade(body, 0.55));
            Shine(dc, w, h);
        });
    }

    // Nintendo Switch: tarjetita negra con la pegatina de cabecera roja y la flecha abajo.
    static BitmapSource RenderSwitch(BitmapSource? label)
    {
        const int w = 620, h = 900;
        var body = Color.FromRgb(0x26, 0x27, 0x2B);
        return Draw(w, h, dc =>
        {
            dc.DrawRoundedRectangle(new LinearGradientBrush(Shade(body, 1.25), Shade(body, 0.9), 90), new Pen(new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x12)), 4),
                new Rect(2, 2, w - 4, h - 4), 46, 46);
            // Muescas de los lados, abajo.
            var notch = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x16));
            dc.DrawRectangle(notch, null, new Rect(0, h - 120, 22, 60));
            dc.DrawRectangle(notch, null, new Rect(w - 22, h - 120, 22, 60));
            var sticker = new Rect(40, 40, w - 80, h - 170);
            dc.DrawRoundedRectangle(Brushes.White, null, sticker, 16, 16);
            var header = new Rect(sticker.X, sticker.Y, sticker.Width, sticker.Height * 0.17);
            dc.PushClip(new RectangleGeometry(sticker, 16, 16));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xE6, 0x00, 0x12)), null, header);
            dc.Pop();
            // El logotipo de la consola, simplificado: dos mandos.
            var logo = new Rect(header.X + 26, header.Y + header.Height * 0.2, header.Height * 0.6, header.Height * 0.6);
            var pen = new Pen(Brushes.White, logo.Width * 0.09);
            dc.DrawRoundedRectangle(null, pen, new Rect(logo.X, logo.Y, logo.Width * 0.46, logo.Height), logo.Width * 0.2, logo.Width * 0.2);
            dc.DrawRoundedRectangle(Brushes.White, null, new Rect(logo.X + logo.Width * 0.54, logo.Y, logo.Width * 0.46, logo.Height), logo.Width * 0.2, logo.Width * 0.2);
            var title = Text("NINTENDO SWITCH", header.Height * 0.3, Brushes.White, FontWeights.Bold);
            dc.DrawText(title, new Point(logo.Right + 18, header.Y + (header.Height - title.Height) / 2));
            Label(dc, label, new Rect(sticker.X, header.Bottom, sticker.Width, sticker.Height - header.Height), 0, Colors.White, clip: new RectangleGeometry(sticker, 16, 16));
            Arrow(dc, new Point(w / 2.0, h - 66), 24, Color.FromRgb(0x5A, 0x5C, 0x62));
            Shine(dc, w, h);
        });
    }

    /// <summary>La imagen del juego en la pegatina, llenándola. Sin imagen, la pegatina se queda lisa.</summary>
    static void Label(DrawingContext dc, BitmapSource? image, Rect area, double radius, Color paper, Geometry? clip = null)
    {
        dc.PushClip(clip ?? new RectangleGeometry(area, radius, radius));
        dc.DrawRectangle(new SolidColorBrush(paper), null, area);
        if (image is not null)
        {
            var scale = Math.Max(area.Width / image.Width, area.Height / image.Height);
            var size = new Size(image.Width * scale, image.Height * scale);
            dc.PushClip(new RectangleGeometry(area));
            dc.DrawImage(image, new Rect(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2, size.Width, size.Height));
            dc.Pop();
        }
        dc.Pop();
        dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), 2), area, radius, radius);
    }

    static void Arrow(DrawingContext dc, Point tip, double size, Color color)
    {
        var arrow = new StreamGeometry();
        using (var g = arrow.Open())
        {
            g.BeginFigure(new Point(tip.X - size, tip.Y - size * 0.6), true, true);
            g.LineTo(new Point(tip.X + size, tip.Y - size * 0.6), true, true);
            g.LineTo(new Point(tip.X, tip.Y + size * 0.6), true, true);
        }
        dc.DrawGeometry(new SolidColorBrush(color), null, arrow);
    }

    /// <summary>Brillo del plástico en diagonal.</summary>
    static void Shine(DrawingContext dc, int w, int h) =>
        dc.DrawRectangle(new LinearGradientBrush(
            [
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.25),
                new GradientStop(Color.FromArgb(38, 255, 255, 255), 0.45),
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.6),
            ], 30), null, new Rect(0, 0, w, h));

    static FormattedText Text(string text, double size, Brush brush, FontWeight weight) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), size, brush, 1.0);

    static Color Shade(Color color, double factor) => Color.FromRgb(
        (byte)Math.Clamp(color.R * factor, 0, 255), (byte)Math.Clamp(color.G * factor, 0, 255), (byte)Math.Clamp(color.B * factor, 0, 255));

    static BitmapSource Draw(int width, int height, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
            draw(dc);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
