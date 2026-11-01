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

    /// <summary>El ancho y el alto de cada página (sin dibujarlas), o una lista vacía si el PDF no se puede abrir.</summary>
    public static async Task<List<System.Windows.Size>> PageSizesAsync(string path)
    {
        var sizes = new List<System.Windows.Size>();
        try
        {
            var document = await PdfDocument.LoadFromFileAsync(await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)));
            for (uint index = 0; index < document.PageCount; index++)
            {
                using var page = document.GetPage(index);
                sizes.Add(new System.Windows.Size(page.Size.Width, page.Size.Height));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
        }
        return sizes;
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
        // Si la primera hoja es doble, su mitad derecha (cuando es la contraportada con la portada, la portada).
        var cover = (await ManualSpreads.DetectAsync(pdfPath)).Contains(0) ? ManualSpreads.Half(pages[0], 1) : pages[0];
        var folder = Path.Combine(Path.GetTempPath(), "Basilisco");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{Guid.NewGuid():N}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(cover));
        await using var output = File.Create(path);
        encoder.Save(output);
        return path;
    }
}
