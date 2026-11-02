using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Basilisco.Models;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Basilisco.Services;

/// <summary>Qué es una cosa de la página de capturas y vídeos: cómo se enseña y cómo se pone.</summary>
public enum MediaKind
{
    /// <summary>Una imagen de internet (se descarga al verla).</summary>
    WebImage,
    /// <summary>Una imagen del disco (tus capturas).</summary>
    LocalImage,
    /// <summary>Un tráiler de Steam en streaming o .mp4.</summary>
    Stream,
    /// <summary>Un vídeo de YouTube (el tráiler que enlaza LaunchBox).</summary>
    YouTube,
    /// <summary>Una grabación de Steam (DASH, .mpd).</summary>
    SteamClip,
    /// <summary>Un vídeo tuyo del disco.</summary>
    LocalVideo,
}

/// <param name="Source">La dirección o la ruta (en las grabaciones de Steam, la carpeta con su session.mpd).</param>
/// <param name="Poster">Su imagen (para los vídeos, la miniatura): dirección o ruta, o null.</param>
/// <param name="Codec">El del vídeo de una grabación de Steam (para ver antes si este equipo lo sabe poner).</param>
public sealed record MediaItem(string Title, string Source, string? Poster, MediaKind Kind, string? Detail = null, string? Codec = null)
{
    public bool IsVideo => Kind is MediaKind.Stream or MediaKind.YouTube or MediaKind.SteamClip or MediaKind.LocalVideo;
}

/// <param name="Folder">Si es una carpeta tuya, cuál (para poder quitarla).</param>
public sealed record MediaGroup(string Name, List<MediaItem> Items, string? Folder = null);

/// <summary>
/// Las capturas y los vídeos de un juego, repartidos por su origen (cada grupo es una opción del desplegable): los de
/// Steam y LaunchBox (los encontrados, guardados en el juego), los tuyos de Steam (las capturas que haces con F12 y las
/// grabaciones de Steam) y los de las carpetas que hayas añadido a ese juego.
/// </summary>
public static partial class GameMediaLibrary
{
    static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif"];
    static readonly string[] VideoExtensions = [".mp4", ".webm", ".m4v", ".mov", ".mkv"];

    static readonly string ThumbFolder = Path.Combine(AppInfo.DataDirectory, "media", "thumbs");

    public static List<MediaGroup> ScreenshotGroups(Game game)
    {
        var groups = new List<MediaGroup>();
        if (game.Media is { } media)
        {
            Add(groups, "Steam", media.SteamScreenshots.Select((url, index) => new MediaItem($"Captura {index + 1}", url, url, MediaKind.WebImage)));
            Add(groups, "LaunchBox", media.LaunchBoxScreenshots.Select((url, index) => new MediaItem($"Captura {index + 1}", url, url, MediaKind.WebImage)));
        }
        if (game.SteamAppId is { } appId)
            Add(groups, "Mis capturas de Steam", SteamScreenshots(appId));
        foreach (var folder in game.ScreenshotFolders ?? [])
            groups.Add(new MediaGroup(FolderName(folder), FilesIn(folder, ImageExtensions).Select(path => new MediaItem(
                Path.GetFileNameWithoutExtension(path), path, path, MediaKind.LocalImage, File.GetLastWriteTime(path).ToString("g", Spanish))).ToList(), folder));
        return groups;
    }

    public static List<MediaGroup> VideoGroups(Game game)
    {
        var groups = new List<MediaGroup>();
        if (game.Media is { } media)
        {
            Add(groups, "Tráilers de Steam", media.SteamTrailers.Select(video => new MediaItem(video.Name, video.Url, video.Poster, MediaKind.Stream, "Steam")));
            if (media.YouTubeTrailer is { } youTube)
                Add(groups, "LaunchBox (YouTube)", [new MediaItem("Tráiler", youTube, $"https://i.ytimg.com/vi/{youTube}/hqdefault.jpg", MediaKind.YouTube, "YouTube · de LaunchBox")]);
        }
        if (game.SteamAppId is { } appId)
            Add(groups, "Mis grabaciones de Steam", SteamClips(appId));
        foreach (var folder in game.VideoFolders ?? [])
            groups.Add(new MediaGroup(FolderName(folder), FilesIn(folder, VideoExtensions).Select(path => new MediaItem(
                Path.GetFileNameWithoutExtension(path), path, null, MediaKind.LocalVideo, File.GetLastWriteTime(path).ToString("g", Spanish))).ToList(), folder));
        return groups;
    }

    static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    static void Add(List<MediaGroup> groups, string name, IEnumerable<MediaItem> items)
    {
        var list = items.ToList();
        if (list.Count > 0)
            groups.Add(new MediaGroup(name, list));
    }

    static string FolderName(string folder) => $"Carpeta «{Path.GetFileName(folder.TrimEnd('\\', '/'))}»";

    /// <summary>Los archivos de esas extensiones de la carpeta (y sus subcarpetas), los más nuevos primero.</summary>
    static List<string> FilesIn(string folder, string[] extensions)
    {
        try
        {
            if (!Directory.Exists(folder))
                return [];
            return Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true })
                .Where(path => extensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
                .OrderByDescending(File.GetLastWriteTime)
                .Take(500)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Las carpetas de cada cuenta de Steam de este equipo (userdata/&lt;cuenta&gt;).</summary>
    static IEnumerable<string> SteamUsers()
    {
        if (SteamLocal.SteamPath() is not { } steam)
            return [];
        try
        {
            var userdata = Path.Combine(steam, "userdata");
            return Directory.Exists(userdata) ? Directory.GetDirectories(userdata) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Las capturas que has hecho en Steam (F12) de ese juego: userdata/&lt;cuenta&gt;/760/remote/&lt;juego&gt;/screenshots.</summary>
    static IEnumerable<MediaItem> SteamScreenshots(int appId) =>
        SteamUsers()
            .SelectMany(user => FilesIn(Path.Combine(user, "760", "remote", appId.ToString(CultureInfo.InvariantCulture), "screenshots"), ImageExtensions)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}thumbnails{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(File.GetLastWriteTime)
            .Select(path => new MediaItem(TakenAt(path) ?? Path.GetFileNameWithoutExtension(path), path, path, MediaKind.LocalImage, "Steam"));

    /// <summary>
    /// Tus grabaciones de Steam de ese juego (los clips que guardas en la grabación de partidas):
    /// userdata/&lt;cuenta&gt;/gamerecordings/clips/clip_&lt;juego&gt;_&lt;fecha&gt;, cada una con su session.mpd y su miniatura.
    /// </summary>
    static IEnumerable<MediaItem> SteamClips(int appId)
    {
        var prefix = $"clip_{appId}_";
        foreach (var user in SteamUsers())
        {
            string[] clips;
            try
            {
                var folder = Path.Combine(user, "gamerecordings", "clips");
                clips = Directory.Exists(folder) ? Directory.GetDirectories(folder, prefix + "*") : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var clip in clips.OrderByDescending(path => path, StringComparer.Ordinal))
            {
                var mpd = SafeFiles(Path.Combine(clip, "video"), "session.mpd").FirstOrDefault();
                if (mpd is null)
                    continue;
                string? codec = null, length = null;
                try
                {
                    var text = File.ReadAllText(mpd);
                    codec = Regex.Match(text, "contentType=\"video\"[\\s\\S]*?codecs=\"([^\"]+)\"").Groups[1].Value is { Length: > 0 } found ? found : null;
                    length = Regex.Match(text, "mediaPresentationDuration=\"PT(?:(\\d+)H)?(?:(\\d+)M)?([\\d.]+)S\"") is { Success: true } duration
                        ? Duration(duration)
                        : null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
                var thumbnail = Path.Combine(clip, "thumbnail.jpg");
                var date = ClipDate(Path.GetFileName(clip)[prefix.Length..]);
                yield return new MediaItem(date ?? Path.GetFileName(clip), Path.GetDirectoryName(mpd)!, File.Exists(thumbnail) ? thumbnail : null,
                    MediaKind.SteamClip, length is null ? "Grabación de Steam" : $"Grabación de Steam · {length}", codec);
            }
        }
    }

    static IEnumerable<string> SafeFiles(string folder, string name)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, name, SearchOption.AllDirectories).ToList() : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    static string Duration(Match match)
    {
        var hours = match.Groups[1].Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        var minutes = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        var seconds = (int)double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        var total = TimeSpan.FromSeconds(hours * 3600 + minutes * 60 + seconds);
        return total.TotalHours >= 1 ? total.ToString(@"h\:mm\:ss") : total.ToString(@"m\:ss");
    }

    /// <summary>«20260810_203729» → «10/08/2026 20:37».</summary>
    static string? ClipDate(string text) =>
        DateTime.TryParseExact(text, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date.ToString("g", Spanish) : null;

    /// <summary>La fecha de una captura de Steam por su nombre («20260223001921_1.jpg»).</summary>
    static string? TakenAt(string path) =>
        DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path).Split('_')[0], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("g", Spanish)
            : null;

    /// <summary>
    /// La miniatura de un vídeo tuyo, la que enseña el Explorador de Windows (se guarda en basilisco-data/media/thumbs para
    /// no pedirla cada vez). Null si Windows no sabe sacarla.
    /// </summary>
    public static async Task<string?> VideoThumbnailAsync(string path)
    {
        try
        {
            var info = new FileInfo(path);
            var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}")))[..16].ToLowerInvariant();
            var thumb = Path.Combine(ThumbFolder, key + ".jpg");
            if (File.Exists(thumb))
                return thumb;
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
            using var image = await file.GetThumbnailAsync(ThumbnailMode.VideosView, 640, ThumbnailOptions.UseCurrentScale);
            if (image is null || image.Size == 0)
                return null;
            Directory.CreateDirectory(ThumbFolder);
            using (var output = File.Create(thumb))
                await image.AsStreamForRead().CopyToAsync(output);
            return thumb;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
