using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Basilisco.Services;

/// <summary>
/// Las canciones de la banda sonora que no tienes en archivo, desde el reproductor oficial de YouTube: visible (como piden
/// las condiciones de YouTube), en streaming y sin descargar nada. El vídeo de cada canción se elige a mano en YouTube o,
/// con tu clave de la API de YouTube, se busca solo.
/// </summary>
public static partial class YouTubeMusic
{
    /// <summary>Dirección propia de la página del reproductor (YouTube no reproduce en páginas sin dirección).</summary>
    public const string Host = "basilisco.player";
    public const string KeyUrl = "https://console.cloud.google.com/apis/library/youtube.googleapis.com";

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Basilisco", AppInfo.Version));
        return client;
    }

    /// <summary>La carpeta con la página del reproductor (se escribe cada vez, por si cambia con una versión nueva).</summary>
    public static string PlayerFolder()
    {
        var folder = Path.Combine(WebViews.ProfileFolder, "player");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "player.html"), PlayerHtml);
        return folder;
    }

    public static string PlayerUrl(string videoId, double volume) =>
        $"https://{Host}/player.html?v={Uri.EscapeDataString(videoId)}&vol={(int)Math.Round(volume * 100)}";

    /// <summary>El identificador de un vídeo en una dirección de YouTube (watch?v=…, youtu.be/…), o null.</summary>
    public static string? VideoIdOf(string? url)
    {
        if (url is null)
            return null;
        var match = VideoId().Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"(?:[?&]v=|youtu\.be/|/embed/|/shorts/)([A-Za-z0-9_-]{11})")]
    private static partial Regex VideoId();

    /// <summary>Con la clave de la API de YouTube: el primer vídeo que se puede poner fuera de YouTube para esa búsqueda.</summary>
    public static async Task<string?> SearchAsync(string query, string apiKey, CancellationToken cancellation)
    {
        try
        {
            var url = "https://www.googleapis.com/youtube/v3/search?part=id&type=video&videoEmbeddable=true&maxResults=1" +
                      $"&q={Uri.EscapeDataString(query)}&key={Uri.EscapeDataString(apiKey)}";
            using var response = await Http.GetAsync(url, cancellation);
            if (!response.IsSuccessStatusCode)
                return null;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            return json.RootElement.TryGetProperty("items", out var items) && items.GetArrayLength() > 0 &&
                   items[0].TryGetProperty("id", out var id) && id.TryGetProperty("videoId", out var video)
                ? video.GetString()
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// El reproductor oficial (IFrame API) a toda la página. Avisa a Basilisco de cuándo suena, se para o acaba, y se maneja
    /// desde fuera con window.bas (reproducir, parar, avanzar, volumen y por dónde va).
    /// </summary>
    const string PlayerHtml = """
        <!doctype html>
        <html><head><meta charset="utf-8"><style>html,body{margin:0;height:100%;background:#000;overflow:hidden}#p{width:100%;height:100%}</style></head>
        <body><div id="p"></div>
        <script src="https://www.youtube.com/iframe_api"></script>
        <script>
          const query = new URLSearchParams(location.search);
          let player = null;
          const send = message => window.chrome && window.chrome.webview && window.chrome.webview.postMessage(JSON.stringify(message));
          function onYouTubeIframeAPIReady() {
            player = new YT.Player('p', {
              width: '100%', height: '100%', videoId: query.get('v'),
              playerVars: { autoplay: 1, playsinline: 1, rel: 0, modestbranding: 1 },
              events: {
                onReady: e => { e.target.setVolume(Number(query.get('vol') || 70)); e.target.playVideo(); },
                onStateChange: e => send({ state: e.data }),
                onError: e => send({ error: e.data })
              }
            });
          }
          window.bas = {
            play: () => player && player.playVideo(),
            pause: () => player && player.pauseVideo(),
            seek: seconds => player && player.seekTo(Math.max(0, player.getCurrentTime() + seconds), true),
            seekTo: seconds => player && player.seekTo(seconds, true),
            volume: value => player && player.setVolume(value),
            info: () => JSON.stringify(player && player.getCurrentTime ? { t: player.getCurrentTime(), d: player.getDuration(), s: player.getPlayerState() } : { t: 0, d: 0, s: -1 })
          };
        </script></body></html>
        """;
}
