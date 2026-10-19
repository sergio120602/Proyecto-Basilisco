using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Basilisco.Services;

/// <summary>
/// Steam Game Covers (steamgamecovers.com): carátulas para imprimir hechas por la comunidad, de cajas de DVD, Blu-ray
/// y jewel case, y pegatinas de disco. No tiene API (su /api/ está cerrada a los robots), así que se lee la web como un
/// navegador: la búsqueda, la página de cada juego y la descarga, que se pide desde la página de la carátula.
/// Las carátulas completas (contraportada, lomo y portada en una imagen) se separan en sus partes al descargarlas.
/// </summary>
public sealed partial class SteamGameCoversProvider(ArtKind kind) : ArtProvider
{
    const string NoKey = "-";
    const string Site = "https://www.steamgamecovers.com/";
    const string Images = "https://images.steamgamecovers.com/";

    /// <summary>Una carátula de la web.</summary>
    /// <param name="Extension">Lo que pide la descarga: 1 si es .jpg, 2 si es .png.</param>
    sealed record Cover(string Slug, string Id, int Extension, string? CaseType, string? Location, bool IsDisc, int Stars);

    /// <summary>Todas las partes de la última carátula descargada, para rellenar las demás caras si están vacías.</summary>
    public CoverParts? LastParts { get; private set; }

    public override string Name => "Steam Game Covers";

    public override string ImageKind => kind switch
    {
        ArtKind.Front => "portada",
        ArtKind.Back => "contraportada",
        ArtKind.Spine => "lomo",
        _ => "disco",
    };

    public override bool RequiresKey => false;
    public override string KeyHelp => "";
    public override string KeyUrl => Site;
    public override string? GetKey(AppSettings settings) => NoKey;
    public override void SetKey(AppSettings settings, string? key) { }

    public override async Task<List<ArtGame>> SearchAsync(string term, string key, CancellationToken cancellation)
    {
        // La web busca el texto tal cual: si no sale nada, se prueba sin «The» delante y sin lo que va tras los dos puntos.
        foreach (var attempt in Attempts(term))
        {
            var html = await PostAsync("search.php", new Dictionary<string, string>
            {
                ["gamename"] = attempt,
                ["submit"] = "Search",
                ["submitted"] = "TRUE",
            }, cancellation);
            var games = SearchResult().Matches(html)
                .Select(match => new ArtGame(match.Groups[1].Value, WebUtility.HtmlDecode(match.Groups[2].Value).Trim(), null))
                .DistinctBy(game => game.Id)
                .ToList();
            if (games.Count > 0)
                return games;
        }
        return [];
    }

    static IEnumerable<string> Attempts(string term)
    {
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var text = term.Trim();
        foreach (var candidate in new[]
                 {
                     text,
                     Regex.Replace(text, @"^the\s+", "", RegexOptions.IgnoreCase),
                     Regex.Replace(text, @"^the\s+", "", RegexOptions.IgnoreCase).Split(':', '-', '–')[0].Trim(),
                 })
        {
            if (candidate.Length > 1 && tried.Add(candidate))
                yield return candidate;
        }
    }

    public override async Task<List<ArtImage>> GetImagesAsync(ArtGame game, string key, CancellationToken cancellation)
    {
        var html = await GetAsync($"covers/{game.Id}", null, cancellation);
        var covers = new List<Cover>();

        // Cada sección («… Disc Case Covers», «… Single Disc Labels») es una tabla con una celda por carátula.
        var sections = Heading().Split(html);
        for (var i = 1; i + 1 < sections.Length; i += 2)
        {
            var isDisc = sections[i].Contains("Disc Label", StringComparison.OrdinalIgnoreCase);
            var isCase = sections[i].Contains("Case Cover", StringComparison.OrdinalIgnoreCase);
            if (!isDisc && !isCase)
                continue;

            foreach (var cell in sections[i + 1].Split("<td"))
            {
                if (Download().Match(cell) is not { Success: true } download)
                    continue;
                covers.Add(new Cover(game.Id, download.Groups[1].Value, int.Parse(download.Groups[2].Value),
                    Small(cell, "Case"), Small(cell, "Location"), isDisc,
                    Stars().Match(cell) is { Success: true } stars ? int.Parse(stars.Groups[1].Value) : 0));
            }
        }

        return covers
            .DistinctBy(cover => cover.Id)
            .Where(Fits)
            .OrderByDescending(cover => cover.Stars)
            .Select(cover =>
            {
                var extension = cover.Extension == 2 ? "png" : "jpg";
                var label = cover.IsDisc ? "Disco" : $"{cover.CaseType ?? "Caja"} · {LocationName(cover)}";
                return new ArtImage($"{Site}covers.php?cover={cover.Id}&ext={cover.Extension}",
                    $"{Images}large/{cover.Id}.{extension}", 0, 0, label, Tag: cover);
            })
            .ToList();
    }

    /// <summary>¿Sirve esta carátula para lo que se busca? Las completas valen para portada, contraportada y lomo.</summary>
    bool Fits(Cover cover)
    {
        if (cover.IsDisc)
            return kind == ArtKind.Disc;
        var back = cover.Location?.Contains("Back", StringComparison.OrdinalIgnoreCase) == true;
        var jewel = cover.CaseType?.Contains("Jewel", StringComparison.OrdinalIgnoreCase) == true;
        var wrap = !back && !jewel;
        return kind switch
        {
            ArtKind.Front => !back,
            ArtKind.Back => back || wrap,
            ArtKind.Spine => wrap || back && jewel,
            _ => false,
        };
    }

    static string LocationName(Cover cover) =>
        cover.Location?.Contains("Back", StringComparison.OrdinalIgnoreCase) == true ? "Trasera"
        : cover.CaseType?.Contains("Jewel", StringComparison.OrdinalIgnoreCase) == true ? "Portada"
        : "Carátula completa";

    static string? Small(string cell, string field) =>
        Regex.Match(cell, $@"<small>{field}:\s*([^<]+)</small>") is { Success: true } match ? WebUtility.HtmlDecode(match.Groups[1].Value).Trim() : null;

    public override async Task<string> DownloadImageAsync(ArtImage image, CancellationToken cancellation)
    {
        if (image.Tag is not Cover cover)
            return await base.DownloadImageAsync(image, cancellation);

        // La descarga solo funciona pedida desde la página de la carátula, como al pulsar «Download» en la web.
        using var request = new HttpRequestMessage(HttpMethod.Get, image.Url);
        request.Headers.Referrer = new Uri($"{Site}covers/{cover.Slug}/{cover.Id}");
        byte[] bytes;
        try
        {
            using var response = await Http.SendAsync(request, cancellation);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType?.StartsWith("image/") != true)
                throw new ArtProviderException("Steam Game Covers no ha dejado descargar la carátula. Prueba otra o más tarde.");
            bytes = await response.Content.ReadAsByteArrayAsync(cancellation);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
        {
            throw new ArtProviderException("No se ha podido descargar la carátula. Comprueba tu conexión a internet.");
        }

        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Basilisco");
        System.IO.Directory.CreateDirectory(folder);
        var path = System.IO.Path.Combine(folder, $"{Guid.NewGuid():N}.{(cover.Extension == 2 ? "png" : "jpg")}");
        await System.IO.File.WriteAllBytesAsync(path, bytes, cancellation);

        // Es una plantilla para imprimir: se recorta y, si es completa, se separa en portada, lomo y contraportada.
        var back = cover.Location?.Contains("Back", StringComparison.OrdinalIgnoreCase) == true;
        var parts = await Task.Run(() => PrintTemplate.Split(path, cover.CaseType, back, cover.IsDisc), cancellation);
        try { System.IO.File.Delete(path); }
        catch (System.IO.IOException) { }

        LastParts = parts;
        return parts.Get(kind) ?? throw new ArtProviderException($"Esta carátula no trae {ImageKind}. Prueba con otra.");
    }

    async Task<string> GetAsync(string path, string? referrer, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Site + path);
        request.Headers.Accept.ParseAdd("text/html");
        if (referrer is not null)
            request.Headers.Referrer = new Uri(referrer);
        return await SendAsync(request, cancellation);
    }

    async Task<string> PostAsync(string path, Dictionary<string, string> form, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Site + path) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Accept.ParseAdd("text/html");
        return await SendAsync(request, cancellation);
    }

    static async Task<string> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        try
        {
            using var response = await Http.SendAsync(request, cancellation);
            if (!response.IsSuccessStatusCode)
                throw new ArtProviderException($"Steam Game Covers ha respondido con un error ({(int)response.StatusCode}). Prueba más tarde.");
            // La web está en latin-1.
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellation);
            return System.Text.Encoding.Latin1.GetString(bytes);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
        {
            throw new ArtProviderException("No se ha podido conectar con Steam Game Covers. Comprueba tu conexión a internet.");
        }
    }

    [GeneratedRegex(@"<a href=""covers/([a-z0-9-]+)"">([^<]+)</a>")]
    private static partial Regex SearchResult();

    [GeneratedRegex(@"<h2 class=""heading"">([^<]*)</h2>")]
    private static partial Regex Heading();

    [GeneratedRegex(@"covers\.php\?cover=(\d+)&(?:amp;)?ext=(\d)")]
    private static partial Regex Download();

    [GeneratedRegex(@"/images/(\d)star\.png")]
    private static partial Regex Stars();
}
