using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Basilisco.Models;

namespace Basilisco.Carousel;

/// <summary>Texturas de la caja abierta: el disco, la bandeja donde va y el interior de la tapa con el manual.</summary>
public static class OpenCaseArt
{
    const int DiscSize = 1024;

    // Proporciones de un disco de 12 cm: agujero de 15 mm y anillo transparente hasta unos 17 mm del centro.
    const double HoleRadius = 7.5 / 60;
    const double ClearRingRadius = 17.0 / 60;

    /// <summary>
    /// El disco con su imagen recortada en redondo (o, si no tiene, con la portada del juego sobre un disco plateado),
    /// el anillo transparente del centro y el agujero. Fuera del círculo es transparente.
    /// </summary>
    public static BitmapSource RenderDisc(string? discPath, string? coverPath)
    {
        var size = DiscSize;
        var center = new Point(size / 2.0, size / 2.0);
        var radius = size / 2.0 - 2;
        var art = discPath is not null ? CoverArt.LoadImage(discPath, size) : null;
        var cover = art is null && coverPath is not null ? CoverArt.LoadImage(coverPath, size) : null;

        return Render(size, size, dc =>
        {
            var disc = new EllipseGeometry(center, radius, radius);
            dc.PushClip(disc);

            // Plateado de fondo, con el brillo en abanico de un disco de verdad.
            dc.DrawEllipse(new RadialGradientBrush(Color.FromRgb(0xE9, 0xEC, 0xF0), Color.FromRgb(0x9A, 0xA1, 0xAB)), null, center, radius, radius);
            dc.DrawGeometry(new LinearGradientBrush(
                [
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.25),
                    new GradientStop(Color.FromArgb(120, 255, 255, 255), 0.5),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.75),
                ], 45), null, disc);

            if (art is not null)
                DrawFill(dc, art, new Rect(0, 0, size, size));
            else if (cover is not null)
            {
                // Sin imagen de disco: la portada impresa en el disco, algo apagada para que se note el plateado.
                dc.PushOpacity(0.92);
                DrawFill(dc, cover, new Rect(0, 0, size, size));
                dc.Pop();
            }
            dc.Pop();

            // Anillo central de plástico transparente y el agujero (se ve la bandeja a través).
            var ring = Geometry.Combine(
                new EllipseGeometry(center, radius * ClearRingRadius, radius * ClearRingRadius),
                new EllipseGeometry(center, radius * HoleRadius, radius * HoleRadius),
                GeometryCombineMode.Exclude, null);
            var clear = new RadialGradientBrush(Color.FromArgb(235, 0xD8, 0xDD, 0xE3), Color.FromArgb(235, 0xB4, 0xBB, 0xC4))
            {
                Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.4, 0.4), RadiusX = 0.5, RadiusY = 0.5,
            };
            dc.DrawGeometry(clear, new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 3), ring);
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(90, 0x50, 0x55, 0x5E)), 2), center, radius * 0.21, radius * 0.21);

            // Canto del disco.
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(160, 0xF4, 0xF6, 0xF8)), 4), center, radius - 1, radius - 1);
        }, transparentHole: (center, radius * HoleRadius));
    }

    /// <summary>
    /// La bandeja de la caja: plástico translúcido con el hueco del disco y el botón del centro que lo sujeta.
    /// Debajo del plástico se ve, nítida, la mitad derecha de la imagen del interior (<paramref name="inside"/>).
    /// </summary>
    public static BitmapSource RenderTray(Color plastic, double aspect, double discFraction, BitmapSource? inside = null, ImageFrame? insideFrame = null)
    {
        var width = 768;
        var height = (int)Math.Round(width / aspect);
        var center = new Point(width / 2.0, height / 2.0);
        var radius = width * discFraction / 2;
        var light = Shade(plastic, 1.25);
        var dark = Shade(plastic, 0.7);

        return Render(width, height, dc =>
        {
            DrawPlastic(dc, plastic, inside, new Rect(0, 0, width, height), 70, rightHalf: true, insideFrame);
            // El hueco donde descansa el disco, un poco más hondo (también translúcido).
            dc.DrawEllipse(new RadialGradientBrush(WithAlpha(dark, 150), WithAlpha(plastic, 90)), new Pen(new SolidColorBrush(WithAlpha(Shade(plastic, 0.55), 200)), 3),
                center, radius * 1.03, radius * 1.03);
            // El botón central con sus dientes.
            for (var i = 0; i < 12; i++)
            {
                var angle = i * Math.PI / 6;
                var tip = new Point(center.X + Math.Cos(angle) * radius * 0.16, center.Y + Math.Sin(angle) * radius * 0.16);
                dc.DrawLine(new Pen(new SolidColorBrush(light), radius * 0.035), center, tip);
            }
            dc.DrawEllipse(new SolidColorBrush(Shade(plastic, 1.4)), new Pen(new SolidColorBrush(dark), 2), center, radius * 0.1, radius * 0.1);
            // Borde de la caja.
            dc.DrawRectangle(null, new Pen(new SolidColorBrush(Shade(plastic, 0.5)), 6), new Rect(3, 3, width - 6, height - 6));
        });
    }

    /// <summary>
    /// El interior de la tapa: el plástico con las pestañas que sujetan el librito, y encima el manual (la imagen,
    /// rellenando el librito). Sin imagen, el nombre del juego en grande, como la portada interior de un manual.
    /// </summary>
    /// <param name="inside">La imagen del interior: en la tapa se ve su mitad izquierda, a través del plástico.</param>
    /// <remarks>Con imagen del interior y sin manual, no se pone el librito de ejemplo: así se ve la imagen entera.</remarks>
    /// <param name="manualFrame">Encuadre de la imagen del manual (null: llena el librito).</param>
    public static BitmapSource RenderInside(Color plastic, double aspect, string? manualPath, string title, BitmapSource? inside = null,
                                            ImageFrame? manualFrame = null, ImageFrame? insideFrame = null)
    {
        var width = 768;
        var height = (int)Math.Round(width / aspect);
        var manual = manualPath is not null ? CoverArt.LoadImage(manualPath, 1400) : null;
        var light = Shade(plastic, 1.25);

        return Render(width, height, dc =>
        {
            DrawPlastic(dc, plastic, inside, new Rect(0, 0, width, height), 110, rightHalf: false, insideFrame);
            dc.DrawRectangle(null, new Pen(new SolidColorBrush(Shade(plastic, 0.5)), 6), new Rect(3, 3, width - 6, height - 6));
            if (manual is null && inside is not null)
                return;

            // El librito, con un margen y su sombra (alrededor se ve el plástico).
            var margin = width * 0.085;
            var booklet = new Rect(margin, margin, width - 2 * margin, height - 2 * margin);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), null, new Rect(booklet.X + 6, booklet.Y + 8, booklet.Width, booklet.Height), 6, 6);
            if (manual is not null)
            {
                dc.PushClip(new RectangleGeometry(booklet, 4, 4));
                dc.DrawRectangle(Brushes.Black, null, booklet);
                CoverArt.DrawFramed(dc, manual, booklet, manualFrame ?? FillFrame(manual, booklet.Size));
                dc.Pop();
            }
            else
            {
                dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromRgb(0xF7, 0xF4, 0xEC), Color.FromRgb(0xE4, 0xDF, 0xD3), 90), null, booklet, 4, 4);
                var text = new FormattedText(title, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                    54, new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x35)), 1.0)
                {
                    MaxTextWidth = booklet.Width * 0.82,
                    TextAlignment = TextAlignment.Center,
                    MaxLineCount = 4,
                    Trimming = TextTrimming.CharacterEllipsis,
                };
                dc.DrawText(text, new Point(booklet.X + booklet.Width * 0.09, booklet.Y + booklet.Height * 0.38 - text.Height / 2));
                var caption = new FormattedText("MANUAL DE INSTRUCCIONES", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 20, new SolidColorBrush(Color.FromRgb(0x7A, 0x7F, 0x88)), 1.0)
                {
                    MaxTextWidth = booklet.Width,
                    TextAlignment = TextAlignment.Center,
                };
                dc.DrawText(caption, new Point(booklet.X, booklet.Bottom - booklet.Height * 0.14));
            }
            dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), 2), booklet, 4, 4);

            // Pestañas de plástico que sujetan el librito.
            var tab = new SolidColorBrush(Color.FromArgb(220, light.R, light.G, light.B));
            foreach (var y in new[] { booklet.Y - 4, booklet.Bottom - 22 })
                dc.DrawRoundedRectangle(tab, null, new Rect(width / 2.0 - 46, y, 92, 26), 6, 6);
        });
    }

    /// <summary>
    /// Plástico translúcido. Si hay imagen del interior, se ve nítida debajo, ocupando las dos mitades de la caja abierta
    /// (la tapa a la izquierda y la bandeja a la derecha), y el plástico solo le da un velo de su color y un brillo.
    /// </summary>
    static void DrawPlastic(DrawingContext dc, Color plastic, BitmapSource? inside, Rect area, double angle, bool rightHalf, ImageFrame? frame)
    {
        if (inside is not null)
        {
            // La imagen se encuadra en la caja abierta entera (el doble de ancho) y cada mitad pinta su parte.
            var spread = new Rect(rightHalf ? area.X - area.Width : area.X, area.Y, area.Width * 2, area.Height);
            dc.PushClip(new RectangleGeometry(area));
            dc.DrawRectangle(new SolidColorBrush(plastic), null, area);
            CoverArt.DrawFramed(dc, inside, spread, frame ?? FillFrame(inside, spread.Size));
            dc.Pop();
            dc.DrawRectangle(new LinearGradientBrush(WithAlpha(plastic, 110), WithAlpha(Shade(plastic, 1.25), 80), angle), null, area);
        }
        else
        {
            dc.DrawRectangle(new LinearGradientBrush(Shade(plastic, 1.25), plastic, angle), null, area);
        }

        // Reflejo del plástico.
        dc.DrawRectangle(new LinearGradientBrush(
            [
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.2),
                new GradientStop(Color.FromArgb(46, 255, 255, 255), 0.45),
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.62),
            ], 35), null, area);
    }

    /// <summary>El encuadre con el que la imagen llena el hueco (el de siempre, si no se ha ajustado).</summary>
    public static ImageFrame FillFrame(BitmapSource image, Size area) =>
        new(CoverArt.FillZoom(new Size(image.Width, image.Height), area, rotateWide: false));

    /// <summary>Tamaño del librito del manual dentro de la tapa (en los píxeles de la textura), para ajustarlo.</summary>
    public static Size BookletSize(double aspect)
    {
        var width = 768.0;
        var height = Math.Round(width / aspect);
        var margin = width * 0.085;
        return new Size(width - 2 * margin, height - 2 * margin);
    }

    /// <summary>Tamaño de la caja abierta entera (tapa y bandeja), para ajustar la imagen del interior.</summary>
    public static Size SpreadSize(double aspect) => new(768.0 * 2, Math.Round(768.0 / aspect));

    /// <summary>
    /// La caja abierta vista de frente, en plano (la tapa a la izquierda y la bandeja con el disco a la derecha),
    /// para el editor de encuadre.
    /// </summary>
    public static BitmapSource RenderSpread(Color plastic, double aspect, double discFraction, string? manualPath, string title,
                                            BitmapSource? inside, ImageFrame? manualFrame, ImageFrame? insideFrame, BitmapSource? disc)
    {
        var lid = RenderInside(plastic, aspect, manualPath, title, inside, manualFrame, insideFrame);
        var tray = RenderTray(plastic, aspect, discFraction, inside, insideFrame);
        return Render(lid.PixelWidth + tray.PixelWidth, lid.PixelHeight, dc =>
        {
            dc.DrawImage(lid, new Rect(0, 0, lid.PixelWidth, lid.PixelHeight));
            dc.DrawImage(tray, new Rect(lid.PixelWidth, 0, tray.PixelWidth, tray.PixelHeight));
            if (disc is not null)
            {
                var size = tray.PixelWidth * discFraction;
                dc.DrawImage(disc, new Rect(lid.PixelWidth + (tray.PixelWidth - size) / 2, (tray.PixelHeight - size) / 2, size, size));
            }
        });
    }

    static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    /// <summary>Pinta la imagen llenando el rectángulo (recortando lo que sobre), centrada.</summary>
    static void DrawFill(DrawingContext dc, BitmapSource image, Rect target)
    {
        var scale = Math.Max(target.Width / image.Width, target.Height / image.Height);
        var size = new Size(image.Width * scale, image.Height * scale);
        dc.PushClip(new RectangleGeometry(target));
        dc.DrawImage(image, new Rect(target.X + (target.Width - size.Width) / 2, target.Y + (target.Height - size.Height) / 2, size.Width, size.Height));
        dc.Pop();
    }

    static Color Shade(Color color, double factor) => Color.FromRgb(
        (byte)Math.Clamp(color.R * factor + (factor > 1 ? 18 : 0), 0, 255),
        (byte)Math.Clamp(color.G * factor + (factor > 1 ? 18 : 0), 0, 255),
        (byte)Math.Clamp(color.B * factor + (factor > 1 ? 18 : 0), 0, 255));

    static BitmapSource Render(int width, int height, Action<DrawingContext> draw, (Point Center, double Radius)? transparentHole = null)
    {
        var visual = new DrawingVisual();
        // Las imágenes ampliadas se suavizan (el desenfoque del plástico depende de ello).
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            if (transparentHole is { } hole)
            {
                // Todo lo que se pinte deja libre el agujero del centro.
                dc.PushClip(Geometry.Combine(new RectangleGeometry(new Rect(0, 0, width, height)),
                    new EllipseGeometry(hole.Center, hole.Radius, hole.Radius), GeometryCombineMode.Exclude, null));
                draw(dc);
                dc.Pop();
            }
            else
            {
                draw(dc);
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
