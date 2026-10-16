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
    const int SpineWidth = (int)(Height * BoxModel.Depth / BoxModel.Height);
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

    public static BitmapSource RenderBack(CarouselItem item)
    {
        var image = item.BackImagePath is null ? null : LoadImage(item.BackImagePath);
        return Rasterize(Width, Height, dc => DrawBack(dc, item.Title, image));
    }

    /// <summary>Lomo de la caja: tan alto como la portada y tan ancho como el grosor, con el nombre en vertical.</summary>
    public static BitmapSource RenderSpine(CarouselItem item) =>
        Rasterize(SpineWidth, Height, dc => DrawSpine(dc, item.IsAddCard ? "" : item.Title));

    /// <summary>
    /// Versión para el reflejo en el suelo: a media resolución y desvaneciéndose desde el borde de abajo,
    /// que es el que toca el suelo (la geometría ya se encarga de darle la vuelta).
    /// </summary>
    public static BitmapSource RenderReflection(BitmapSource face)
    {
        int width = Math.Max(1, face.PixelWidth / 2), height = Math.Max(1, face.PixelHeight / 2);
        return Rasterize(width, height, dc =>
        {
            dc.PushOpacityMask(new LinearGradientBrush(Color.FromArgb(95, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), new Point(0, 1), new Point(0, 0.5)));
            dc.DrawImage(face, new Rect(0, 0, width, height));
            dc.Pop();
        });
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

    static void DrawBack(DrawingContext dc, string title, BitmapSource? image)
    {
        var bounds = new Rect(0, 0, Width, Height);
        dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x12, 0x13, 0x17), Color.FromRgb(0x05, 0x05, 0x07), 135), null, bounds);

        if (image is not null)
        {
            // Igual que la portada: entera, sin deformar ni recortar, con negro en el hueco que sobre.
            var area = new Rect(Frame, Frame, Width - Frame * 2, Height - Frame * 2);
            dc.DrawRectangle(Brushes.Black, null, area);
            dc.DrawImage(image, FitInside(image.Width, image.Height, area));
        }
        else
        {
            var text = CreateText(title, 26 * Scale, Color.FromArgb(110, 255, 255, 255), Width - 80 * Scale, TextAlignment.Center);
            dc.DrawText(text, new Point(40 * Scale, 40 * Scale));
            var hint = CreateText("Contraportada", 18 * Scale, Color.FromArgb(60, 255, 255, 255), Width, TextAlignment.Center);
            dc.DrawText(hint, new Point(0, Height - 40 * Scale - hint.Height));
        }

        var gloss = new LinearGradientBrush(Color.FromArgb(24, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(1, 0), new Point(0.4, 0.6));
        dc.DrawRectangle(gloss, null, bounds);
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0x3C, 0x3F, 0x47)), Frame), new Rect(Frame / 2, Frame / 2, Width - Frame, Height - Frame));
    }

    static void DrawSpine(DrawingContext dc, string title)
    {
        dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x1C, 0x1D, 0x22), Color.FromRgb(0x0A, 0x0A, 0x0C), 0), null, new Rect(0, 0, SpineWidth, Height));
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0x3C, 0x3F, 0x47)), 3), new Rect(1.5, 1.5, SpineWidth - 3, Height - 3));

        if (title.Length == 0)
            return;

        // Texto girado 90° para leerse de arriba abajo, como en el lomo de las cajas reales.
        var text = CreateText(title, SpineWidth * 0.5, Color.FromArgb(170, 255, 255, 255), Height - 48, TextAlignment.Left);
        text.MaxLineCount = 1;
        dc.PushTransform(new MatrixTransform(0, 1, -1, 0, SpineWidth / 2.0 + text.Height / 2, 24));
        dc.DrawText(text, new Point(0, 0));
        dc.Pop();
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
