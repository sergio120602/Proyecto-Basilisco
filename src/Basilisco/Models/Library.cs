namespace Basilisco.Models;

/// <summary>Todo lo que se guarda en library.json.</summary>
public sealed class Library
{
    public int Version { get; set; } = 1;
    public List<Game> Games { get; set; } = [];
    public List<GameFilter> Filters { get; set; } = [];
}
