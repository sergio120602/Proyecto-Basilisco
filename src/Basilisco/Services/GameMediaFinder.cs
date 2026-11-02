using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Basilisco.Models;

namespace Basilisco.Services;

/// <summary>
/// Las capturas de pantalla y el tráiler de un juego, de Steam (los suyos, o los de un juego de la tienda que se llame
/// igual si es de PC) y de LaunchBox (sus capturas de juego y de la pantalla de título, y el tráiler de YouTube que
/// enlaza). Las capturas se descargan a basilisco-data/media al verlas; los tráileres se ven en streaming.
/// </summary>
public static class GameMediaFinder
{
    /// <summary>Pasado este tiempo se vuelven a buscar (Steam añade capturas y tráileres nuevos).</summary>
    static readonly TimeSpan Stale = TimeSpan.FromDays(30);

    /// <summary>Si no se encontró nada, se vuelve a probar antes.</summary>
    static readonly TimeSpan StaleEmpty = TimeSpan.FromDays(3);

    const int MaxScreenshots = 24;

    static readonly string Folder = Path.Combine(AppInfo.DataDirectory, "media");

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Basilisco", AppInfo.Version));
        return client;
    }

    /// <summary>True si hay que buscarlos (nunca se buscaron, o hace mucho).</summary>
    public static bool NeedsLookup(Game game) =>
        game.Media is not { } media || media.Version < GameMedia.CurrentVersion || DateTime.Now - media.Looked > (media.IsEmpty ? StaleEmpty : Stale);

    /// <summary>Busca las capturas y el tráiler del juego. Nunca falla: si no hay red o no se encuentra, devuelve lo que haya.</summary>
    /// <param name="steamFound">Lo de Steam, en cuanto llega (LaunchBox puede tardar si hay que descargar su base de datos).</param>
    public static async Task<GameMedia> FindAsync(Game game, IProgress<string>? status, CancellationToken cancellation, Action<GameMedia>? steamFound = null)
    {
        var media = new GameMedia { Looked = DateTime.Now, Version = GameMedia.CurrentVersion };

        status?.Report("Buscando en Steam…");
        var appId = game.SteamAppId;
        // Los de PC que no vienen de Steam (Epic, GOG, un .exe…) pueden estar en la tienda de Steam con el mismo nombre.
        if (appId is null && game.Platform is null or "pc")
            appId = await FindSteamAppAsync(game.Name, cancellation);
        if (appId is { } id && await FillFromSteamAsync(media, id, cancellation))
        {
            steamFound?.Invoke(new GameMedia
            {
                SteamScreenshots = [.. media.SteamScreenshots], SteamTrailers = [.. media.SteamTrailers],
                Version = GameMedia.CurrentVersion, Looked = DateTime.MinValue,
            });
        }

        status?.Report("Buscando en LaunchBox…");
        try
        {
            await LaunchBoxDatabase.EnsureReadyAsync(status, cancellation, upgrade: true);
            if (await Task.Run(() => LaunchBoxDatabase.FindGame(game.Name, LaunchBoxDatabase.PlatformName(game.Platform)), cancellation) is { } found)
            {
                var shots = found.Images
                    .Where(image => image.Kind == ArtKind.Manual && image.Detail is "Captura" or "Pantalla de título")
                    .OrderBy(image => image.Detail == "Captura" ? 0 : 1)
                    .Select(image => LaunchBoxDatabase.ImagesUrl + image.FileName)
                    .ToList();
                media.LaunchBoxScreenshots.AddRange(shots.Take(MaxScreenshots));
                media.YouTubeTrailer = YouTubeMusic.VideoIdOf(found.VideoUrl);
            }
        }
        catch (Exception ex) when (ex is ArtProviderException or HttpRequestException or IOException or InvalidDataException
                                   || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
        {
            // Sin LaunchBox (sin red, o la descarga ha fallado): se queda lo de Steam.
        }

        return media;
    }

    /// <summary>El juego de la tienda de Steam que se llama exactamente igual (sin contar mayúsculas, tildes ni signos).</summary>
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
                if (LaunchBoxDatabase.Normalize(item.GetProperty("name").GetString() ?? "") == wanted)
                    return item.GetProperty("id").GetInt32();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException
                                   || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
        {
        }
        return null;
    }

    /// <summary>Las capturas y el tráiler (el destacado, o el primero) de la ficha de Steam. True si había algo.</summary>
    static async Task<bool> FillFromSteamAsync(GameMedia media, int appId, CancellationToken cancellation)
    {
        try
        {
            using var json = await GetJsonAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&filters=screenshots,movies&l=spanish", cancellation);
            if (!json.RootElement.TryGetProperty(appId.ToString(), out var entry) || !entry.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object)
                return false;

            if (data.TryGetProperty("screenshots", out var screenshots))
            {
                foreach (var shot in screenshots.EnumerateArray().Take(MaxScreenshots))
                {
                    if (shot.TryGetProperty("path_full", out var full) && full.GetString() is { Length: > 0 } url)
                        media.SteamScreenshots.Add(url);
                }
            }

            if (data.TryGetProperty("movies", out var movies))
            {
                // Los destacados primero. Los tráileres antiguos tienen .mp4; los nuevos, solo en streaming (HLS), que la
                // página del reproductor también pone.
                foreach (var movie in movies.EnumerateArray().OrderBy(item => item.TryGetProperty("highlight", out var h) && h.ValueKind == JsonValueKind.True ? 0 : 1))
                {
                    var url = movie.TryGetProperty("mp4", out var mp4) && mp4.ValueKind == JsonValueKind.Object && mp4.TryGetProperty("max", out var max)
                        ? max.GetString()
                        : movie.TryGetProperty("hls_h264", out var hls) ? hls.GetString() : null;
                    if (url is null)
                        continue;
                    media.SteamTrailers.Add(new MediaVideo(movie.TryGetProperty("name", out var title) ? title.GetString() ?? "Tráiler" : "Tráiler", url,
                        movie.TryGetProperty("thumbnail", out var thumb) ? thumb.GetString() : null));
                }
            }
            return media.SteamScreenshots.Count > 0 || media.SteamTrailers.Count > 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException
                                   || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
        {
            return false;
        }
    }

    static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellation)
    {
        using var response = await Http.GetAsync(url, cancellation);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
    }

    /// <summary>
    /// La captura en el disco (basilisco-data/media/&lt;juego&gt;/), descargándola la primera vez. Null si no se puede
    /// descargar o no es una imagen.
    /// </summary>
    public static async Task<string?> ImagePathAsync(Game game, string url, CancellationToken cancellation)
    {
        var name = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url)))[..16].ToLowerInvariant();
        var folder = Path.Combine(Folder, game.Id);
        var path = Path.Combine(folder, name + ".jpg");
        if (File.Exists(path))
            return path;
        try
        {
            var bytes = await Http.GetByteArrayAsync(url, cancellation);
            // JPEG, PNG o WebP: si no, es una página de error.
            if (bytes.Length < 64 || !(bytes[0] == 0xFF && bytes[1] == 0xD8 || bytes[0] == 0x89 && bytes[1] == 0x50 || bytes[8] == (byte)'W' && bytes[9] == (byte)'E'))
                return null;
            Directory.CreateDirectory(folder);
            await File.WriteAllBytesAsync(path + ".tmp", bytes, cancellation);
            File.Move(path + ".tmp", path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException
                                   || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Borra las capturas descargadas de un juego (al quitarlo de la biblioteca).</summary>
    public static void Remove(Game game)
    {
        try
        {
            var folder = Path.Combine(Folder, game.Id);
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>La carpeta de las páginas del reproductor (la de YouTube y la de los tráileres de Steam).</summary>
    public static string PlayerFolder()
    {
        var folder = YouTubeMusic.PlayerFolder();
        File.WriteAllText(Path.Combine(folder, "trailer.html"), TrailerHtml);
        return folder;
    }

    /// <summary>
    /// La página que pone un vídeo a todo el reproductor: un tráiler de Steam (.mp4 o en streaming HLS), una grabación de
    /// Steam (DASH, .mpd) o un vídeo tuyo. Con <paramref name="codec"/> (el del vídeo de una grabación), antes mira si se puede ver.
    /// </summary>
    public static string TrailerUrl(string video, double volume, string? codec = null) =>
        $"https://{YouTubeMusic.Host}/trailer.html?src={Uri.EscapeDataString(video)}&vol={(int)Math.Round(volume * 100)}" +
        (codec is null ? "" : $"&codec={Uri.EscapeDataString(codec)}");

    /// <summary>
    /// El vídeo con sus controles. Los .m3u8 (los tráileres nuevos de Steam) se ponen con hls.js si el navegador no los
    /// sabe poner solo, y los .mpd (las grabaciones de Steam) con dash.js. Si el vídeo es de un formato que este equipo no
    /// sabe poner (las grabaciones en HEVC sin sus extensiones de Windows) avisa con { error: 'codec' }. Avisa a Basilisco
    /// de cuándo suena, se para o acaba; window.bas lo maneja desde fuera.
    /// </summary>
    const string TrailerHtml = """
        <!doctype html>
        <html><head><meta charset="utf-8"><style>html,body{margin:0;height:100%;background:#000;overflow:hidden}video{width:100%;height:100%;background:#000}</style></head>
        <body><video id="v" controls autoplay playsinline></video>
        <script src="https://cdnjs.cloudflare.com/ajax/libs/hls.js/1.5.20/hls.min.js"></script>
        <script src="https://cdnjs.cloudflare.com/ajax/libs/dashjs/4.7.4/dash.all.min.js"></script>
        <script>
          const query = new URLSearchParams(location.search);
          const src = query.get('src');
          const video = document.getElementById('v');
          const send = message => window.chrome && window.chrome.webview && window.chrome.webview.postMessage(JSON.stringify(message));
          video.volume = Math.min(1, Math.max(0, Number(query.get('vol') || 70) / 100));
          const codec = query.get('codec');
          if (codec && !(window.MediaSource && MediaSource.isTypeSupported('video/mp4; codecs="' + codec + '"'))) {
            send({ error: 'codec' });
          } else if (src.includes('.mpd') && window.dashjs) {
            const dash = dashjs.MediaPlayer().create();
            dash.on(dashjs.MediaPlayer.events.ERROR, () => send({ error: 'video' }));
            dash.initialize(video, src, true);
          } else if (src.includes('.m3u8') && !video.canPlayType('application/vnd.apple.mpegurl') && window.Hls && Hls.isSupported()) {
            const hls = new Hls({ capLevelToPlayerSize: true });
            hls.on(Hls.Events.ERROR, (_, data) => { if (data.fatal) send({ error: data.details }); });
            hls.loadSource(src);
            hls.attachMedia(video);
          } else {
            video.src = src;
          }
          video.addEventListener('playing', () => send({ state: 1 }));
          video.addEventListener('pause', () => send({ state: 2 }));
          video.addEventListener('ended', () => send({ state: 0 }));
          video.addEventListener('error', () => send({ error: 'video' }));
          video.play().catch(() => {});
          window.bas = { play: () => video.play(), pause: () => video.pause(), toggle: () => video.paused ? video.play() : video.pause() };
        </script></body></html>
        """;
}
