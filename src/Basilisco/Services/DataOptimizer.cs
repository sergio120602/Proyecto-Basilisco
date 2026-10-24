using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Basilisco.Models;

namespace Basilisco.Services;

/// <summary>Lo que se puede ahorrar (o se ha ahorrado) en basilisco-data.</summary>
/// <param name="Large">Imágenes más grandes de lo que Basilisco llega a enseñar.</param>
/// <param name="Orphans">Archivos que ya no usa ningún juego, filtro ni consola.</param>
public sealed record DataReport(List<string> Large, long LargeBytes, List<string> Orphans, long OrphanBytes, long SavedBytes = 0)
{
    public bool IsEmpty => Large.Count == 0 && Orphans.Count == 0;
}

/// <summary>
/// Aligera la carpeta basilisco-data: reduce las imágenes que son mucho más grandes de lo que se ve en pantalla
/// (con el mismo formato y nombre) y borra los archivos que ya no usa nada (portadas, fondos o manuales de juegos
/// quitados o cambiados). Con menos píxeles que leer, el carrusel y los menús cargan antes y gastan menos memoria.
/// </summary>
public static class DataOptimizer
{
    /// <summary>Tamaño máximo (el lado más largo) que llega a usar Basilisco en cada carpeta.</summary>
    static readonly (string Folder, int MaxSide)[] Limits =
    [
        (AppInfo.CoversDirectory, 2560),          // portadas, lomos, discos e interiores (este, de lado a lado de la caja)
        (AppInfo.BackgroundsDirectory, 2560),     // fondos de pantalla
        (AppInfo.FilterImagesDirectory, 1024),    // logotipos de los filtros
        (AppInfo.PlatformImagesDirectory, 1280),  // fotos de las consolas
    ];

    public static DataReport Analyze(IReadOnlyList<Game> games, IReadOnlyList<GameFilter> filters, AppSettings settings)
    {
        var used = UsedFiles(games, filters, settings);
        var large = new List<string>();
        var orphans = new List<string>();
        long largeBytes = 0, orphanBytes = 0;

        foreach (var (folder, maxSide) in Limits.Append((AppInfo.ManualsDirectory, 0)))
        {
            if (!Directory.Exists(folder))
                continue;
            foreach (var path in Directory.EnumerateFiles(folder))
            {
                var length = new FileInfo(path).Length;
                if (!used.Contains(Path.GetFullPath(path)))
                {
                    orphans.Add(path);
                    orphanBytes += length;
                }
                else if (maxSide > 0 && CanShrink(path) && Size(path) is { } size && Math.Max(size.Width, size.Height) > maxSide * 1.15)
                {
                    large.Add(path);
                    largeBytes += length;
                }
            }
        }
        return new DataReport(large, largeBytes, orphans, orphanBytes);
    }

    /// <summary>Reduce las imágenes grandes y borra los archivos sin usar. Devuelve lo que se ha ahorrado.</summary>
    public static DataReport Apply(DataReport report)
    {
        long saved = 0;
        foreach (var path in report.Large)
        {
            var maxSide = Limits.First(limit => Path.GetFullPath(path).StartsWith(Path.GetFullPath(limit.Folder), StringComparison.OrdinalIgnoreCase)).MaxSide;
            saved += Shrink(path, maxSide);
        }
        foreach (var path in report.Orphans)
        {
            try
            {
                var length = new FileInfo(path).Length;
                File.Delete(path);
                saved += length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        return report with { SavedBytes = saved };
    }

    /// <summary>Todos los archivos que usa la biblioteca (rutas completas).</summary>
    static HashSet<string> UsedFiles(IReadOnlyList<Game> games, IReadOnlyList<GameFilter> filters, AppSettings settings)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string folder, string? file)
        {
            if (file is { Length: > 0 })
                used.Add(Path.GetFullPath(Path.Combine(folder, file)));
        }

        foreach (var game in games)
        {
            foreach (var faces in new[] { game.Square, game.Portrait })
            {
                if (faces is null)
                    continue;
                Add(AppInfo.CoversDirectory, faces.Front);
                Add(AppInfo.CoversDirectory, faces.Back);
                Add(AppInfo.CoversDirectory, faces.Spine);
            }
            Add(AppInfo.CoversDirectory, game.Disc);
            Add(AppInfo.CoversDirectory, game.Manual);
            Add(AppInfo.CoversDirectory, game.Inside);
            Add(AppInfo.BackgroundsDirectory, game.Background);
            Add(AppInfo.ManualsDirectory, game.ManualPdf);
            Add(AppInfo.CoversDirectory, game.Soundtrack?.Disc);
        }
        foreach (var filter in filters)
            Add(AppInfo.FilterImagesDirectory, filter.Image);
        Add(AppInfo.BackgroundsDirectory, settings.Background);

        // De cada consola vale la imagen más reciente; las anteriores son restos.
        foreach (var platform in Platforms.All)
        {
            if (ImageStore.GetPlatformImagePath(platform.Id) is { } image)
                used.Add(Path.GetFullPath(image));
        }
        return used;
    }

    static bool CanShrink(string path) => Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".tif" or ".tiff";

    static (int Width, int Height)? Size(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0];
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
                                   or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// <summary>Vuelve a guardar la imagen con su lado más largo en <paramref name="maxSide"/>, en el mismo formato. Devuelve los bytes ahorrados.</summary>
    static long Shrink(string path, int maxSide)
    {
        try
        {
            var original = new FileInfo(path).Length;
            BitmapSource image;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var decoded = BitmapFrame.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                var scale = (double)maxSide / Math.Max(decoded.PixelWidth, decoded.PixelHeight);
                image = scale < 1 ? new TransformedBitmap(decoded, new ScaleTransform(scale, scale)) : decoded;
                image.Freeze();
            }

            BitmapEncoder encoder = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 92 },
                ".bmp" => new BmpBitmapEncoder(),
                ".tif" or ".tiff" => new TiffBitmapEncoder(),
                _ => new PngBitmapEncoder(),
            };
            encoder.Frames.Add(BitmapFrame.Create(image));
            var temp = path + ".tmp";
            using (var output = File.Create(temp))
                encoder.Save(output);

            var smaller = new FileInfo(temp).Length;
            if (smaller >= original)
            {
                File.Delete(temp);
                return 0;
            }
            File.Move(temp, path, overwrite: true);
            return original - smaller;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
                                   or System.Runtime.InteropServices.COMException)
        {
            return 0;
        }
    }
}
