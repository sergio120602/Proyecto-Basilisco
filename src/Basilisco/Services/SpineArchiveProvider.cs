using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

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

    [GeneratedRegex(@"\b(ii|iii|iv|v|vi|vii|viii|ix|x)\b")]
    private static partial Regex RomanNumeral();
}
