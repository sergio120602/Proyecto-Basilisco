using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Basilisco.Carousel;

/// <summary>
/// Genera las texturas de las portadas: una caja negra genérica o la imagen que haya elegido el usuario.
/// </summary>
public static class CoverArt
{
    const int Width = 768;
    const int Height = 668;
    const int ReflectionWidth = 384;
    const int ReflectionHeight = 334;
    const double Scale = Width / 576.0;
    const double Spine = Width * 0.085;
    const double Frame = 5;

    // Por encima de esto se decodifica reducida: la portada nunca se pinta más grande.
    const int MaxDecodeWidth = Width * 2;

    static readonly Typeface TitleFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public static BitmapSource RenderCover(CarouselItem item)
    {
        var image = item.ImagePath is null ? null : LoadImage(item.ImagePath);
        return Rasterize(Width, Height, dc =>
        {
            if (item.IsAddCard)
                DrawAddCard(dc);
            else
                DrawCase(dc, item.Title, image);
        });
    }

    public static (BitmapSource Cover, BitmapSource Reflection) Render(CarouselItem item)
    {
        var cover = RenderCover(item);
        var reflection = Rasterize(ReflectionWidth, ReflectionHeight, dc =>
        {
            var fade = new LinearGradientBrush(Color.FromArgb(95, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), new Point(0, 0), new Point(0, 0.5));
            dc.PushOpacityMask(fade);
            dc.PushTransform(new MatrixTransform(1, 0, 0, -1, 0, ReflectionHeight));
            dc.DrawImage(cover, new Rect(0, 0, ReflectionWidth, ReflectionHeight));
            dc.Pop();
            dc.Pop();
        });

        return (cover, reflection);
    }

    /// <summary>Carga una imagen sin bloquear el archivo. Devuelve null si no se puede leer.</summary>
    /// <param name="maxWidth">Si la imagen es más ancha, se decodifica reducida a este ancho.</param>
    public static BitmapSource? LoadImage(string path, int maxWidth = MaxDecodeWidth)
    {
        try
        {
            var image = Decode(path, decodeWidth: 0);
            if (image.PixelWidth > maxWidth)
                image = Decode(path, maxWidth);
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    static BitmapImage Decode(string path, int decodeWidth)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        if (decodeWidth > 0)
            image.DecodePixelWidth = decodeWidth;
        image.EndInit();
        image.Freeze();
        return image;
    }

    static void DrawCase(DrawingContext dc, string title, BitmapSource? image)
    {
        var bounds = new Rect(0, 0, Width, Height);

        dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x17, 0x18, 0x1C), Color.FromRgb(0x05, 0x05, 0x07), 45), null, bounds);

        if (image is not null)
        {
            // La imagen se encaja entera conservando su proporción: ni se deforma ni se recorta.
            // El hueco que sobre se queda en negro.
            var area = new Rect(Spine + 2, Frame, Width - Spine - 2 - Frame, Height - Frame * 2);
            dc.DrawRectangle(Brushes.Black, null, area);
            dc.DrawImage(image, FitInside(image.Width, image.Height, area));
        }

        // Lomo de la caja.
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0D)), null, new Rect(0, 0, Spine, Height));
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2B, 0x2D, 0x33)), null, new Rect(Spine, 0, 2, Height));

        // Brillo del plástico.
        var gloss = new LinearGradientBrush(Color.FromArgb(30, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(0.6, 0.6));
        dc.DrawRectangle(gloss, null, bounds);

        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0x3C, 0x3F, 0x47)), Frame), new Rect(Frame / 2, Frame / 2, Width - Frame, Height - Frame));

        if (image is null)
        {
            var text = CreateText(title, 30 * Scale, Color.FromArgb(150, 255, 255, 255), Width - Spine - 56 * Scale, TextAlignment.Left);
            dc.DrawText(text, new Point(Spine + 28 * Scale, Height - 28 * Scale - text.Height));
        }
    }

    static Rect FitInside(double width, double height, Rect area)
    {
        var scale = Math.Min(area.Width / width, area.Height / height);
        double w = width * scale, h = height * scale;
        return new Rect(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
    }

    static void DrawAddCard(DrawingContext dc)
    {
        var bounds = new Rect(0, 0, Width, Height);
        dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x24, 0x29, 0x32), Color.FromRgb(0x12, 0x14, 0x19), 90), null, bounds);

        const double inset = 22 * Scale, corner = 18 * Scale;
        var dashed = new Pen(new SolidColorBrush(Color.FromRgb(0x6B, 0x75, 0x85)), 4 * Scale) { DashStyle = new DashStyle([4, 3], 0) };
        dc.DrawRoundedRectangle(null, dashed, new Rect(inset, inset, Width - inset * 2, Height - inset * 2), corner, corner);

        var plus = new SolidColorBrush(Color.FromRgb(0xC9, 0xD1, 0xDC));
        const double cx = Width / 2.0, cy = Height / 2.0 - 40 * Scale, arm = 50 * Scale, thickness = 10 * Scale;
        dc.DrawRoundedRectangle(plus, null, new Rect(cx - arm, cy - thickness / 2, arm * 2, thickness), thickness / 2, thickness / 2);
        dc.DrawRoundedRectangle(plus, null, new Rect(cx - thickness / 2, cy - arm, thickness, arm * 2), thickness / 2, thickness / 2);

        var text = CreateText("Importar juego", 36 * Scale, Color.FromRgb(0xC9, 0xD1, 0xDC), Width, TextAlignment.Center);
        dc.DrawText(text, new Point(0, cy + arm + 30 * Scale));
    }

    static FormattedText CreateText(string text, double size, Color color, double maxWidth, TextAlignment alignment) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, TitleFace, size, new SolidColorBrush(color), 1.0)
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = 2,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = alignment,
        };

    static BitmapSource Rasterize(int width, int height, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            draw(dc);

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
