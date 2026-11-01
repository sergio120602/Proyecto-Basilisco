using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Basilisco.Services;

/// <summary>
/// Hojas dobles de los manuales en PDF: muchos escaneos (los de Games Database, por ejemplo) traen dos páginas del librito
/// en cada hoja apaisada. Se detectan para partirlas por la mitad y leerlas como páginas sueltas.
/// </summary>
public static class ManualSpreads
{
    /// <summary>Más ancha que alta por encima de esto: una hoja apaisada (candidata a doble).</summary>
    const double Landscape = 1.15;

    /// <summary>
    /// Las hojas dobles del PDF (sus números, desde 0). Si el manual tiene también hojas verticales (las páginas
    /// sueltas), son dobles las apaisadas que miden como dos de ellas juntas. Si todas son apaisadas puede ser un manual
    /// apaisado de verdad (los de Super Nintendo europeos, por ejemplo): solo son dobles si en la mayoría de las que se
    /// miran se ve el pliegue del centro (una franja vertical lisa: el margen blanco entre las dos páginas o la sombra
    /// del doblez). Con <paramref name="all"/> (el usuario lo ha pedido), todas las apaisadas son dobles.
    /// </summary>
    public static async Task<HashSet<int>> DetectAsync(string path, bool all = false)
    {
        var sizes = await PdfRenderer.PageSizesAsync(path);
        var wide = Enumerable.Range(0, sizes.Count).Where(index => Ratio(sizes[index]) > Landscape).ToList();
        if (wide.Count == 0 || all)
            return [.. wide];

        var upright = sizes.Select(Ratio).Where(ratio => ratio < 0.95).Order().ToList();
        if (upright.Count > 0)
        {
            var single = upright[upright.Count / 2];
            return [.. wide.Where(index => Math.Abs(Ratio(sizes[index]) / (single * 2) - 1) < 0.25)];
        }

        // Todas apaisadas: se mira el pliegue en unas pocas, repartidas por el manual (sin la portada, que suele ser distinta).
        var sample = wide.Count <= 6 ? wide : Enumerable.Range(0, 6).Select(step => wide[1 + step * (wide.Count - 2) / 5]).Distinct().ToList();
        var folds = 0;
        foreach (var image in await PdfRenderer.RenderAsync(path, 360, sample))
        {
            if (HasFold(image))
                folds++;
        }
        return folds * 2 > sample.Count ? [.. wide] : [];
    }

    static double Ratio(Size size) => size.Height > 0 ? size.Width / size.Height : 0;

    /// <summary>
    /// True si en el centro de la hoja hay una columna lisa: sin texto ni dibujos que la crucen (mucho menos variada que
    /// las columnas normales de la hoja), o más oscura que el resto y también lisa (la sombra del doblez).
    /// </summary>
    internal static bool HasFold(BitmapSource image)
    {
        var gray = new FormatConvertedBitmap(image, PixelFormats.Gray8, null, 0);
        int width = gray.PixelWidth, height = gray.PixelHeight;
        if (width < 40 || height < 40)
            return false;
        var stride = (width + 3) / 4 * 4;
        var pixels = new byte[stride * height];
        gray.CopyPixels(pixels, stride, 0);

        // Media y desviación de cada columna, sin los bordes de arriba y abajo (el filo del escaneo).
        var means = new double[width];
        var spreads = new double[width];
        int top = height / 20, bottom = height - height / 20;
        for (var x = 0; x < width; x++)
        {
            double sum = 0, squares = 0;
            for (var y = top; y < bottom; y++)
            {
                double value = pixels[y * stride + x];
                sum += value;
                squares += value * value;
            }
            var count = bottom - top;
            means[x] = sum / count;
            spreads[x] = Math.Sqrt(Math.Max(0, squares / count - means[x] * means[x]));
        }

        var body = Enumerable.Range(width * 15 / 100, width * 70 / 100).ToList();
        var typicalSpread = Median(body.Select(x => spreads[x]));
        var typicalMean = Median(body.Select(x => means[x]));
        if (typicalSpread < 4)
            return false; // Una hoja casi vacía: no se puede saber.
        for (var x = width * 46 / 100; x <= width * 54 / 100; x++)
        {
            if (spreads[x] < typicalSpread * 0.4 || means[x] < typicalMean * 0.8 && spreads[x] < typicalSpread * 0.8)
                return true;
        }
        return false;
    }

    static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }

    /// <summary>La mitad izquierda (−1) o derecha (1) de una hoja, ya congelada.</summary>
    public static BitmapSource Half(BitmapSource image, int half)
    {
        var width = image.PixelWidth / 2;
        var cropped = new CroppedBitmap(image, new Int32Rect(half < 0 ? 0 : image.PixelWidth - width, 0, width, image.PixelHeight));
        cropped.Freeze();
        return cropped;
    }
}
