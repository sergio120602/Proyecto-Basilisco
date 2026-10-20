using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Basilisco.Services;

/// <summary>Un juego encontrado en un servicio de carátulas.</summary>
/// <param name="Detail">Información extra para distinguirlo (año, plataforma…).</param>
public sealed record ArtGame(string Id, string Name, string? Detail);

/// <summary>Una imagen disponible para un juego.</summary>
/// <param name="Label">Texto bajo la miniatura cuando no se saben las medidas (por ejemplo, la región).</param>
/// <param name="Tall">Imagen muy alta y estrecha (un lomo): la miniatura se enseña tumbada para que se lea.</param>
/// <param name="Tag">Datos propios del servicio para descargarla (por ejemplo, de qué página sale).</param>
/// <param name="Wide">Imagen muy apaisada (un fondo): la miniatura se enseña más ancha.</param>
/// <param name="Dark">Se enseña sobre fondo oscuro (los logotipos suelen ser blancos y con fondo transparente).</param>
public sealed record ArtImage(string Url, string ThumbUrl, int Width, int Height, string? Label = null, bool Tall = false, object? Tag = null,
                              bool Wide = false, bool Dark = false)
{
    public string SizeText => Width > 0 && Height > 0 ? $"{Width} × {Height}" : Label ?? "";
}

/// <summary>Error que se le puede enseñar al usuario tal cual.</summary>
public sealed class ArtProviderException(string message) : Exception(message);

/// <summary>Servicio de internet del que descargar imágenes de juegos.</summary>
public abstract class ArtProvider
{
    protected static readonly HttpClient Http = CreateClient();

    public abstract string Name { get; }

    /// <summary>Qué se descarga de aquí, para los textos de la interfaz («portada», «contraportada»).</summary>
    public abstract string ImageKind { get; }

    /// <summary>Cómo conseguir la clave de la API, en una frase.</summary>
    public abstract string KeyHelp { get; }

    /// <summary>Página donde se consigue la clave.</summary>
    public abstract string KeyUrl { get; }

    public abstract string? GetKey(AppSettings settings);
    public abstract void SetKey(AppSettings settings, string? key);

    /// <summary>False para servicios que no necesitan clave (LaunchBox).</summary>
    public virtual bool RequiresKey => true;

    /// <summary>Preparación antes de buscar (LaunchBox descarga aquí su base de datos la primera vez).</summary>
    public virtual Task PrepareAsync(IProgress<string> status, CancellationToken cancellation) => Task.CompletedTask;

    /// <summary>Botón extra del buscador (por ejemplo, «Actualizar base de datos»), o null si no tiene.</summary>
    public virtual string? MaintenanceLabel => null;

    public virtual Task RunMaintenanceAsync(IProgress<string> status, CancellationToken cancellation) => Task.CompletedTask;

    public abstract Task<List<ArtGame>> SearchAsync(string term, string key, CancellationToken cancellation);
    public abstract Task<List<ArtImage>> GetImagesAsync(ArtGame game, string key, CancellationToken cancellation);

    /// <summary>Lo que se enseña mientras se descarga lo elegido.</summary>
    public virtual string DownloadingText => "Descargando la imagen…";

    /// <summary>Descarga la imagen elegida y devuelve su ruta. Cada servicio puede prepararla a su manera (recortarla…).</summary>
    public virtual Task<string> DownloadImageAsync(ArtImage image, CancellationToken cancellation) => DownloadAsync(image, cancellation);

    /// <summary>Descarga la imagen a una carpeta temporal y devuelve su ruta.</summary>
    public static async Task<string> DownloadAsync(ArtImage image, CancellationToken cancellation)
    {
        var extension = Path.GetExtension(new Uri(image.Url).AbsolutePath).ToLowerInvariant();
        if (!ImageStore.IsImage("x" + extension))
            extension = ".png";

        var folder = Path.Combine(Path.GetTempPath(), "Basilisco");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{Guid.NewGuid():N}{extension}");

        // Algunos servidores de imágenes (el de LaunchBox, por ejemplo) a veces se quedan parados en la primera
        // petición y responden al momento en la segunda: si el primer intento falla o tarda, se prueba otra vez.
        for (var attempt = 1; ; attempt++)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            limit.CancelAfter(attempt == 1 ? TimeSpan.FromSeconds(12) : TimeSpan.FromSeconds(30));
            try
            {
                var bytes = await Http.GetByteArrayAsync(image.Url, limit.Token);
                await File.WriteAllBytesAsync(path, bytes, cancellation);
                return path;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException or OperationCanceledException && !cancellation.IsCancellationRequested)
            {
                // TaskCanceledException sin cancelar nosotros: el servidor no ha respondido a tiempo.
                if (attempt >= 2)
                    throw new ArtProviderException("No se ha podido descargar la imagen. Comprueba tu conexión a internet.");
            }
        }
    }

    protected static async Task<JsonDocument> GetJsonAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, cancellation);
        }
        catch (HttpRequestException)
        {
            throw new ArtProviderException("No se ha podido conectar. Comprueba tu conexión a internet.");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new ArtProviderException("La clave de la API no es válida o se ha agotado su límite de consultas.");
            if (response.StatusCode == HttpStatusCode.NotFound)
                return JsonDocument.Parse("{}");
            if (!response.IsSuccessStatusCode)
                throw new ArtProviderException($"El servicio ha respondido con un error ({(int)response.StatusCode}). Prueba más tarde.");

            var json = await response.Content.ReadAsStringAsync(cancellation);
            try
            {
                return JsonDocument.Parse(json);
            }
            catch (JsonException)
            {
                throw new ArtProviderException("El servicio ha devuelto una respuesta que no se entiende. Prueba más tarde.");
            }
        }
    }

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Basilisco", AppInfo.Version));
        return client;
    }

    protected static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    protected static int GetInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : 0;
}

/// <summary>
/// SteamGridDB (steamgriddb.com): carátulas hechas por la comunidad. Se piden primero las del formato de la caja:
/// cuadradas (1024 × 1024 y 512 × 512) o verticales (600 × 900 y similares, las más parecidas a 600 × 800).
/// Con <paramref name="heroes"/>, en lugar de carátulas se buscan sus «heroes»: imágenes grandes y apaisadas
/// (1920 × 620 o 3840 × 1240) hechas para ser el fondo del juego.
/// </summary>
/// <remarks>Con <paramref name="inside"/>, para el interior de la caja: los «heroes» primero y después todas las carátulas.</remarks>
/// <remarks>Con <paramref name="logos"/>, los logotipos del juego (PNG con fondo transparente), para los filtros.</remarks>
public sealed class SteamGridDbProvider(Models.CoverFormat format = Models.CoverFormat.Square, bool heroes = false, bool inside = false,
                                        bool logos = false) : ArtProvider
{
    string Dimensions => format == Models.CoverFormat.Portrait
        ? "600x900,342x482,660x930"
        : "1024x1024,512x512";

    const string BaseUrl = "https://www.steamgriddb.com/api/v2";

    public override string Name => "SteamGridDB";
    public override string ImageKind => logos ? "logotipo" : inside ? "imagen para el interior" : heroes ? "fondo" : "portada";
    public override string KeyHelp => "Entra en steamgriddb.com con tu cuenta, ve a Preferencias → API y genera una clave (es gratis e inmediata).";
    public override string KeyUrl => "https://www.steamgriddb.com/profile/preferences/api";

    public override string? GetKey(AppSettings settings) => settings.SteamGridDbApiKey;
    public override void SetKey(AppSettings settings, string? key) => settings.SteamGridDbApiKey = key;

    public override async Task<List<ArtGame>> SearchAsync(string term, string key, CancellationToken cancellation)
    {
        using var json = await GetJsonAsync(Request($"/search/autocomplete/{Uri.EscapeDataString(term)}", key), cancellation);
        var games = new List<ArtGame>();
        if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return games;

        foreach (var game in data.EnumerateArray())
        {
            var year = game.TryGetProperty("release_date", out var date) && date.TryGetInt64(out var seconds) && seconds > 0
                ? DateTimeOffset.FromUnixTimeSeconds(seconds).Year.ToString()
                : null;
            games.Add(new ArtGame(GetInt(game, "id").ToString(), GetString(game, "name") ?? "", year));
        }
        return games;
    }

    public override async Task<List<ArtImage>> GetImagesAsync(ArtGame game, string key, CancellationToken cancellation)
    {
        if (heroes)
            return await GetGridsAsync(game, key, "", cancellation, "heroes");
        if (logos)
            return await GetGridsAsync(game, key, "", cancellation, "logos");
        if (inside)
        {
            var all = await GetGridsAsync(game, key, "", cancellation, "heroes");
            all.AddRange(await GetGridsAsync(game, key, "", cancellation));
            return all;
        }

        // Primero las del formato de la caja; si el juego no tiene, todas las demás (se enseñarán enteras, sin recortar).
        var images = await GetGridsAsync(game, key, $"&dimensions={Dimensions}", cancellation);
        if (images.Count == 0)
            images = await GetGridsAsync(game, key, "", cancellation);
        return images;
    }

    async Task<List<ArtImage>> GetGridsAsync(ArtGame game, string key, string filter, CancellationToken cancellation, string kind = "grids")
    {
        using var json = await GetJsonAsync(Request($"/{kind}/game/{game.Id}?types=static{filter}", key), cancellation);
        var images = new List<ArtImage>();
        if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return images;

        foreach (var grid in data.EnumerateArray())
        {
            if (GetString(grid, "url") is not { } url)
                continue;
            images.Add(new ArtImage(url, GetString(grid, "thumb") ?? url, GetInt(grid, "width"), GetInt(grid, "height"),
                Wide: heroes || kind == "heroes" || GetInt(grid, "width") > GetInt(grid, "height") * 1.3, Dark: kind == "logos"));
        }
        return images;
    }

    static HttpRequestMessage Request(string path, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }
}

/// <summary>
/// TheGamesDB (thegamesdb.net): base de datos de juegos con fotos de las cajas por delante y por detrás,
/// y banners horizontales con el título. De aquí se sacan las contraportadas y, de los banners, los lomos.
/// </summary>
/// <param name="banners">True para buscar banners (para el lomo) en lugar de contraportadas.</param>
public sealed class TheGamesDbProvider(bool banners = false) : ArtProvider
{
    const string BaseUrl = "https://api.thegamesdb.net";

    // La búsqueda ya trae las imágenes de las cajas: se guardan para no gastar otra consulta (hay un límite mensual).
    readonly Dictionary<string, List<ArtImage>> _backsByGame = [];

    public override string Name => "TheGamesDB";
    public override string ImageKind => banners ? "imagen para el lomo" : "contraportada";
    public override string KeyHelp => "Entra en thegamesdb.net con tu cuenta y pulsa «API Access Request» al pie de la página. Es gratis, pero tienen que aprobarla.";
    public override string KeyUrl => "https://thegamesdb.net/";

    public override string? GetKey(AppSettings settings) => settings.TheGamesDbApiKey;
    public override void SetKey(AppSettings settings, string? key) => settings.TheGamesDbApiKey = key;

    public override async Task<List<ArtGame>> SearchAsync(string term, string key, CancellationToken cancellation)
    {
        var url = $"{BaseUrl}/v1.1/Games/ByGameName?apikey={Uri.EscapeDataString(key)}&name={Uri.EscapeDataString(term)}" +
                  "&fields=platform&include=boxart,platform";
        using var json = await GetJsonAsync(new HttpRequestMessage(HttpMethod.Get, url), cancellation);
        var root = json.RootElement;
        var games = new List<ArtGame>();
        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("games", out var list) || list.ValueKind != JsonValueKind.Array)
            return games;

        var include = root.TryGetProperty("include", out var inc) ? inc : default;
        var platforms = ReadPlatforms(include);
        if (!banners && include.ValueKind == JsonValueKind.Object && include.TryGetProperty("boxart", out var boxart))
            CacheBacks(boxart, "data");

        foreach (var game in list.EnumerateArray())
        {
            var id = GetInt(game, "id").ToString();
            var platform = platforms.GetValueOrDefault(GetInt(game, "platform"));
            var year = GetString(game, "release_date") is { Length: >= 4 } date ? date[..4] : null;
            var detail = string.Join(" · ", new[] { platform, year }.Where(text => !string.IsNullOrEmpty(text)));
            games.Add(new ArtGame(id, GetString(game, "game_title") ?? "", detail.Length > 0 ? detail : null));
        }
        return games;
    }

    public override async Task<List<ArtImage>> GetImagesAsync(ArtGame game, string key, CancellationToken cancellation)
    {
        if (_backsByGame.TryGetValue(game.Id, out var cached))
            return cached;

        var type = banners ? "banner" : "boxart";
        var url = $"{BaseUrl}/v1/Games/Images?apikey={Uri.EscapeDataString(key)}&games_id={game.Id}&filter%5Btype%5D={type}";
        using var json = await GetJsonAsync(new HttpRequestMessage(HttpMethod.Get, url), cancellation);
        if (json.RootElement.TryGetProperty("data", out var data))
            CacheBacks(data, "images");
        return _backsByGame.GetValueOrDefault(game.Id) ?? [];
    }

    /// <summary>Lee las imágenes («base_url» + lista por juego) y se queda con las de atrás de la caja, o con los banners.</summary>
    void CacheBacks(JsonElement section, string listProperty)
    {
        if (!section.TryGetProperty("base_url", out var baseUrl) || !section.TryGetProperty(listProperty, out var byGame) ||
            byGame.ValueKind != JsonValueKind.Object)
            return;

        var original = GetString(baseUrl, "original") ?? "";
        var thumb = GetString(baseUrl, "thumb") ?? GetString(baseUrl, "small") ?? original;

        foreach (var entry in byGame.EnumerateObject())
        {
            var backs = new List<ArtImage>();
            if (entry.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var image in entry.Value.EnumerateArray())
                {
                    var wanted = banners ? GetString(image, "type") == "banner" : GetString(image, "side") == "back";
                    if (!wanted || GetString(image, "filename") is not { } file)
                        continue;
                    var (width, height) = ParseResolution(GetString(image, "resolution"));
                    backs.Add(new ArtImage(original + file, thumb + file, width, height));
                }
            }
            _backsByGame[entry.Name] = backs;
        }
    }

    static Dictionary<int, string> ReadPlatforms(JsonElement include)
    {
        var platforms = new Dictionary<int, string>();
        if (include.ValueKind != JsonValueKind.Object || !include.TryGetProperty("platform", out var platform))
            return platforms;

        var data = platform.TryGetProperty("data", out var inner) ? inner : platform;
        if (data.ValueKind != JsonValueKind.Object)
            return platforms;

        foreach (var entry in data.EnumerateObject())
        {
            if (int.TryParse(entry.Name, out var id) && entry.Value.ValueKind == JsonValueKind.Object && GetString(entry.Value, "name") is { } name)
                platforms[id] = name;
        }
        return platforms;
    }

    static (int Width, int Height) ParseResolution(string? resolution)
    {
        var parts = resolution?.Split('x');
        return parts is [var w, var h] && int.TryParse(w, out var width) && int.TryParse(h, out var height)
            ? (width, height)
            : (0, 0);
    }
}
