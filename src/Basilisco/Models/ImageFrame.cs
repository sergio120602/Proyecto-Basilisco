namespace Basilisco.Models;

/// <summary>
/// Cómo se encuadra una imagen en su cara de la caja. Con el valor por defecto la imagen se ve entera,
/// sin deformar ni recortar; con más zoom se amplía (y lo que sobresale se corta) y se puede mover.
/// </summary>
/// <param name="Zoom">1 = la imagen entera dentro de la cara.</param>
/// <param name="X">Desplazamiento horizontal, en fracción del ancho de la cara (0,1 = un 10 % a la derecha).</param>
/// <param name="Y">Desplazamiento vertical, en fracción del alto de la cara.</param>
/// <param name="Adapted">
/// Solo para la contraportada: true (por defecto) para estirarla justo al tamaño de la cara, el mismo que la portada
/// (600 × 900 en las cajas de PC); false para verla con su proporción original. La imagen guardada no cambia.
/// </param>
public sealed record ImageFrame(double Zoom = 1, double X = 0, double Y = 0, bool Adapted = true)
{
    public const double MinZoom = 0.5;
    public const double MaxZoom = 12;

    public static readonly ImageFrame Default = new();
}
