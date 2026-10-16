namespace Basilisco.Models;

public sealed class Game
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";

    /// <summary>Ejecutable, acceso directo o cualquier archivo que se pueda abrir.</summary>
    public string Path { get; set; } = "";

    public DateTime AddedAt { get; set; }
    public DateTime? LastPlayed { get; set; }
    public int PlayCount { get; set; }
}
