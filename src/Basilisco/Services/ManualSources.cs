using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Basilisco.Models;

namespace Basilisco.Services;

/// <summary>Un trozo de texto del manual.</summary>
public sealed record ManualBlock(ManualBlockKind Kind, string Text);

public enum ManualBlockKind { Heading, Paragraph, Bullet }

/// <summary>Todo lo que se sabe de un juego para escribir su manual.</summary>
public sealed class ManualContent
{
    public string Title { get; set; } = "";
    public string? Platform { get; set; }
    public string? ShortDescription { get; set; }
    public string? Developer { get; set; }
    public string? Publisher { get; set; }
    public string? Released { get; set; }
    public string? Genres { get; set; }
    public string? Players { get; set; }
    public string? Rating { get; set; }
    public string? Controller { get; set; }
    public string? Languages { get; set; }
    public string? Website { get; set; }
    public List<ManualBlock> About { get; } = [];
    public List<ManualBlock> Requirements { get; } = [];
    public List<string> Screenshots { get; } = [];
    public List<string> Sources { get; } = [];

    public bool HasText => About.Count > 0 || ShortDescription is not null;
}

/// <summary>
/// Saca la documentación de un juego de la tienda de Steam (en español, sin clave) y de la base de datos de LaunchBox
/// (en inglés): descripción, ficha, requisitos y capturas.
/// </summary>
public static partial class ManualSources
{
    static readonly HttpClient Http = CreateClient();

    public static async Task<ManualContent> GatherAsync(Game game, IProgress<string> status, CancellationToken cancellation)
    {
        var content = new ManualContent { Title = game.Name, Platform = Platforms.Find(game.Platform)?.Name };

        status.Report("Buscando el juego en Steam…");
        var appId = game.SteamAppId ?? await FindSteamAppAsync(game.Name, cancellation);
        if (appId is { } id)
            await FillFromSteamAsync(content, id, cancellation);

        // LaunchBox completa lo que falte. Si Steam no ha dado nada, se descarga su base de datos si hace falta.
        if (!content.HasText)
            await LaunchBoxDatabase.EnsureReadyAsync(status, cancellation, upgrade: true);
        else
            await LaunchBoxDatabase.TryLoadAsync(cancellation);
        status.Report("Buscando el juego en LaunchBox…");
        await Task.Run(() => FillFromLaunchBox(content, game), cancellation);

        if (!content.HasText)
            throw new ArtProviderException($"No se ha encontrado información de «{game.Name}» ni en Steam ni en LaunchBox. Prueba a escribir su nombre como en la tienda.");
        return content;
    }

    /// <summary>Busca en la tienda de Steam un juego que se llame igual (los que no se importaron desde Steam).</summary>
    static async Task<int?> FindSteamAppAsync(string name, CancellationToken cancellation)
    {
        try
        {
            using var json = await GetJsonAsync($"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(name)}&l=spanish&cc=ES", cancellation);
            if (!json.RootElement.TryGetProperty("items", out var items))
                return null;
            var wanted = LaunchBoxDatabase.Normalize(name);
            foreach (var item in items.EnumerateArray())
            {
                var found = LaunchBoxDatabase.Normalize(item.GetProperty("name").GetString() ?? "");
                if (found == wanted || found.StartsWith(wanted + " ", StringComparison.Ordinal) || wanted.StartsWith(found + " ", StringComparison.Ordinal))
                    return item.GetProperty("id").GetInt32();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
        }
        return null;
    }

    static async Task FillFromSteamAsync(ManualContent content, int appId, CancellationToken cancellation)
    {
        try
        {
            using var json = await GetJsonAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&l=spanish&cc=es", cancellation);
            if (!json.RootElement.TryGetProperty(appId.ToString(), out var entry) || !entry.TryGetProperty("data", out var data))
                return;

            content.Sources.Add("Steam");
            content.ShortDescription = Text(data, "short_description") is { } brief ? Decode(brief) : null;
            content.Developer = Join(data, "developers");
            content.Publisher = Join(data, "publishers");
            content.Released = data.TryGetProperty("release_date", out var release) ? Text(release, "date") : null;
            content.Genres = Join(data, "genres", "description");
            content.Website = Text(data, "website");
            content.Controller = Text(data, "controller_support") switch
            {
                "full" => "Compatible con mando",
                "partial" => "Compatible en parte con mando",
                _ => null,
            };
            if (data.TryGetProperty("categories", out var categories))
            {
                var modes = categories.EnumerateArray().Select(category => Text(category, "description") ?? "")
                    .Where(text => text.Contains("jugador", StringComparison.OrdinalIgnoreCase) || text.Contains("Cooperativo", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (modes.Count > 0)
                    content.Players = string.Join(", ", modes);
            }
            if (data.TryGetProperty("metacritic", out var metacritic) && metacritic.TryGetProperty("score", out var score))
                content.Rating = $"{score.GetInt32()} / 100 en Metacritic";
            if (Text(data, "supported_languages") is { } languages)
                content.Languages = Regex.Replace(Decode(Regex.Replace(languages, "<br>.*$", "", RegexOptions.Singleline)), "<[^>]+>", "").Replace("*", "").Trim();

            content.About.AddRange(HtmlBlocks(Text(data, "about_the_game") ?? Text(data, "detailed_description") ?? ""));

            // Requisitos: es un objeto con «minimum» y «recommended», o una lista vacía si no tiene.
            if (data.TryGetProperty("pc_requirements", out var requirements) && requirements.ValueKind == JsonValueKind.Object)
            {
                foreach (var level in new[] { "minimum", "recommended" })
                {
                    if (Text(requirements, level) is { } html)
                        content.Requirements.AddRange(HtmlBlocks(html));
                }
            }

            if (data.TryGetProperty("screenshots", out var screenshots))
            {
                foreach (var shot in screenshots.EnumerateArray().Take(6))
                {
                    if (Text(shot, "path_full") is { } url)
                        content.Screenshots.Add(url);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
        }
    }

    static void FillFromLaunchBox(ManualContent content, Game game)
    {
        if (LaunchBoxDatabase.FindGame(game.Name, LaunchBoxDatabase.PlatformName(game.Platform)) is not { } match)
            return;
        var details = LaunchBoxDatabase.Details(match.Id);
        var used = false;

        if (details is not null)
        {
            if (content.About.Count == 0 && details.Overview.Length > 0)
            {
                content.About.AddRange(details.Overview.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(paragraph => new ManualBlock(ManualBlockKind.Paragraph, paragraph)));
                used = true;
            }
            content.Developer ??= details.Developer;
            content.Publisher ??= details.Publisher;
            content.Genres ??= details.Genres?.Replace(";", ", ");
            if (content.Players is null && details.MaxPlayers is { } players)
            {
                content.Players = players == "1" ? "1 jugador" : $"Hasta {players} jugadores";
                if (details.Cooperative == "true")
                    content.Players += " (cooperativo)";
            }
            if (content.Rating is null && details.Esrb is { Length: > 0 } esrb && !esrb.StartsWith("Not Rated", StringComparison.OrdinalIgnoreCase))
                content.Rating = $"ESRB: {esrb}";
        }
        if (content.Released is null && match.ReleaseDate is { } released)
            content.Released = match.Released is { Length: 10 } ? released.ToString("d 'de' MMMM 'de' yyyy", new System.Globalization.CultureInfo("es-ES")) : released.Year.ToString();

        if (content.Screenshots.Count == 0)
        {
            foreach (var image in LaunchBoxDatabase.ImagesOf(match, ArtKind.Manual).Where(image => image.Detail == "Captura").Take(4))
                content.Screenshots.Add(LaunchBoxDatabase.ImagesUrl + image.FileName);
            used |= content.Screenshots.Count > 0;
        }
        if (used || details is not null)
            content.Sources.Add("LaunchBox");
    }

    /// <summary>Convierte el HTML de Steam en títulos, párrafos y viñetas (sin imágenes ni enlaces).</summary>
    public static List<ManualBlock> HtmlBlocks(string html)
    {
        var text = Regex.Replace(html, @"<img[^>]*>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<h\d[^>]*>(.*?)</h\d>", "\n\u0001$1\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"<li[^>]*>", "\n\u0002", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</li>|<br\s*/?>|</?p[^>]*>|</?ul[^>]*>|</?ol[^>]*>", "\n", RegexOptions.IgnoreCase);
        text = Decode(Regex.Replace(text, "<[^>]+>", ""));

        var blocks = new List<ManualBlock>();
        foreach (var raw in text.Split('\n'))
        {
            var line = Regex.Replace(raw, @"\s+", " ").Trim();
            if (line.Length == 0)
                continue;
            if (line[0] == '\u0001')
                Add(ManualBlockKind.Heading, line[1..]);
            else if (line[0] == '\u0002')
                Add(ManualBlockKind.Bullet, line[1..]);
            // «Mínimo:» y «Recomendado:» en los requisitos.
            else if (line.EndsWith(':') && line.Length < 30)
                Add(ManualBlockKind.Heading, line.TrimEnd(':'));
            else
                Add(ManualBlockKind.Paragraph, line);
        }
        return blocks;

        void Add(ManualBlockKind kind, string value)
        {
            value = value.Trim();
            if (value.Length > 0)
                blocks.Add(new ManualBlock(kind, value));
        }
    }

    public static async Task<byte[]?> DownloadAsync(string url, CancellationToken cancellation)
    {
        try
        {
            return await Http.GetByteArrayAsync(url, cancellation);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellation.IsCancellationRequested)
        {
            return null;
        }
    }

    static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellation) =>
        JsonDocument.Parse(await Http.GetStringAsync(url, cancellation));

    static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;

    static string? Join(JsonElement element, string property, string? field = null)
    {
        if (!element.TryGetProperty(property, out var list) || list.ValueKind != JsonValueKind.Array)
            return null;
        var values = list.EnumerateArray()
            .Select(item => field is null ? item.GetString() : Text(item, field))
            .OfType<string>().Select(Decode).ToList();
        return values.Count > 0 ? string.Join(", ", values) : null;
    }

    static string Decode(string text) => WebUtility.HtmlDecode(text).Trim();

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Basilisco", AppInfo.Version));
        return client;
    }
}
