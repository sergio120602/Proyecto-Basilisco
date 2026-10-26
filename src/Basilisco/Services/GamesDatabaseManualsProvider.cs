using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Basilisco.Services;

/// <summary>
/// Manuales originales escaneados de Games Database (gamesdatabase.org): más de 20 000 manuales en PDF de consolas y
/// ordenadores clásicos. Basilisco busca en su catálogo, enseña la portada de la caja de cada juego y descarga el PDF del
/// manual directamente (la web lo enlaza desde la ficha del juego y deja entrar a los programas).
/// </summary>
public sealed partial class GamesDatabaseManualsProvider : ArtProvider
{
    const string NoKey = "-";
    const string Site = "https://www.gamesdatabase.org";

    /// <summary>Un juego de la lista de resultados: su nombre, su consola y su año.</summary>
    sealed record Result(string Name, string System, string? Year);

    readonly Dictionary<string, Result> _found = [];

    /// <summary>Consola del juego (de Basilisco), para poner primero los de esa consola.</summary>
    public string? PlatformHint { get; set; }

    public override string Name => "Games Database";
    public override string ImageKind => "manual";
    public override bool RequiresKey => false;
    public override string KeyHelp => "";
    public override string KeyUrl => Site + "/all_manuals";
    public override string? GetKey(AppSettings settings) => NoKey;
    public override void SetKey(AppSettings settings, string? key) { }

    public override string DownloadingText => "Descargando el manual en PDF de Games Database…";

    public override async Task<List<ArtGame>> SearchAsync(string term, string key, CancellationToken cancellation)
    {
        _found.Clear();
        foreach (var attempt in Attempts(term))
        {
            var html = await GetAsync($"/list.aspx?in=1&searchtext={Uri.EscapeDataString(attempt)}&searchtype=1", cancellation);
            foreach (Match row in Row().Matches(html))
            {
                var cells = Cell().Matches(row.Value).Select(cell => Text(cell.Groups[1].Value)).ToList();
                if (cells.Count < 10 || cells[1].Length == 0 || cells[3].Length == 0)
                    continue;
                var result = new Result(cells[1], cells[3], Regex.IsMatch(cells[9], @"^\d{4}$") ? cells[9] : null);
                _found.TryAdd(PageOf(result), result);
            }
            if (_found.Count > 0)
                break;
        }

        // Primero los de la consola del juego, y de ellos los que más se parecen a lo buscado.
        var wanted = SystemNames(PlatformHint);
        var words = Words(term);
        return _found
            .OrderByDescending(entry => wanted.Contains(Squash(entry.Value.System)))
            .ThenByDescending(entry => Words(entry.Value.Name).Count(words.Contains) - Words(entry.Value.Name).Count(word => !words.Contains(word)) * 0.1)
            .ThenBy(entry => entry.Value.Name)
            .Select(entry => new ArtGame(entry.Key, entry.Value.Name, entry.Value.Year is { } year ? $"{entry.Value.System} · {year}" : entry.Value.System))
            .ToList();
    }

    /// <summary>Los manuales de la ficha del juego (casi siempre uno), con la portada de su caja como vista previa.</summary>
    public override async Task<List<ArtImage>> GetImagesAsync(ArtGame game, string key, CancellationToken cancellation)
    {
        var html = await GetAsync(game.Id, cancellation);
        var cover = Image().Matches(html).Select(match => match.Groups["src"].Value)
                        .FirstOrDefault(src => src.Contains("/Box/", StringComparison.OrdinalIgnoreCase))
                    ?? Image().Matches(html).Select(match => match.Groups["src"].Value)
                        .FirstOrDefault(src => src.Contains("/Title/", StringComparison.OrdinalIgnoreCase));
        var thumb = cover is null ? "" : Absolute(cover);
        return Pdf().Matches(html).Select(match => Absolute(WebUtility.HtmlDecode(match.Groups[1].Value)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((url, index) => new ArtImage(url, thumb, 0, 0, index == 0 ? "Manual en PDF" : $"Manual en PDF ({index + 1})", Tall: true))
            .ToList();
    }

    /// <summary>Descarga el PDF (pueden ser varias decenas de MB) y comprueba que lo es.</summary>
    public override async Task<string> DownloadImageAsync(ArtImage image, CancellationToken cancellation)
    {
        var folder = Path.Combine(Path.GetTempPath(), "Basilisco");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{Guid.NewGuid():N}.pdf");
        var done = false;
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            limit.CancelAfter(TimeSpan.FromMinutes(5));
            using var response = await Http.GetAsync(image.Url, HttpCompletionOption.ResponseHeadersRead, limit.Token);
            if (!response.IsSuccessStatusCode)
                throw new ArtProviderException($"Games Database ha respondido con un error ({(int)response.StatusCode}) al descargar el manual.");
            await using (var source = await response.Content.ReadAsStreamAsync(limit.Token))
            await using (var file = File.Create(path))
                await source.CopyToAsync(file, limit.Token);
            var header = new byte[5];
            await using (var check = File.OpenRead(path))
                _ = await check.ReadAsync(header, cancellation);
            if (header is not [(byte)'%', (byte)'P', (byte)'D', (byte)'F', ..])
                throw new ArtProviderException("Lo que ha llegado de Games Database no es un PDF. Prueba con otro manual.");
            done = true;
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
        {
            throw new ArtProviderException("No se ha podido descargar el manual de Games Database. Comprueba tu conexión a internet.");
        }
        finally
        {
            // Lo descargado a medias (o lo que no era un PDF) no se queda en la carpeta temporal.
            if (!done)
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    /// <summary>La ficha de cada juego: /game/&lt;consola&gt;/&lt;nombre&gt;, en minúsculas y con guiones.</summary>
    static string PageOf(Result result) => $"/game/{Slug(result.System)}/{Slug(result.Name)}";

    static string Slug(string text) => Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", "-");

    static string Absolute(string href) => href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href : Site + (href.StartsWith('/') ? "" : "/") + href;

    static string Text(string html) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ").Trim();

    static IEnumerable<string> Attempts(string term)
    {
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var text = Regex.Replace(term.Trim(), @"[™®©]", "");
        var withoutThe = Regex.Replace(text, @"^the\s+", "", RegexOptions.IgnoreCase);
        // La web escribe «And» y quita los dos puntos («Ratchet And Clank - Size Matters»): se prueba también así y con solo lo de antes de ellos.
        var plain = Regex.Replace(withoutThe.Replace("&", "and"), @"[:\-–]", " ");
        var parts = withoutThe.Split(':', '-', '–');
        foreach (var candidate in new[] { text, withoutThe, Regex.Replace(plain, @"\s+", " ").Trim(), parts[0].Trim() })
        {
            if (candidate.Length > 1 && tried.Add(candidate))
                yield return candidate;
        }
    }

    /// <summary>Cómo llama Games Database a cada consola de Basilisco (sin espacios y en minúsculas).</summary>
    static string[] SystemNames(string? platform) => platform switch
    {
        "ps1" => ["sonyplaystation"],
        "ps2" => ["sonyplaystation2"],
        "ps3" => ["sonyplaystation3"],
        "psp" => ["sonypsp"],
        "xbox" => ["microsoftxbox"],
        "x360" => ["microsoftxbox360"],
        "n64" => ["nintendo64", "nintendon64"],
        "gc" => ["nintendogamecube"],
        "wii" => ["nintendowii"],
        "wiiu" => ["nintendowiiu"],
        "gb" => ["nintendogameboy", "nintendogameboycolor"],
        "gba" => ["nintendogameboyadvance"],
        "nds" => ["nintendods"],
        "nes" => ["nintendones"],
        "snes" => ["nintendosnes"],
        "switch" => ["nintendoswitch"],
        "arcade" => ["arcade"],
        "pc" => ["pc", "microsoftwindows"],
        _ => [],
    };

    /// <summary>Las palabras de un título, sin artículos («The», «A»…).</summary>
    static HashSet<string> Words(string text) =>
        LaunchBoxDatabase.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => word is not ("the" or "a" or "of" or "and")).ToHashSet();

    static string Squash(string text) => Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9]", "");

    static async Task<string> GetAsync(string path, CancellationToken cancellation)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Site + path);
            request.Headers.Accept.ParseAdd("text/html");
            using var response = await Http.SendAsync(request, cancellation);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return "";
            if (!response.IsSuccessStatusCode)
                throw new ArtProviderException($"Games Database ha respondido con un error ({(int)response.StatusCode}). Prueba más tarde.");
            return await response.Content.ReadAsStringAsync(cancellation);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
        {
            throw new ArtProviderException("No se ha podido conectar con Games Database. Comprueba tu conexión a internet.");
        }
    }

    // Cada fila de resultados: la de un juego lleva el enlace «GAME$n» y sus celdas (nombre, consola, editora, desarrolladora, tipo y año).
    [GeneratedRegex(@"<tr[^>]*>(?:(?!</tr>).)*?GAME\$\d+(?:(?!</tr>).)*</tr>", RegexOptions.Singleline)]
    private static partial Regex Row();

    [GeneratedRegex(@"<td[^>]*>(.*?)</td>", RegexOptions.Singleline)]
    private static partial Regex Cell();

    [GeneratedRegex(@"href=""([^""]+\.pdf)""", RegexOptions.IgnoreCase)]
    private static partial Regex Pdf();

    [GeneratedRegex(@"<img[^>]+src=""(?<src>/Media/[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex Image();
}
