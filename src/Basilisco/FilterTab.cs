using System.Windows.Media;
using Basilisco.Models;

namespace Basilisco;

/// <summary>Una pestaña de la barra de filtros de arriba.</summary>
public sealed class FilterTab
{
    /// <summary>El filtro que representa; null para «Todos los juegos» y para el botón de crear.</summary>
    public GameFilter? Filter { get; init; }
    public required string Label { get; init; }
    public ImageSource? Icon { get; init; }
    public bool IsSelected { get; init; }
    public bool IsAddButton { get; init; }

    /// <summary>Con imagen, la pestaña muestra solo el logo en lugar del nombre.</summary>
    public bool HasLogo => Icon is not null;

    public string? ToolTip => Filter is null ? null : $"{Label}\nClic derecho para editar el filtro";
}
