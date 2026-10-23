using System.IO;
using System.Windows.Media.Imaging;
using Basilisco.Models;

namespace Basilisco.Services;

/// <summary>De dónde salen las páginas de un manual para el libro: un PDF o las imágenes guardadas (de Pinterest).</summary>
public abstract class ManualSource
{
    public abstract Task<int> CountAsync();

    /// <summary>Una página a ese alto en píxeles (ya congelada), o null si no se puede.</summary>
    public abstract Task<BitmapSource?> PageAsync(int index, int height, CancellationToken cancellation);

    /// <summary>El PDF, si lo es (para abrirlo con el visor de Windows).</summary>
    public virtual string? PdfPath => null;
}

public sealed class PdfManual(string path) : ManualSource
{
    public override string? PdfPath => path;

    public override Task<int> CountAsync() => PdfRenderer.PageCountAsync(path);

    public override async Task<BitmapSource?> PageAsync(int index, int height, CancellationToken cancellation)
    {
        var pages = await PdfRenderer.RenderAsync(path, height, [index], cancellation);
        return pages.Count > 0 ? pages[0] : null;
    }
}

/// <summary>Las páginas de un manual de imágenes. Si alguna se soltó para ahorrar espacio, se vuelve a descargar al abrirlo.</summary>
public sealed class ImageManual(IReadOnlyList<ManualPage> pages) : ManualSource
{
    public override async Task<int> CountAsync()
    {
        await ManualPages.EnsureAsync(pages, CancellationToken.None);
        return pages.Count;
    }

    public override Task<BitmapSource?> PageAsync(int index, int height, CancellationToken cancellation)
    {
        if (index < 0 || index >= pages.Count)
            return Task.FromResult<BitmapSource?>(null);
        var path = ManualPages.PathOf(pages[index]);
        return Task.Run(() =>
        {
            if (!File.Exists(path))
                return null;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                if (pages[index].Height > height)
                    image.DecodePixelHeight = height;
                image.EndInit();
                image.Freeze();
                return (BitmapSource?)image;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or InvalidOperationException
                                       or System.Runtime.InteropServices.COMException)
            {
                return null;
            }
        }, cancellation);
    }
}
