using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Basilisco.Models;

namespace Basilisco.Services;

/// <summary>Un disco de banda sonora encontrado en MusicBrainz, para elegirlo.</summary>
public sealed record SoundtrackRelease(string Id, string Title, string? Date, int Tracks, string? Artist)
{
    public string Detail => string.Join(" · ", new[] { Date?.Length >= 4 ? Date[..4] : Date, Tracks > 0 ? $"{Tracks} canciones" : null, Artist }
        .Where(text => !string.IsNullOrEmpty(text)));
}

/// <summary>Lo que ha pasado al importar música.</summary>
public sealed record MusicImport(int Registered, List<string> NotMatched, bool CreatedList);

/// <summary>
/// Bandas sonoras: la lista de canciones y la carátula salen de MusicBrainz (base de datos de música abierta, con API
/// oficial y gratuita); la música la importas tú, y cada archivo se registra en la canción que se llama igual. Los
/// archivos se copian a basilisco-data/music/&lt;juego&gt;, para que Basilisco siga siendo portable.
/// </summary>
public static partial class Soundtracks
{
    const string Api = "https://musicbrainz.org/ws/2";
    static readonly HttpClient Http = CreateClient();
    static readonly SemaphoreSlim Gate = new(1, 1);
    static DateTime _lastRequest;

    public static string MusicFolder { get; } = Path.Combine(AppInfo.DataDirectory, "music");

    public static readonly string[] AudioExtensions = [".mp3", ".flac", ".m4a", ".aac", ".wav", ".wma", ".ogg", ".opus"];

    public static string FileFilter => "Música|" + string.Join(";", AudioExtensions.Select(e => "*" + e)) + "|Todos los archivos|*.*";

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        // MusicBrainz pide un nombre de programa con una forma de contacto: la página del proyecto.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Basilisco/{AppInfo.Version} ( https://github.com/sergio120602/Proyecto-Basilisco )");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    /// <summary>MusicBrainz admite una consulta por segundo: se espera lo justo entre una y otra.</summary>
    static async Task<JsonDocument?> GetAsync(string url, CancellationToken cancellation)
    {
        await Gate.WaitAsync(cancellation);
        try
        {
            var wait = _lastRequest.AddMilliseconds(1100) - DateTime.Now;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, cancellation);
            _lastRequest = DateTime.Now;
            using var response = await Http.GetAsync(url, cancellation);
            if (!response.IsSuccessStatusCode)
                return null;
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    static string Escape(string text) => Regex.Replace(text, @"([+\-&|!(){}\[\]^""~*?:\\/])", @"\$1");

    /// <summary>Bandas sonoras de MusicBrainz para el juego, las más parecidas primero.</summary>
    public static async Task<List<SoundtrackRelease>> SearchAsync(string gameName, CancellationToken cancellation)
    {
        var name = Escape(gameName.Trim());
        var found = new List<SoundtrackRelease>();
        foreach (var query in new[] { $"release:\"{name}\" AND secondarytype:soundtrack", $"\"{name}\" AND secondarytype:soundtrack", $"release:\"{name} soundtrack\"" })
        {
            using var json = await GetAsync($"{Api}/release/?query={Uri.EscapeDataString(query)}&fmt=json&limit=15", cancellation);
            if (json?.RootElement.TryGetProperty("releases", out var releases) != true)
                continue;
            foreach (var release in releases.EnumerateArray())
            {
                if (release.GetProperty("id").GetString() is not { } id || found.Any(f => f.Id == id))
                    continue;
                var artist = release.TryGetProperty("artist-credit", out var credit) && credit.GetArrayLength() > 0 && credit[0].TryGetProperty("name", out var a)
                    ? a.GetString() : null;
                found.Add(new SoundtrackRelease(id, release.GetProperty("title").GetString() ?? "",
                    release.TryGetProperty("date", out var date) ? date.GetString() : null,
                    release.TryGetProperty("track-count", out var count) && count.TryGetInt32(out var n) ? n : 0, artist));
            }
            if (found.Count > 0)
                break;
        }
        // Primero los que se llaman como el juego; entre ellos, los que traen más canciones (las ediciones completas).
        var normalized = GameSearch.Normalize(gameName);
        return found
            .OrderByDescending(r => GameSearch.Normalize(r.Title) == normalized)
            .ThenByDescending(r => GameSearch.Normalize(r.Title).Contains(normalized))
            .ThenByDescending(r => r.Tracks)
            .ToList();
    }

    /// <summary>Lee la lista de canciones del disco elegido y descarga su carátula (para el disco). Null si no se puede.</summary>
    public static async Task<(Soundtrack Soundtrack, string? CoverPath)?> LoadAsync(SoundtrackRelease release, CancellationToken cancellation)
    {
        using var json = await GetAsync($"{Api}/release/{release.Id}?inc=recordings+artist-credits&fmt=json", cancellation);
        if (json is null || !json.RootElement.TryGetProperty("media", out var media))
            return null;
        var soundtrack = new Soundtrack { ReleaseId = release.Id, Title = release.Title, Artist = release.Artist };
        var number = 0;
        foreach (var medium in media.EnumerateArray())
        {
            if (!medium.TryGetProperty("tracks", out var tracks))
                continue;
            foreach (var track in tracks.EnumerateArray())
            {
                soundtrack.Tracks.Add(new SoundtrackTrack
                {
                    Number = ++number,
                    Title = track.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
                    LengthMs = track.TryGetProperty("length", out var length) && length.TryGetInt32(out var ms) ? ms : null,
                });
            }
        }
        if (soundtrack.Tracks.Count == 0)
            return null;
        return (soundtrack, await CoverAsync(release.Id, cancellation));
    }

    /// <summary>La carátula del disco en el Cover Art Archive (el de MusicBrainz), a 500 px. Null si no tiene.</summary>
    static async Task<string?> CoverAsync(string releaseId, CancellationToken cancellation)
    {
        try
        {
            using var response = await Http.GetAsync($"https://coverartarchive.org/release/{releaseId}/front-500", cancellation);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType?.StartsWith("image/") != true)
                return null;
            var folder = Path.Combine(Path.GetTempPath(), "Basilisco");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{Guid.NewGuid():N}.jpg");
            await File.WriteAllBytesAsync(path, await response.Content.ReadAsByteArrayAsync(cancellation), cancellation);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Busca la banda sonora del juego y se queda con la más parecida (para la descarga automática).</summary>
    public static async Task<(Soundtrack Soundtrack, string? CoverPath)?> FindAsync(string gameName, CancellationToken cancellation)
    {
        var releases = await SearchAsync(gameName, cancellation);
        foreach (var release in releases.Take(2))
        {
            if (await LoadAsync(release, cancellation) is not { } loaded)
                continue;
            // Si esa edición no tiene carátula, la de otra edición del mismo disco.
            if (loaded.CoverPath is null)
            {
                var title = GameSearch.Normalize(release.Title);
                foreach (var other in releases.Where(r => r.Id != release.Id && GameSearch.Normalize(r.Title) == title).Take(4))
                {
                    if (await CoverAsync(other.Id, cancellation) is { } cover)
                        return (loaded.Soundtrack, cover);
                }
            }
            return loaded;
        }
        return null;
    }

    public static string FolderOf(Game game) => Path.Combine(MusicFolder, game.Id);

    public static string? PathOf(Game game, SoundtrackTrack track) =>
        track.File is { } file && File.Exists(Path.Combine(FolderOf(game), file)) ? Path.Combine(FolderOf(game), file) : null;

    /// <summary>
    /// Importa archivos de música. Cada uno se registra en la canción de la lista que se llama igual (sin contar el número
    /// de pista, mayúsculas, tildes ni signos); los que no se parecen a ninguna no se copian. Si el juego aún no tiene
    /// lista de canciones, los archivos pasan a ser la lista, en orden.
    /// </summary>
    public static MusicImport Import(Game game, IReadOnlyList<string> files)
    {
        var folder = FolderOf(game);
        Directory.CreateDirectory(folder);
        var created = game.Soundtrack is not { Tracks.Count: > 0 };
        var soundtrack = game.Soundtrack ??= new Soundtrack { Title = game.Name };
        var notMatched = new List<string>();
        var registered = 0;

        foreach (var file in files.OrderBy(file => file, StringComparer.CurrentCultureIgnoreCase))
        {
            var name = CleanName(Path.GetFileNameWithoutExtension(file));
            SoundtrackTrack? track;
            if (created)
            {
                track = new SoundtrackTrack { Number = soundtrack.Tracks.Count + 1, Title = name };
                soundtrack.Tracks.Add(track);
            }
            else if ((track = Match(soundtrack, name)) is null)
            {
                notMatched.Add(Path.GetFileName(file));
                continue;
            }

            var target = $"{track.Number:00}-{Guid.NewGuid():N}"[..12] + Path.GetExtension(file).ToLowerInvariant();
            try
            {
                File.Copy(file, Path.Combine(folder, target), overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                notMatched.Add(Path.GetFileName(file));
                continue;
            }
            if (track.File is { } old && old != target)
                TryDelete(Path.Combine(folder, old));
            track.File = target;
            registered++;
        }
        return new MusicImport(registered, notMatched, created);
    }

    /// <summary>
    /// La canción que se llama como el archivo: igual, o el archivo lleva delante o detrás algo más (el nombre del juego,
    /// del compositor…). Entre varias, la de nombre más largo (la más concreta).
    /// </summary>
    static SoundtrackTrack? Match(Soundtrack soundtrack, string fileName)
    {
        var file = GameSearch.Normalize(fileName);
        if (file.Length == 0)
            return null;
        SoundtrackTrack? best = null;
        var bestLength = 0;
        foreach (var track in soundtrack.Tracks)
        {
            var title = GameSearch.Normalize(track.Title);
            if (title.Length == 0)
                continue;
            var matches = file == title ||
                          title.Length >= 4 && (file.EndsWith(" " + title, StringComparison.Ordinal) || file.StartsWith(title + " ", StringComparison.Ordinal));
            if (matches && title.Length > bestLength)
            {
                best = track;
                bestLength = title.Length;
            }
        }
        return best;
    }

    /// <summary>«01 - Overture», «1-01. Overture», «Track 01 Overture» → «Overture».</summary>
    static string CleanName(string name)
    {
        var clean = TrackNumber().Replace(name, "").Trim();
        return clean.Length > 0 ? clean : name;
    }

    [GeneratedRegex(@"^\s*(?:track\s*)?\d{1,3}(?:[-.]\d{1,3})?\s*[-._)\]]*\s*", RegexOptions.IgnoreCase)]
    private static partial Regex TrackNumber();

    /// <summary>Quita la banda sonora y su música.</summary>
    public static void Remove(Game game)
    {
        game.Soundtrack = null;
        try
        {
            if (Directory.Exists(FolderOf(game)))
                Directory.Delete(FolderOf(game), recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static string Length(int? ms) => ms is { } value ? $"{value / 60000}:{value / 1000 % 60:00}" : "";
}
