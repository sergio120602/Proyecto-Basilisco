using System.IO;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Basilisco.Services;

/// <summary>Convierte las páginas de un PDF en imágenes con el lector de PDF que trae Windows (para leer los manuales).</summary>
public static class PdfRenderer
{
    /// <summary>Número de páginas, o 0 si el PDF no se puede abrir.</summary>
    public static async Task<int> PageCountAsync(string path)
    {
        try
        {
            var document = await PdfDocument.LoadFromFileAsync(await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)));
            return (int)document.PageCount;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return 0;
        }
    }

    /// <summary>Las páginas pedidas como imágenes de ese alto en píxeles (ya congeladas: se pueden usar en cualquier hilo).</summary>
    public static async Task<List<BitmapSource>> RenderAsync(string path, int height, IEnumerable<int>? pages = null, CancellationToken cancellation = default)
    {
        var images = new List<BitmapSource>();
        PdfDocument document;
        try
        {
            document = await PdfDocument.LoadFromFileAsync(await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return images;
        }

        foreach (var index in pages ?? Enumerable.Range(0, (int)document.PageCount))
        {
            cancellation.ThrowIfCancellationRequested();
            if (index < 0 || index >= document.PageCount)
                continue;
            using var page = document.GetPage((uint)index);
            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, new PdfPageRenderOptions
            {
                DestinationHeight = (uint)height,
                DestinationWidth = (uint)Math.Round(height * page.Size.Width / page.Size.Height),
            });

            stream.Seek(0);
            var bytes = new byte[stream.Size];
            using (var reader = new DataReader(stream))
            {
                await reader.LoadAsync((uint)stream.Size);
                reader.ReadBytes(bytes);
                reader.DetachStream();
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = new MemoryStream(bytes);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            images.Add(image);
        }
        return images;
    }

    /// <summary>Guarda la primera página como PNG (para la portada del librito en la caja abierta). Devuelve su ruta o null.</summary>
    public static async Task<string?> SaveCoverAsync(string pdfPath, int height = 1400)
    {
        var pages = await RenderAsync(pdfPath, height, [0]);
        if (pages.Count == 0)
            return null;
        var folder = Path.Combine(Path.GetTempPath(), "Basilisco");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{Guid.NewGuid():N}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(pages[0]));
        await using var output = File.Create(path);
        encoder.Save(output);
        return path;
    }
}
