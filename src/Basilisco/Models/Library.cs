namespace Basilisco.Models;

/// <summary>Todo lo que se guarda en library.json.</summary>
public sealed class Library
{
    /// <summary>
    /// Versión del formato del archivo. 2: la Game Boy pasa de «gba» a «gb» (desde la 0.1.7 «gba» es la Game Boy Advance).
    /// Las bibliotecas nuevas ya nacen en la versión actual; las antiguas traen su número en el archivo.
    /// </summary>
    public int Version { get; set; } = CurrentVersion;

    public const int CurrentVersion = 2;
    public List<Game> Games { get; set; } = [];
    public List<GameFilter> Filters { get; set; } = [];
}
