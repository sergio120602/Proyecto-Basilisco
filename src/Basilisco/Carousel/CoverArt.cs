using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Basilisco.Models;

namespace Basilisco.Carousel;

/// <summary>
/// Genera las texturas de las portadas: una caja negra genérica o la imagen que haya elegido el usuario.
/// </summary>
public static class CoverArt
{
    // La portada lleva a la izquierda la franja típica de las cajas de PlayStation y, a su derecha,
    // una zona cuadrada para la carátula: así una imagen de 1024 × 1024 la llena entera sin recortarla.
    public const int Height = 1024;
    const int Frame = 6;
    const int Spine = 144;
    public const int Width = Spine + 2 + Height - Frame;
    const int SpineWidth = (int)(Height * BoxModel.Depth / BoxModel.Height);
    const double Scale = Height / 576.0;

    // Caja rectangular: la franja de la consola arriba y, debajo, una zona 3:4 para carátulas de 600 × 800.
    const int PortraitWidth = 768;
    const int TopBand = 112;
    const int PortraitArtWidth = PortraitWidth - Frame * 2;
    const int PortraitArtHeight = PortraitArtWidth * 4 / 3;
    const int PortraitHeight = TopBand + 2 + PortraitArtHeight + Frame;

    /// <summary>Medidas en píxeles de la portada y la contraportada de cada formato.</summary>
    public static Size FaceSize(CoverFormat format) =>
        format == CoverFormat.Portrait ? new Size(PortraitWidth, PortraitHeight) : new Size(Width, Height);

    /// <summary>Ancho de la caja en la escena 3D (el alto es siempre 1).</summary>
    public static double BoxWidth(CoverFormat format)
    {
        var size = FaceSize(format);
        return size.Width / size.Height;
    }

    /// <summary>Medidas de la zona de la carátula en la portada, para calcular el zoom de «Llenar».</summary>
    public static Size FrontArtSize(CoverFormat format) =>
        format == CoverFormat.Portrait ? new Size(PortraitArtWidth, PortraitArtHeight) : new Size(Height - Frame, Height - Frame * 2);

    // El lomo con imagen se pinta al doble de resolución; un banner girado no necesita más que esto de ancho.
    const int SpineDecodeWidth = 2048;

    // Por encima de esto se decodifica reducida: la portada nunca se pinta más grande.
    const int MaxDecodeWidth = Width;

    static readonly Typeface TitleFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public static BitmapSource RenderCover(CarouselItem item)
    {
        var image = item.ImagePath is null ? null : LoadImage(item.ImagePath);
        var frame = item.FrontFrame ?? ImageFrame.Default;
        if (item.IsAddCard)
            return Rasterize(Width, Height, DrawAddCard);
        if (item.Format == CoverFormat.Portrait)
            return Rasterize(PortraitWidth, PortraitHeight, dc => DrawPortraitCase(dc, item.Title, image, item.Platform, frame));
        return Rasterize(Width, Height, dc => DrawCase(dc, item.Title, image, item.Platform, frame));
    }

    public static BitmapSource RenderBack(CarouselItem item)
    {
        var image = item.BackImagePath is null ? null : LoadImage(item.BackImagePath);
        var size = FaceSize(item.IsAddCard ? CoverFormat.Square : item.Format);
        return Rasterize((int)size.Width, (int)size.Height,
            dc => DrawBack(dc, size, item.Title, image, item.Platform, item.BackFrame ?? ImageFrame.Default));
    }

    /// <summary>
    /// Lomo de la caja: tan alto como la portada y tan ancho como el grosor, con el nombre en vertical
    /// o con la imagen que haya elegido el usuario (entonces al doble de resolución, para que se lea bien).
    /// </summary>
    public static BitmapSource RenderSpine(CarouselItem item)
    {
        var image = item.SpineImagePath is null || item.IsAddCard ? null : LoadImage(item.SpineImagePath, SpineDecodeWidth);
        var scale = image is null ? 1 : 2;
        return Rasterize(SpineWidth * scale, Height * scale, dc =>
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            DrawSpine(dc, item.IsAddCard ? "" : item.Title, item.Platform, image, item.SpineFrame ?? ImageFrame.Default);
            dc.Pop();
        });
    }

    /// <summary>Medidas del lomo (igual en los dos formatos: la caja siempre mide lo mismo de alto y de grosor).</summary>
    public static Size SpineSize => new(SpineWidth, Height);

    /// <summary>
    /// Pinta una imagen dentro de su cara con el encuadre elegido. Con el encuadre por defecto la imagen
    /// se ve entera (sin deformar ni recortar); con zoom se amplía alrededor del centro y se mueve, y lo que
    /// sale de la cara se recorta. Las imágenes horizontales del lomo se giran para leerse de arriba abajo.
    /// </summary>
    static void DrawFramed(DrawingContext dc, BitmapSource image, Rect area, ImageFrame frame, bool rotateWide = false)
    {
        var rotate = rotateWide && image.Width > image.Height;
        double width = rotate ? image.Height : image.Width, height = rotate ? image.Width : image.Height;

        var fit = FitInside(width, height, area);
        var w = fit.Width * frame.Zoom;
        var h = fit.Height * frame.Zoom;
        var center = new Point(area.X + area.Width / 2 + frame.X * area.Width, area.Y + area.Height / 2 + frame.Y * area.Height);

        dc.PushClip(new RectangleGeometry(area));
        if (rotate)
        {
            // Girada 90° en sentido horario: el texto de un banner queda de arriba abajo, como en los lomos reales.
            dc.PushTransform(new RotateTransform(90, center.X, center.Y));
            dc.DrawImage(image, new Rect(center.X - h / 2, center.Y - w / 2, h, w));
            dc.Pop();
        }
        else
        {
            dc.DrawImage(image, new Rect(center.X - w / 2, center.Y - h / 2, w, h));
        }
        dc.Pop();
    }

    /// <summary>Zoom con el que la imagen llena toda la cara (recortando lo que sobre), para el botón «Llenar».</summary>
    public static double FillZoom(Size image, Size face, bool rotateWide)
    {
        var rotate = rotateWide && image.Width > image.Height;
        double width = rotate ? image.Height : image.Width, height = rotate ? image.Width : image.Height;
        var fit = Math.Min(face.Width / width, face.Height / height);
        var fill = Math.Max(face.Width / width, face.Height / height);
        return fill / fit;
    }

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

    static void DrawCase(DrawingContext dc, string title, BitmapSource? image, string? platform, ImageFrame frame)
    {
        var bounds = new Rect(0, 0, Width, Height);

        dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x17, 0x18, 0x1C), Color.FromRgb(0x05, 0x05, 0x07), 45), null, bounds);

        // Franja oficial de la consola si está en basilisco-data/borders; su ancho sale de su propia proporción
        // para no deformarla. Si no, la dibujada por Basilisco, de ancho fijo.
        var bandImage = PlatformArt.BandImage(platform);
        var bandWidth = bandImage is null
            ? Spine + 2
            : Math.Clamp(Height * bandImage.Width / bandImage.Height, 40, Width * 0.3);

        if (image is not null)
        {
            // La carátula va a la derecha de la franja; por defecto entera, sin deformar ni recortar, con negro en el hueco.
            var area = new Rect(bandWidth, Frame, Width - bandWidth - Frame, Height - Frame * 2);
            dc.DrawRectangle(Brushes.Black, null, area);
            DrawFramed(dc, image, area, frame);
        }

        if (bandImage is not null)
            dc.DrawImage(bandImage, new Rect(0, 0, bandWidth, Height));
        else
            PlatformArt.DrawBand(dc, platform, new Rect(0, 0, Spine, Height));

        // Brillo del plástico.
        var gloss = new LinearGradientBrush(Color.FromArgb(30, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(0.6, 0.6));
        dc.DrawRectangle(gloss, null, bounds);

        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0x3C, 0x3F, 0x47)), Frame), new Rect(Frame / 2, Frame / 2, Width - Frame, Height - Frame));

        if (image is null)
        {
            var text = CreateText(title, 30 * Scale, Color.FromArgb(150, 255, 255, 255), Width - bandWidth - 56 * Scale, TextAlignment.Left);
            dc.DrawText(text, new Point(bandWidth + 28 * Scale, Height - 28 * Scale - text.Height));
        }
    }

    /// <summary>Caja rectangular: la franja de la consola arriba y la carátula 3:4 debajo.</summary>
    static void DrawPortraitCase(DrawingContext dc, string title, BitmapSource? image, string? platform, ImageFrame frame)
    {
        var bounds = new Rect(0, 0, PortraitWidth, PortraitHeight);
        dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x17, 0x18, 0x1C), Color.FromRgb(0x05, 0x05, 0x07), 45), null, bounds);

        // Franja de arriba: la del usuario (basilisco-data/borders/<consola>-top.png) con su proporción, o la dibujada.
        var bandImage = PlatformArt.TopBandImage(platform);
        var bandHeight = bandImage is null
            ? TopBand + 2
            : Math.Clamp(PortraitWidth * bandImage.Height / bandImage.Width, 40, PortraitHeight * 0.25);

        var area = new Rect(Frame, bandHeight, PortraitWidth - Frame * 2, PortraitHeight - bandHeight - Frame);
        if (image is not null)
        {
            dc.DrawRectangle(Brushes.Black, null, area);
            DrawFramed(dc, image, area, frame);
        }

        if (bandImage is not null)
            dc.DrawImage(bandImage, new Rect(0, 0, PortraitWidth, bandHeight));
        else
            PlatformArt.DrawTopBand(dc, platform, new Rect(0, 0, PortraitWidth, TopBand));

        var gloss = new LinearGradientBrush(Color.FromArgb(30, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(0.6, 0.6));
        dc.DrawRectangle(gloss, null, bounds);
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0x3C, 0x3F, 0x47)), Frame), new Rect(Frame / 2.0, Frame / 2.0, PortraitWidth - Frame, PortraitHeight - Frame));

        if (image is null)
        {
            var text = CreateText(title, 30 * Scale, Color.FromArgb(150, 255, 255, 255), PortraitWidth - 56 * Scale, TextAlignment.Left);
            dc.DrawText(text, new Point(28 * Scale, PortraitHeight - 28 * Scale - text.Height));
        }
    }

    static void DrawBack(DrawingContext dc, Size size, string title, BitmapSource? image, string? platform, ImageFrame frame)
    {
        double Width = size.Width, Height = size.Height;
        var bounds = new Rect(0, 0, Width, Height);
        // La trasera sin imagen es del color del plástico de la consola.
        var plastic = PlatformArt.Plastic(platform);
        dc.DrawRectangle(new LinearGradientBrush(Shade(plastic, 1.12), Shade(plastic, 0.75), 135), null, bounds);
        var ink = PlatformArt.TextOnPlastic(platform);

        if (image is not null)
        {
            // Con el encuadre que haya elegido el usuario (por defecto, entera y con negro en el hueco que sobre).
            var area = new Rect(Frame, Frame, Width - Frame * 2, Height - Frame * 2);
            dc.DrawRectangle(Brushes.Black, null, area);
            DrawFramed(dc, image, area, frame);
        }
        else
        {
            var text = CreateText(title, 26 * Scale, Color.FromArgb(150, ink.R, ink.G, ink.B), Width - 80 * Scale, TextAlignment.Center);
            dc.DrawText(text, new Point(40 * Scale, 40 * Scale));
            var hint = CreateText("Contraportada", 18 * Scale, Color.FromArgb(80, ink.R, ink.G, ink.B), Width, TextAlignment.Center);
            dc.DrawText(hint, new Point(0, Height - 40 * Scale - hint.Height));
        }

        var gloss = new LinearGradientBrush(Color.FromArgb(24, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(1, 0), new Point(0.4, 0.6));
        dc.DrawRectangle(gloss, null, bounds);
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0x3C, 0x3F, 0x47)), Frame), new Rect(Frame / 2.0, Frame / 2.0, Width - Frame, Height - Frame));
    }

    static void DrawSpine(DrawingContext dc, string title, string? platform, BitmapSource? image, ImageFrame frame)
    {
        // El lomo es del plástico de la consola (verde en Xbox 360, blanco en Wii…).
        var plastic = PlatformArt.Plastic(platform);
        var bounds = new Rect(0, 0, SpineWidth, Height);
        dc.DrawRectangle(new LinearGradientBrush(Shade(plastic, 1.25), Shade(plastic, 0.8), 0), null, bounds);

        if (image is not null)
        {
            // Imagen propia del lomo, encuadrada como haya elegido el usuario; sustituye al nombre.
            dc.DrawRectangle(Brushes.Black, null, bounds);
            DrawFramed(dc, image, bounds, frame, rotateWide: true);
        }
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0x3C, 0x3F, 0x47)), 3), new Rect(1.5, 1.5, SpineWidth - 3, Height - 3));

        if (title.Length == 0 || image is not null)
            return;

        // Texto girado 90° para leerse de arriba abajo, como en el lomo de las cajas reales.
        var text = CreateText(title, SpineWidth * 0.5, PlatformArt.TextOnPlastic(platform), Height - 48, TextAlignment.Left);
        text.MaxLineCount = 1;
        dc.PushTransform(new MatrixTransform(0, 1, -1, 0, SpineWidth / 2.0 + text.Height / 2, 24));
        dc.DrawText(text, new Point(0, 0));
        dc.Pop();
    }

    /// <summary>Aclara (factor &gt; 1) u oscurece (factor &lt; 1) un color, para dar volumen al plástico.</summary>
    static Color Shade(Color color, double factor)
    {
        static byte Channel(byte value, double factor) =>
            (byte)Math.Clamp(factor >= 1 ? value + (255 - value) * (factor - 1) : value * factor, 0, 255);
        return Color.FromRgb(Channel(color.R, factor), Channel(color.G, factor), Channel(color.B, factor));
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
