namespace Basilisco.Models;

/// <summary>Una página de un manual hecho con imágenes: el archivo (por su contenido) y de dónde salió, para volver a bajarla.</summary>
public sealed class ManualPage
{
    public string Hash { get; set; } = "";
    public string? Source { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}
