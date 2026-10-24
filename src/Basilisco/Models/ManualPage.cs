namespace Basilisco.Models;

/// <summary>Una página de un manual hecho con imágenes: el archivo (por su contenido) y de dónde salió, para volver a bajarla.</summary>
public sealed class ManualPage
{
    public string Hash { get; set; } = "";
    public string? Source { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>Ocupa las dos páginas del libro abierto (para las imágenes horizontales), en el modo libreta.</summary>
    public bool Spread { get; set; }

    /// <summary>Giro en grados (0, 90, 180 o 270), en el modo de una sola página.</summary>
    public int Rotation { get; set; }
}
