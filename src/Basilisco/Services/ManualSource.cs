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

    /// <summary>La página ocupa las dos del libro abierto (una imagen horizontal), en el modo libreta.</summary>
    public virtual bool IsSpread(int index) => false;

    /// <summary>El giro de la página en grados (0, 90, 180 o 270), en el modo de una sola página.</summary>
    public abstract int Rotation(int index);

    public abstract void SetRotation(int index, int degrees);
}

/// <summary>
/// Un PDF; los giros de sus páginas se guardan en <paramref name="rotations"/> (los del juego, o unos de usar y tirar).
/// Las hojas dobles (dos páginas escaneadas juntas) se parten por la mitad y se leen como dos páginas, la izquierda primero.
/// <paramref name="split"/>: null para detectarlas solo, true para partir todas las apaisadas, false para no partir.
/// </summary>
public sealed class PdfManual(string path, Dictionary<int, int> rotations, bool? split = null) : ManualSource
{
    // Cada página del libro: la hoja del PDF y cuál de sus mitades (0 entera, −1 la izquierda, 1 la derecha).
    List<(int Sheet, int Half)> _layout = [];

    public override string? PdfPath => path;

    /// <summary>True si alguna hoja se ha partido (tras <see cref="CountAsync"/>).</summary>
    public bool HasSplitSheets => _layout.Any(page => page.Half != 0);

    /// <summary>La hoja del PDF de esa página del libro.</summary>
    public int SheetOf(int index) => index >= 0 && index < _layout.Count ? _layout[index].Sheet : 0;

    /// <summary>La primera página del libro que sale de esa hoja del PDF.</summary>
    public int PageOfSheet(int sheet) => Math.Max(0, _layout.FindIndex(page => page.Sheet == sheet));

    public override int Rotation(int index) => rotations.GetValueOrDefault(index);

    public override void SetRotation(int index, int degrees)
    {
        if (degrees % 360 == 0)
            rotations.Remove(index);
        else
            rotations[index] = degrees % 360;
    }

    public override async Task<int> CountAsync()
    {
        var sheets = await PdfRenderer.PageCountAsync(path);
        var spreads = split == false || sheets == 0 ? [] : await ManualSpreads.DetectAsync(path, all: split == true);
        var layout = new List<(int Sheet, int Half)>();
        for (var sheet = 0; sheet < sheets; sheet++)
        {
            if (spreads.Contains(sheet))
                layout.AddRange([(sheet, -1), (sheet, 1)]);
            else
                layout.Add((sheet, 0));
        }
        _layout = layout;
        return layout.Count;
    }

    public override async Task<BitmapSource?> PageAsync(int index, int height, CancellationToken cancellation)
    {
        if (index < 0 || index >= _layout.Count)
            return null;
        var (sheet, half) = _layout[index];
        var pages = await PdfRenderer.RenderAsync(path, height, [sheet], cancellation);
        if (pages.Count == 0)
            return null;
        return half == 0 ? pages[0] : ManualSpreads.Half(pages[0], half);
    }
}

/// <summary>Las páginas de un manual de imágenes. Si alguna se soltó para ahorrar espacio, se vuelve a descargar al abrirlo.</summary>
public sealed class ImageManual(IReadOnlyList<ManualPage> pages) : ManualSource
{
    public override bool IsSpread(int index) => index >= 0 && index < pages.Count && pages[index].Spread;

    public override int Rotation(int index) => index >= 0 && index < pages.Count ? pages[index].Rotation : 0;

    public override void SetRotation(int index, int degrees)
    {
        if (index >= 0 && index < pages.Count)
            pages[index].Rotation = degrees % 360;
    }

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
