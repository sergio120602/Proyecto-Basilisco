namespace Basilisco.Models;

/// <summary>Capturas de pantalla y tráiler de un juego (de Steam y de LaunchBox), para su página en la caja abierta.</summary>
public sealed class GameMedia
{
    /// <summary>Direcciones de las capturas, a tamaño completo (se descargan a basilisco-data/media al verlas).</summary>
    public List<string> Screenshots { get; set; } = [];

    /// <summary>Tráiler de Steam: su dirección en streaming (.m3u8) o en .mp4. Null si no tiene.</summary>
    public string? Trailer { get; set; }

    /// <summary>Miniatura del tráiler de Steam (lo que se ve antes de darle a reproducir).</summary>
    public string? TrailerPoster { get; set; }

    /// <summary>Nombre del tráiler («Launch Trailer»…).</summary>
    public string? TrailerName { get; set; }

    /// <summary>Tráiler de YouTube (el identificador del vídeo, de LaunchBox), si no hay el de Steam.</summary>
    public string? YouTubeTrailer { get; set; }

    /// <summary>De dónde salieron («Steam», «LaunchBox» o los dos).</summary>
    public string? Sources { get; set; }

    /// <summary>Cuándo se buscaron (se vuelven a buscar pasado un tiempo, o si no se encontró nada).</summary>
    public DateTime Looked { get; set; }

    public bool IsEmpty => Screenshots.Count == 0 && Trailer is null && YouTubeTrailer is null;
}
