using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Basilisco.Models;

namespace Basilisco.Services;

/// <summary>
/// Imágenes de las consolas en la base de datos de LaunchBox (gamesdb.launchbox-app.com): la foto de la consola
/// («Platform Device Thumb») de la página de imágenes de cada plataforma. Su archivo de datos no las trae, así que se
/// leen de esa página, que se identifica por el número de la plataforma en LaunchBox.
/// </summary>
public sealed partial class LaunchBoxPlatformProvider : ArtProvider
{
    const string NoKey = "-";

    /// <summary>Número de cada consola de Basilisco en LaunchBox (el de sus páginas de plataforma).</summary>
    static readonly Dictionary<string, int> Ids = new()
    {
        ["arcade"] = 5, ["xbox"] = 18, ["x360"] = 19, ["n64"] = 25, ["nds"] = 26, ["nes"] = 27, ["gb"] = 28, ["gba"] = 29,
        ["gc"] = 31, ["wii"] = 33, ["wiiu"] = 34, ["ps1"] = 47, ["ps2"] = 48, ["ps3"] = 49, ["psp"] = 52, ["snes"] = 53,
        ["pc"] = 84, ["switch"] = 211,
    };

    public override string Name => "LaunchBox";
    public override string ImageKind => "imagen de la consola";
    public override bool RequiresKey => false;
    public override string KeyHelp => "";
    public override string KeyUrl => "https://gamesdb.launchbox-app.com/";
    public override string? GetKey(AppSettings settings) => NoKey;
    public override void SetKey(AppSettings settings, string? key) { }

    /// <summary>Las consolas de Basilisco cuyo nombre contiene lo buscado (o todas); la buscada, la primera.</summary>
    public override Task<List<ArtGame>> SearchAsync(string term, string key, CancellationToken cancellation)
    {
        var text = term.Trim();
        var consoles = Platforms.All.Where(platform => Ids.ContainsKey(platform.Id)).ToList();
        var matches = consoles.Where(platform => platform.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)).ToList();
        return Task.FromResult((matches.Count > 0 ? matches.Concat(consoles.Except(matches)) : consoles)
            .Select(platform => new ArtGame(platform.Id, platform.Name, LaunchBoxDatabase.PlatformName(platform.Id)))
            .ToList());
    }

    public override async Task<List<ArtImage>> GetImagesAsync(ArtGame game, string key, CancellationToken cancellation)
    {
        if (!Ids.TryGetValue(game.Id, out var id))
            return [];
        string html;
        try
        {
            using var response = await Http.GetAsync($"https://gamesdb.launchbox-app.com/platforms/images/{id}", cancellation);
            if (!response.IsSuccessStatusCode)
                return [];
            html = await response.Content.ReadAsStringAsync(cancellation);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
        {
            throw new ArtProviderException("No se ha podido conectar con LaunchBox. Comprueba tu conexión a internet.");
        }

        return Image().Matches(html)
            .Select(match =>
            {
                var url = match.Groups["url"].Value;
                var title = WebUtility.HtmlDecode(match.Groups["title"].Value);
                var kind = title.Contains(" - ") ? title[(title.LastIndexOf(" - ", StringComparison.Ordinal) + 3)..] : title;
                var label = kind.Replace("Platform Device Thumb", "Foto de la consola").Replace("Platform ", "");
                return new ArtImage(url, url, 0, 0, $"{label} · {match.Groups["size"].Value}", Wide: true, Dark: true);
            })
            .DistinctBy(image => image.Url)
            .ToList();
    }

    [GeneratedRegex(@"href=""(?<url>https://images\.launchbox-app\.com/Platforms/[^""]+)""[^>]*data-title=""(?<title>[^""]+)""[^>]*data-footer=""(?<size>[^""]+)""")]
    private static partial Regex Image();
}
