namespace Basilisco.Models;

/// <summary>Un manual en PDF del juego que no es el que se lee ahora (se puede elegir al abrir el manual).</summary>
/// <param name="File">El PDF, dentro de basilisco-data/manuals.</param>
/// <param name="Name">Cómo se llama en los menús («Manual original (Games Database)», el nombre del archivo…).</param>
/// <param name="Rotations">Giro de sus páginas (número de página → grados), como <see cref="Game.ManualPdfRotations"/>.</param>
public sealed record SavedManual(string File, string Name, Dictionary<int, int>? Rotations = null);
