namespace Basilisco.Models;

/// <summary>
/// Capturas de pantalla y tráileres de un juego encontrados en Steam y LaunchBox, para su página en la caja abierta. Las
/// tuyas (las de Steam y las de tus carpetas) no se guardan aquí: se leen del disco al abrir la página.
/// </summary>
public sealed class GameMedia
{
    /// <summary>Formato: la 2 separa las capturas por su origen. Las de la 0.10.0 (1) se vuelven a buscar.</summary>
    public const int CurrentVersion = 2;

    public int Version { get; set; }

    /// <summary>Capturas de la ficha de Steam (direcciones a tamaño completo; se descargan a basilisco-data/media al verlas).</summary>
    public List<string> SteamScreenshots { get; set; } = [];

    /// <summary>Capturas de LaunchBox (de juego y de la pantalla de título).</summary>
    public List<string> LaunchBoxScreenshots { get; set; } = [];

    /// <summary>Los tráileres de la ficha de Steam, el destacado primero.</summary>
    public List<MediaVideo> SteamTrailers { get; set; } = [];

    /// <summary>Tráiler de YouTube (el identificador del vídeo) que enlaza LaunchBox.</summary>
    public string? YouTubeTrailer { get; set; }

    /// <summary>Cuándo se buscaron (se vuelven a buscar pasado un tiempo, o si no se encontró nada).</summary>
    public DateTime Looked { get; set; }

    public bool IsEmpty => SteamScreenshots.Count == 0 && LaunchBoxScreenshots.Count == 0 && SteamTrailers.Count == 0 && YouTubeTrailer is null;
}

/// <summary>Un tráiler de Steam: su nombre, su dirección (.mp4 o en streaming, .m3u8) y su miniatura.</summary>
public sealed record MediaVideo(string Name, string Url, string? Poster);
