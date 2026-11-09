namespace Basilisco.Models;

/// <summary>
/// Un juego desinstalado con Basilisco: su carpeta (o sus archivos) comprimida en un .zip en la carpeta de juegos
/// desinstalados (Ajustes). Al instalarlo, se descomprime en el mismo sitio de donde salió y se borra el .zip.
/// </summary>
public sealed class GameArchive
{
    /// <summary>El .zip con el juego.</summary>
    public string File { get; set; } = "";

    /// <summary>Lo que se comprimió: la carpeta del juego o, si sus archivos estaban sueltos entre otros, su archivo principal.</summary>
    public string Source { get; set; } = "";

    /// <summary>True si se comprimió la carpeta entera; false si fueron sus archivos (se vuelven a la carpeta de <see cref="Source"/>).</summary>
    public bool IsFolder { get; set; }

    /// <summary>Lo que ocupaba instalado, en bytes.</summary>
    public long Size { get; set; }

    /// <summary>Lo que ocupa el .zip, en bytes.</summary>
    public long CompressedSize { get; set; }

    public DateTime Date { get; set; }
}
