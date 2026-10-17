namespace Basilisco.Models;

public sealed class Game
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";

    /// <summary>Ejecutable, acceso directo o cualquier archivo que se pueda abrir.</summary>
    public string Path { get; set; } = "";

    /// <summary>Nombre del archivo de la portada personalizada dentro de basilisco-data/covers, si tiene.</summary>
    public string? CoverImage { get; set; }

    /// <summary>Nombre del archivo de la contraportada personalizada dentro de basilisco-data/covers, si tiene.</summary>
    public string? BackImage { get; set; }

    /// <summary>Encuadre de la contraportada (zoom y posición). Null: entera.</summary>
    public ImageFrame? BackFrame { get; set; }

    /// <summary>Nombre del archivo de la imagen del lomo dentro de basilisco-data/covers, si tiene.</summary>
    public string? SpineImage { get; set; }

    /// <summary>Encuadre de la imagen del lomo. Null: entera.</summary>
    public ImageFrame? SpineFrame { get; set; }

    /// <summary>Consola del juego (ver <see cref="Platforms"/>); decide el borde de su caja. Null si no tiene.</summary>
    public string? Platform { get; set; }

    /// <summary>Filtros a los que pertenece el juego.</summary>
    public List<string> FilterIds { get; set; } = [];

    public DateTime AddedAt { get; set; }
    public DateTime? LastPlayed { get; set; }
    public int PlayCount { get; set; }
}
