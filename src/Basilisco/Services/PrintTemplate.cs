using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Basilisco.Services;

/// <summary>Las partes de una carátula para imprimir, ya recortadas (archivos temporales). Null: esa parte no viene.</summary>
public sealed record CoverParts(string? Front, string? Back, string? Spine, string? Disc)
{
    public string? Get(ArtKind kind) => kind switch
    {
        ArtKind.Front => Front,
        ArtKind.Back => Back,
        ArtKind.Spine => Spine,
        ArtKind.Disc => Disc,
        _ => null,
    };
}

/// <summary>
/// Recorta las plantillas de impresión de Steam Game Covers: quita el margen blanco, las guías de corte y los textos
/// legales, y separa la carátula completa (contraportada, lomo y portada en una sola imagen) en sus tres partes.
/// </summary>
public static class PrintTemplate
{
    // Proporción ancho / alto de la portada de cada caja, para saber dónde acaba la portada en una carátula completa.
    const double DvdFront = 130.0 / 184.0;
    const double BluRayFront = 128.5 / 148.0;

    // Jewel case: la trasera mide 151 × 118 mm, con un lomo de 6,5 mm a cada lado de los 138 mm del centro.
    const double JewelBackCenter = 138.0 / 118.0;
    const double JewelBack = 151.0 / 118.0;

    // Proporción de la carátula completa (contraportada, lomo y portada): DVD de 130 + 14 + 130 mm por 184 de alto; la de
    // Blu-ray, la de las plantillas de la web.
    const double DvdWrap = (130.0 * 2 + 14) / 184.0;
    const double BluRayWrap = 1.79;

    /// <summary>
    /// Corrige la zona del dibujo si no tiene la proporción que debe. Si se ha quedado corta en una medida (el alto o el
    /// ancho), se alarga hasta la proporción buena; si casi no se ha encontrado nada (un dibujo blanco sobre el papel
    /// blanco), se toma el tamaño normal de las plantillas. En las plantillas de la web el dibujo va siempre centrado en la
    /// hoja, así que la zona corregida se centra.
    /// </summary>
    static Int32Rect FitTemplate(Int32Rect art, int pageWidth, int pageHeight, double aspect)
    {
        if (art.Width <= 0 || art.Height <= 0)
            art = new Int32Rect(0, 0, pageWidth, pageHeight);
        var ratio = (double)art.Width / art.Height;
        if (Math.Abs(ratio - aspect) <= aspect * 0.04)
            return art;

        double width = art.Width, height = art.Height;
        if (art.Width < pageWidth * 0.5 && art.Height < pageHeight * 0.5)
        {
            // Casi nada: el dibujo ocupa la hoja de lado a lado, con el margen de las plantillas (un 1,2 % a cada lado).
            width = pageWidth * 0.976;
            height = width / aspect;
            if (height > pageHeight * 0.86)
            {
                height = pageHeight * 0.86;
                width = height * aspect;
            }
        }
        else if (ratio > aspect)
        {
            height = width / aspect; // Se ha quedado corto el alto.
        }
        else
        {
            width = height * aspect; // Se ha quedado corto el ancho.
        }

        width = Math.Min(width, pageWidth);
        height = Math.Min(height, pageHeight);
        return new Int32Rect((int)Math.Round((pageWidth - width) / 2), (int)Math.Round((pageHeight - height) / 2),
            (int)Math.Round(width), (int)Math.Round(height));
    }

    /// <param name="caseType">Tipo de caja según la web: «DVD», «Blu-ray», «Jewel»…</param>
    /// <param name="isBack">True si la imagen es solo la parte de atrás.</param>
    public static CoverParts Split(string path, string? caseType, bool isBack, bool isDisc)
    {
        var image = Load(path);
        var folder = Path.Combine(Path.GetTempPath(), "Basilisco");
        Directory.CreateDirectory(folder);
        string Save(Int32Rect rect)
        {
            var file = Path.Combine(folder, $"{Guid.NewGuid():N}.jpg");
            var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
            encoder.Frames.Add(BitmapFrame.Create(new CroppedBitmap(image, rect)));
            using var stream = File.Create(file);
            encoder.Save(stream);
            return file;
        }

        if (isDisc)
        {
            // La pegatina del disco: un círculo en el centro de la hoja. Se deja un cuadrado justo alrededor.
            var disc = ArtArea(image, 0.15);
            var side = Math.Min(Math.Max(disc.Width, disc.Height), Math.Min(image.PixelWidth, image.PixelHeight));
            var x = Math.Clamp(disc.X + disc.Width / 2 - side / 2, 0, image.PixelWidth - side);
            var y = Math.Clamp(disc.Y + disc.Height / 2 - side / 2, 0, image.PixelHeight - side);
            return new CoverParts(null, null, null, Save(new Int32Rect(x, y, side, side)));
        }

        var art = ArtArea(image, 0.5);
        var jewel = caseType?.Contains("jewel", StringComparison.OrdinalIgnoreCase) == true;
        var bluRay = caseType?.Contains("blu", StringComparison.OrdinalIgnoreCase) == true;

        // Lo que se detecta por los píxeles falla con dibujos claros (se confunden con el papel) o con zonas blancas dentro
        // del dibujo: se corrige con la proporción que tiene que tener la carátula.
        var expected = isBack ? (jewel ? JewelBack : DvdFront) : jewel ? 1.0 : bluRay ? BluRayWrap : DvdWrap;
        art = FitTemplate(art, image.PixelWidth, image.PixelHeight, expected);
        double w = art.Width, h = art.Height;

        if (isBack && jewel && w / h > 1.1)
        {
            // Trasera de jewel case: el centro es la contraportada y las tiras de los lados, los lomos.
            var center = (int)Math.Min(w, Math.Round(h * JewelBackCenter));
            var strip = (art.Width - center) / 2;
            var back = Save(new Int32Rect(art.X + strip, art.Y, center, art.Height));
            var spine = strip > 4 ? Save(new Int32Rect(art.X, art.Y, strip, art.Height)) : null;
            return new CoverParts(null, back, spine, null);
        }

        if (!isBack && w / h > 1.15)
        {
            // Carátula completa: contraportada a la izquierda, lomo en el centro y portada a la derecha.
            var frontAspect = caseType?.Contains("blu", StringComparison.OrdinalIgnoreCase) == true ? BluRayFront : DvdFront;
            var front = (int)Math.Round(Math.Min(h * frontAspect, w * 0.49));
            var spineWidth = art.Width - 2 * front;
            return new CoverParts(
                Save(new Int32Rect(art.X + art.Width - front, art.Y, front, art.Height)),
                Save(new Int32Rect(art.X, art.Y, front, art.Height)),
                spineWidth > 4 ? Save(new Int32Rect(art.X + front, art.Y, spineWidth, art.Height)) : null,
                null);
        }

        // Una sola cara (la portada de un jewel case, o una trasera suelta).
        var whole = Save(art);
        return isBack ? new CoverParts(null, whole, null, null) : new CoverParts(whole, null, null, null);
    }

    static BitmapSource Load(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.EndInit();
        BitmapSource source = image;
        if (source.Format != PixelFormats.Bgr32 && source.Format != PixelFormats.Bgra32)
            source = new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);
        source.Freeze();
        return source;
    }

    /// <summary>
    /// Zona con dibujo: las filas y columnas donde más de <paramref name="threshold"/> de los píxeles no son blancos,
    /// y de esas, el tramo seguido más largo (así no cuentan las guías ni las líneas de texto sueltas).
    /// </summary>
    public static Int32Rect ArtArea(BitmapSource image, double threshold)
    {
        // Se mide sobre una copia pequeña: es igual de exacto para esto y mucho más rápido.
        var scale = Math.Min(1.0, 900.0 / Math.Max(image.PixelWidth, image.PixelHeight));
        BitmapSource small = scale < 1 ? new TransformedBitmap(image, new ScaleTransform(scale, scale)) : image;
        var gray = new FormatConvertedBitmap(small, PixelFormats.Gray8, null, 0);
        int width = gray.PixelWidth, height = gray.PixelHeight;
        var pixels = new byte[width * height];
        gray.CopyPixels(pixels, width, 0);

        var columns = new int[width];
        var rows = new int[height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (pixels[y * width + x] < 235)
                {
                    columns[x]++;
                    rows[y]++;
                }
            }
        }

        var (x0, x1) = LongestRun(columns, height * threshold);
        var (y0, y1) = LongestRun(rows, width * threshold);
        if (x1 <= x0 || y1 <= y0)
            return new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight);

        var left = (int)Math.Floor(x0 / scale);
        var top = (int)Math.Floor(y0 / scale);
        var right = Math.Min(image.PixelWidth, (int)Math.Ceiling((x1 + 1) / scale));
        var bottom = Math.Min(image.PixelHeight, (int)Math.Ceiling((y1 + 1) / scale));
        return new Int32Rect(left, top, right - left, bottom - top);
    }

    static (int Start, int End) LongestRun(int[] counts, double minimum)
    {
        (int Start, int End) best = (0, -1);
        var start = -1;
        for (var i = 0; i <= counts.Length; i++)
        {
            var dense = i < counts.Length && counts[i] > minimum;
            if (dense && start < 0)
                start = i;
            else if (!dense && start >= 0)
            {
                if (i - 1 - start > best.End - best.Start)
                    best = (start, i - 1);
                start = -1;
            }
        }
        return best;
    }
}
