using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using Basilisco.Models;

namespace Basilisco.Services;

/// <summary>
/// Las páginas de los manuales hechos con imágenes (de Pinterest): cada una es un JPEG en basilisco-data/manuals/pages,
/// guardado al alto al que se lee (1600 px, calidad 82; unos 250-400 KB), con el nombre sacado de su contenido para que
/// una imagen repetida en varios manuales ocupe una sola vez. Hay un tope de espacio: si se pasa, se sueltan las páginas
/// de los manuales que llevas más tiempo sin abrir (se recuerda de dónde salieron y se vuelven a descargar al abrirlos).
/// </summary>
public static class ManualPages
{
    public const int PageHeight = 1600;
    const int Quality = 82;

    public static string Folder { get; } = Path.Combine(AppInfo.ManualsDirectory, "pages");

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Basilisco", AppInfo.Version));
        return client;
    }

    public static string PathOf(ManualPage page) => Path.Combine(Folder, page.Hash + ".jpg");

    public static bool IsStored(ManualPage page) => File.Exists(PathOf(page));

    /// <summary>Descarga las imágenes en ese orden y las guarda optimizadas. Las que no se puedan descargar se saltan.</summary>
    public static async Task<List<ManualPage>> ImportAsync(IReadOnlyList<string> urls, IProgress<int>? progress, CancellationToken cancellation)
    {
        Directory.CreateDirectory(Folder);
        var pages = new List<ManualPage>();
        for (var i = 0; i < urls.Count; i++)
        {
            progress?.Report(i);
            if (await DownloadAsync(urls[i], null, cancellation) is { } page)
                pages.Add(page);
        }
        progress?.Report(urls.Count);
        return pages;
    }

    /// <summary>
    /// Descarga una imagen (si la original no está, la versión grande de Pinterest), la reduce a 1600 px de alto si es más
    /// alta y la guarda en JPEG. Devuelve la página, o null si no se ha podido.
    /// </summary>
    static async Task<ManualPage?> DownloadAsync(string url, string? expectedHash, CancellationToken cancellation)
    {
        byte[]? bytes = null;
        foreach (var candidate in Candidates(url))
        {
            try
            {
                using var response = await Http.GetAsync(candidate, cancellation);
                if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType?.StartsWith("image/") == true)
                {
                    bytes = await response.Content.ReadAsByteArrayAsync(cancellation);
                    break;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
            {
            }
        }
        if (bytes is null)
            return null;

        var hash = expectedHash ?? Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant()[..20];
        var target = Path.Combine(Folder, hash + ".jpg");
        try
        {
            return await Task.Run(() =>
            {
                using var input = new MemoryStream(bytes);
                var frame = BitmapDecoder.Create(input, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
                BitmapSource image = frame;
                if (frame.PixelHeight > PageHeight)
                {
                    var scale = (double)PageHeight / frame.PixelHeight;
                    image = new TransformedBitmap(frame, new System.Windows.Media.ScaleTransform(scale, scale));
                }
                if (!File.Exists(target))
                {
                    var encoder = new JpegBitmapEncoder { QualityLevel = Quality };
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    var temp = target + ".tmp";
                    using (var output = File.Create(temp))
                        encoder.Save(output);
                    File.Move(temp, target, overwrite: true);
                }
                return new ManualPage { Hash = hash, Source = url, Width = image.PixelWidth, Height = image.PixelHeight };
            }, cancellation);
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or ArgumentException or InvalidOperationException
                                   or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>La original de Pinterest primero (i.pinimg.com/originals/…) y, si no está, la de 736 px de ancho.</summary>
    static IEnumerable<string> Candidates(string url)
    {
        var match = System.Text.RegularExpressions.Regex.Match(url, @"^(https://i\.pinimg\.com/)(?:\d+x|originals)(/.+)$");
        if (!match.Success)
        {
            yield return url;
            yield break;
        }
        yield return match.Groups[1].Value + "originals" + match.Groups[2].Value;
        yield return match.Groups[1].Value + "736x" + match.Groups[2].Value;
    }

    /// <summary>Vuelve a descargar las páginas que se soltaron para ahorrar espacio. Devuelve cuántas faltan aún.</summary>
    public static async Task<int> EnsureAsync(IReadOnlyList<ManualPage> pages, CancellationToken cancellation)
    {
        Directory.CreateDirectory(Folder);
        var missing = 0;
        foreach (var page in pages.Where(page => !IsStored(page)))
        {
            if (page.Source is not { } source || await DownloadAsync(source, page.Hash, cancellation) is null)
                missing++;
        }
        return missing;
    }

    /// <summary>Lo que ocupan ahora todas las páginas guardadas, en bytes.</summary>
    public static long TotalBytes() =>
        Directory.Exists(Folder) ? Directory.EnumerateFiles(Folder, "*.jpg").Sum(file => new FileInfo(file).Length) : 0;

    /// <summary>
    /// Respeta el tope de espacio: si las páginas ocupan más, se borran las de los manuales abiertos hace más tiempo (las
    /// que no usa ningún otro manual), salvo las del juego indicado. Devuelve los bytes liberados.
    /// </summary>
    public static long EnforceLimit(IReadOnlyList<Game> games, long limitBytes, Game? keep)
    {
        var total = TotalBytes();
        if (total <= limitBytes)
            return 0;
        var freed = 0L;
        var withPages = games.Where(game => game.ManualPages is { Count: > 0 }).ToList();
        foreach (var game in withPages.Where(game => game != keep).OrderBy(game => game.ManualOpened ?? DateTime.MinValue))
        {
            if (total - freed <= limitBytes)
                break;
            var inUse = withPages.Where(other => other != game).SelectMany(other => other.ManualPages!).Select(page => page.Hash).ToHashSet();
            foreach (var page in game.ManualPages!.Where(page => !inUse.Contains(page.Hash) && IsStored(page) && page.Source is not null))
            {
                try
                {
                    var length = new FileInfo(PathOf(page)).Length;
                    File.Delete(PathOf(page));
                    freed += length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        return freed;
    }

    /// <summary>Borra los archivos de páginas que ya no usa ningún manual.</summary>
    public static void RemoveUnused(IReadOnlyList<Game> games)
    {
        if (!Directory.Exists(Folder))
            return;
        var used = games.Where(game => game.ManualPages is not null).SelectMany(game => game.ManualPages!).Select(page => page.Hash).ToHashSet();
        foreach (var file in Directory.EnumerateFiles(Folder, "*.jpg"))
        {
            if (used.Contains(Path.GetFileNameWithoutExtension(file)))
                continue;
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
