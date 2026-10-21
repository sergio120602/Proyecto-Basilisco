using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Basilisco.Services;

/// <summary>
/// The Spine Archive (thespinearchive.com): más de 11 000 lomos hechos por la comunidad (r/SwitchSpines), pensados
/// para las cajas de Switch pero válidos para cualquier caja. No necesita clave: su catálogo es un JSON público que
/// se guarda en basilisco-data/spinearchive y se vuelve a descargar si tiene más de una semana.
/// </summary>
public sealed partial class SpineArchiveProvider : ArtProvider
{
    const string NoKey = "-";
    const string CatalogUrl = "https://thespinearchive.com/database.json";
    static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    static readonly string Folder = Path.Combine(AppInfo.DataDirectory, "spinearchive");
    static readonly string CatalogPath = Path.Combine(Folder, "database.json");
    static readonly SemaphoreSlim Lock = new(1, 1);

    /// <summary>Un lomo del catálogo.</summary>
    sealed record Spine(string Game, string Normalized, string[] Words, string Author, string ImageUrl, string? Platform);

    static List<Spine>? _spines;

    public override string Name => "The Spine Archive";
    public override string ImageKind => "lomo";
    public override bool RequiresKey => false;
    public override string KeyHelp => "";
    public override string KeyUrl => "https://thespinearchive.com/";
    public override string? GetKey(AppSettings settings) => NoKey;
    public override void SetKey(AppSettings settings, string? key) { }

    public override string? MaintenanceLabel => "Actualizar catálogo";

    public override Task PrepareAsync(IProgress<string> status, CancellationToken cancellation) =>
        EnsureReadyAsync(status, cancellation, forceDownload: false);

    public override Task RunMaintenanceAsync(IProgress<string> status, CancellationToken cancellation) =>
        EnsureReadyAsync(status, cancellation, forceDownload: true);

    static async Task EnsureReadyAsync(IProgress<string> status, CancellationToken cancellation, bool forceDownload)
    {
        await Lock.WaitAsync(cancellation);
        try
        {
            var stale = !File.Exists(CatalogPath) || DateTime.Now - File.GetLastWriteTime(CatalogPath) > MaxAge;
            if (forceDownload || stale)
            {
                status.Report("Descargando el catálogo de The Spine Archive…");
                try
                {
                    var json = await Http.GetByteArrayAsync(CatalogUrl, cancellation);
                    Directory.CreateDirectory(Folder);
                    var temp = CatalogPath + ".tmp";
                    await File.WriteAllBytesAsync(temp, json, cancellation);
                    File.Move(temp, CatalogPath, overwrite: true);
                    _spines = null;
                }
                catch (Exception ex) when (IsOffline(ex, cancellation) && File.Exists(CatalogPath) && !forceDownload)
                {
                    // Sin internet: sirve el catálogo que ya había, aunque sea viejo.
                }
                catch (Exception ex) when (IsOffline(ex, cancellation))
                {
                    throw new ArtProviderException("No se ha podido descargar el catálogo de The Spine Archive. Comprueba tu conexión a internet.");
                }
            }

            if (_spines is null)
            {
                status.Report("Cargando el catálogo de The Spine Archive…");
                _spines = await Task.Run(Load, cancellation);
            }
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>Sin conexión o sin respuesta a tiempo (no porque se haya cancelado la búsqueda).</summary>
    static bool IsOffline(Exception ex, CancellationToken cancellation) =>
        ex is HttpRequestException || ex is TaskCanceledException && !cancellation.IsCancellationRequested;

    static List<Spine> Load()
    {
        var spines = new List<Spine>();
        try
        {
            using var stream = File.OpenRead(CatalogPath);
            using var json = JsonDocument.Parse(stream);
            if (json.RootElement.ValueKind != JsonValueKind.Array)
                return spines;

            foreach (var entry in json.RootElement.EnumerateArray())
            {
                if (GetString(entry, "title") is not { Length: > 0 } title || ImageUrl(entry) is not { } url)
                    continue;
                var author = GetString(entry, "author") ?? "";
                var game = GameTitle(title, author);
                var normalized = Normalize(game);
                var platform = entry.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object ? GetString(tags, "Platform") : null;
                spines.Add(new Spine(game, normalized, normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries), author, url, platform));
            }
        }
        catch (JsonException)
        {
            throw new ArtProviderException("El catálogo de The Spine Archive está dañado. Pulsa «Actualizar catálogo».");
        }
        return spines;
    }

    const string Cdn = "https://cdn.thespinearchive.com/file/the-spine-archive-storage/";

    static string? ImageUrl(JsonElement entry)
    {
        // Los más antiguos apuntan a cdn.thespinearchive.xyz, que ya no responde; los mismos archivos están en el .com.
        if (GetString(entry, "image") is { } image && image.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return image.Replace("cdn.thespinearchive.xyz", "cdn.thespinearchive.com", StringComparison.OrdinalIgnoreCase);
        // Algunos solo traen la ruta («/spines/abc.webp»).
        return GetString(entry, "src") is { Length: > 0 } src ? Cdn + src.TrimStart('/') : null;
    }

    /// <summary>«Stray by Lewcifer820» → «Stray» (el autor va aparte).</summary>
    static string GameTitle(string title, string author)
    {
        var name = author.StartsWith("u/", StringComparison.Ordinal) ? author[2..] : author;
        var by = title.LastIndexOf(" by ", StringComparison.OrdinalIgnoreCase);
        if (by > 0 && (name.Length == 0 || title[(by + 4)..].Trim().Equals(name, StringComparison.OrdinalIgnoreCase)))
            return title[..by].Trim();
        return by > 0 ? title[..by].Trim() : title.Trim();
    }

    /// <summary>Como en LaunchBox, y además los números romanos en cifras: «The Witcher III» y «The Witcher 3» son lo mismo.</summary>
    static string Normalize(string text) =>
        RomanNumeral().Replace(LaunchBoxDatabase.Normalize(text), match => match.Value switch
        {
            "ii" => "2", "iii" => "3", "iv" => "4", "v" => "5", "vi" => "6",
            "vii" => "7", "viii" => "8", "ix" => "9", "x" => "10", _ => match.Value,
        });

    // Palabras que no cuentan para decidir si un lomo es del juego buscado.
    static readonly HashSet<string> Filler = ["the", "a", "of", "and", "edition", "el", "la", "los", "las", "de", "y"];

    public override Task<List<ArtGame>> SearchAsync(string term, string key, CancellationToken cancellation)
    {
        if (_spines is not { } spines)
            return Task.FromResult(new List<ArtGame>());

        var query = Normalize(term);
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => !Filler.Contains(word)).ToArray();
        if (words.Length == 0)
            return Task.FromResult(new List<ArtGame>());

        return Task.Run(() =>
        {
            // Un resultado por juego (el mismo juego tiene lomos de muchos autores).
            var best = new Dictionary<string, (int Score, string Name, int Count)>();
            foreach (var spine in spines)
            {
                var matched = words.Count(word => spine.Words.Contains(word));
                // Todas las palabras, o en búsquedas largas al menos la mitad («The Witcher 3: Wild Hunt» encuentra «The Witcher 3»).
                if (matched < words.Length && (words.Length < 3 || matched * 2 < words.Length))
                    continue;

                var score = spine.Normalized == query ? 1000 : spine.Normalized.StartsWith(query, StringComparison.Ordinal) ? 600 : 300;
                score += matched * 50 - Math.Min(100, Math.Abs(spine.Normalized.Length - query.Length) * 2);
                var count = best.TryGetValue(spine.Normalized, out var previous) ? previous.Count + 1 : 1;
                best[spine.Normalized] = (Math.Max(score, previous.Score), previous.Name ?? spine.Game, count);
            }

            return best
                .OrderByDescending(entry => entry.Value.Score).ThenBy(entry => entry.Value.Name)
                .Take(60)
                .Select(entry => new ArtGame(entry.Key, entry.Value.Name, entry.Value.Count == 1 ? "1 lomo" : $"{entry.Value.Count} lomos"))
                .ToList();
        }, cancellation);
    }

    public override Task<List<ArtImage>> GetImagesAsync(ArtGame game, string key, CancellationToken cancellation) =>
        Task.FromResult(_spines?
            .Where(spine => spine.Normalized == game.Id)
            .Select(spine => new ArtImage(spine.ImageUrl, spine.ImageUrl, 0, 0,
                string.Join(" · ", new[] { spine.Author, spine.Platform }.Where(text => !string.IsNullOrEmpty(text))), Tall: true))
            .ToList() ?? []);

    /// <summary>
    /// Quita de un lomo la franja de arriba con el logotipo de Nintendo Switch (para los juegos de otras consolas).
    /// La franja es de un solo color de lado a lado (casi siempre rojo) con el logotipo en blanco encima: se buscan las
    /// filas seguidas desde arriba que son de ese color y se corta justo debajo. En los lomos de Switch ocupa del 12 al
    /// 17 % de la altura; si no se encuentra una así, el lomo se deja como está. Devuelve la ruta del lomo (temporal).
    /// </summary>
    public static string HideConsoleLogo(string path)
    {
        BitmapSource image;
        try
        {
            var decoded = new BitmapImage();
            decoded.BeginInit();
            decoded.UriSource = new Uri(path, UriKind.Absolute);
            decoded.CacheOption = BitmapCacheOption.OnLoad;
            decoded.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            decoded.EndInit();
            image = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
            image.Freeze();
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or InvalidOperationException
                                   or System.Runtime.InteropServices.COMException)
        {
            return path;
        }

        int width = image.PixelWidth, height = image.PixelHeight;
        if (height < width * 4)
            return path; // No es un lomo.
        var stride = width * 4;
        var pixels = new byte[stride * height];
        image.CopyPixels(pixels, stride, 0);

        // Color de la franja: el de una fila de arriba (la mediana de cada canal, para que no cuente el logotipo).
        var top = Math.Max(1, height / 200);
        byte Median(int channel)
        {
            var values = new byte[width];
            for (var x = 0; x < width; x++)
                values[x] = pixels[top * stride + x * 4 + channel];
            Array.Sort(values);
            return values[width / 2];
        }
        int b = Median(0), g = Median(1), r = Median(2);

        // Filas de ese color (al menos un 30 %: el resto es el logotipo); se aceptan huecos cortos entre las letras.
        var end = -1;
        var misses = 0;
        var maxGap = Math.Max(2, height / 100);
        for (var y = top; y < height / 4; y++)
        {
            var same = 0;
            for (var x = 0; x < width; x++)
            {
                var o = y * stride + x * 4;
                if (Math.Abs(pixels[o] - b) + Math.Abs(pixels[o + 1] - g) + Math.Abs(pixels[o + 2] - r) < 90)
                    same++;
            }
            if (same >= width * 0.3)
            {
                end = y;
                misses = 0;
            }
            else if (++misses > maxGap)
            {
                break;
            }
        }

        var band = (end + 1) / (double)height;
        if (band < 0.08 || band > 0.22)
            return path;

        // La franja mide siempre lo mismo (la plantilla de Switch 2 y la de Switch 1): si el dibujo de debajo es de un color
        // parecido, se ha contado de más, así que se ajusta a la medida de la plantilla más cercana.
        foreach (var template in new[] { 0.126, 0.166 })
        {
            if (Math.Abs(band - template) <= 0.025)
                band = Math.Min(band, template);
        }
        var cut = Math.Min(height - 1, (int)Math.Round(band * height) + height / 250);
        var cropped = new CroppedBitmap(image, new Int32Rect(0, cut, width, height - cut));
        var folder = Path.Combine(Path.GetTempPath(), "Basilisco");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"{Guid.NewGuid():N}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(cropped));
        using (var stream = File.Create(file))
            encoder.Save(stream);
        return file;
    }

    [GeneratedRegex(@"\b(ii|iii|iv|v|vi|vii|viii|ix|x)\b")]
    private static partial Regex RomanNumeral();
}
