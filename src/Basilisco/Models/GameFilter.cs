namespace Basilisco.Models;

/// <summary>Grupo de juegos creado por el usuario (por ejemplo «RPG» o «PS1»). Un juego puede estar en varios.</summary>
public sealed class GameFilter
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";

    /// <summary>Nombre del archivo de su imagen dentro de basilisco-data/filters, si tiene.</summary>
    public string? Image { get; set; }
}
