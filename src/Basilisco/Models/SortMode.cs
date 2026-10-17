using System.Text.Json.Serialization;

namespace Basilisco.Models;

/// <summary>Cómo se ordenan los juegos en el carrusel.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SortMode>))]
public enum SortMode
{
    /// <summary>El orden que pone el usuario a mano (Ctrl + ← →).</summary>
    Custom,
    NameAscending,
    NameDescending,
    ReleaseNewest,
    ReleaseOldest,
    AddedNewest,
    AddedOldest,
}

public static class SortModes
{
    public static string Label(SortMode mode) => mode switch
    {
        SortMode.Custom => "Personalizado",
        SortMode.NameAscending => "Alfabético (A → Z)",
        SortMode.NameDescending => "Alfabético (Z → A)",
        SortMode.ReleaseNewest => "Lanzamiento: más nuevos primero",
        SortMode.ReleaseOldest => "Lanzamiento: más antiguos primero",
        SortMode.AddedNewest => "Añadidos: más recientes primero",
        SortMode.AddedOldest => "Añadidos: más antiguos primero",
        _ => mode.ToString(),
    };

    /// <summary>Texto corto para el botón de arriba.</summary>
    public static string Short(SortMode mode) => mode switch
    {
        SortMode.Custom => "Personalizado",
        SortMode.NameAscending => "A → Z",
        SortMode.NameDescending => "Z → A",
        SortMode.ReleaseNewest => "Lanzamiento ↓",
        SortMode.ReleaseOldest => "Lanzamiento ↑",
        SortMode.AddedNewest => "Añadidos ↓",
        SortMode.AddedOldest => "Añadidos ↑",
        _ => mode.ToString(),
    };

    public static bool UsesReleaseDate(SortMode mode) => mode is SortMode.ReleaseNewest or SortMode.ReleaseOldest;

    /// <summary>
    /// Ordena los juegos. En el personalizado se respeta el orden de la biblioteca; los que no tienen fecha
    /// de lanzamiento van siempre al final, y a igualdad se desempata por nombre.
    /// </summary>
    public static IEnumerable<Game> Apply(IEnumerable<Game> games, SortMode mode)
    {
        var byName = StringComparer.Create(System.Globalization.CultureInfo.CurrentCulture,
            System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.NumericOrdering);
        return mode switch
        {
            SortMode.NameAscending => games.OrderBy(game => game.Name, byName),
            SortMode.NameDescending => games.OrderByDescending(game => game.Name, byName),
            SortMode.ReleaseNewest => games.OrderBy(game => game.ReleaseDate is null).ThenByDescending(game => game.ReleaseDate).ThenBy(game => game.Name, byName),
            SortMode.ReleaseOldest => games.OrderBy(game => game.ReleaseDate is null).ThenBy(game => game.ReleaseDate).ThenBy(game => game.Name, byName),
            SortMode.AddedNewest => games.OrderByDescending(game => game.AddedAt).ThenBy(game => game.Name, byName),
            SortMode.AddedOldest => games.OrderBy(game => game.AddedAt).ThenBy(game => game.Name, byName),
            _ => games,
        };
    }
}
