namespace Basilisco.Models;

/// <summary>
/// La banda sonora de un juego: la lista de canciones (de MusicBrainz o de los archivos importados), el disco que se ve
/// en la caja abierta y la música que hayas importado tú para cada canción.
/// </summary>
public sealed class Soundtrack
{
    /// <summary>El disco en MusicBrainz (para volver a leerlo), o null si la lista salió de los archivos importados.</summary>
    public string? ReleaseId { get; set; }

    public string Title { get; set; } = "";
    public string? Artist { get; set; }

    /// <summary>Imagen del disco de la banda sonora (dentro de basilisco-data/covers), o null para usar la portada del disco.</summary>
    public string? Disc { get; set; }

    /// <summary>Encuadre de la imagen del disco (null: lo llena).</summary>
    public ImageFrame? DiscFrame { get; set; }

    public List<SoundtrackTrack> Tracks { get; set; } = [];
}

/// <summary>Una canción de la banda sonora.</summary>
/// <param name="File">El archivo de música importado (dentro de basilisco-data/music/&lt;juego&gt;), o null si aún no tiene.</param>
public sealed class SoundtrackTrack
{
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public int? LengthMs { get; set; }
    public string? File { get; set; }

    /// <summary>El vídeo de YouTube con esta canción (para escucharla sin tener el archivo), o null.</summary>
    public string? YouTubeId { get; set; }
}
