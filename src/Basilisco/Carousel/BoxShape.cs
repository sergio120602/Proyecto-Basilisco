using Basilisco.Models;

namespace Basilisco.Carousel;

/// <summary>
/// Medidas de una caja en la escena 3D. La cuadrada mide siempre lo mismo; las rectangulares tienen las medidas
/// reales de la caja de su consola, a escala: 19 cm (el alto de una caja de DVD) es 1 unidad, así que una caja
/// de PS1 o de DS se ve más baja que una de PS2, como en una estantería.
/// </summary>
public sealed record BoxShape(double Width, double Height, double Depth)
{
    const double ReferenceCm = 19.0;

    public static readonly BoxShape Square = new((double)CoverArt.Width / CoverArt.Height, 1.0, 0.08);

    // Alto × ancho × grosor en centímetros de cada caja real.
    static readonly BoxShape Dvd = Cm(19.0, 13.5, 1.4);

    public static BoxShape Of(CarouselItem item) =>
        item.IsAddCard ? Square : Of(item.Format, item.Platform);

    public static BoxShape Of(CoverFormat format, string? platform) =>
        format == CoverFormat.Square ? Square : platform switch
        {
            "ps1" => Cm(12.5, 14.2, 1.0),   // Jewel case de CD (EE. UU.)
            "ps2" => Dvd,                   // Caja de DVD
            "ps3" => Cm(17.0, 13.5, 1.4),   // Caja de Blu-ray de PS3
            "psp" => Cm(18.0, 10.5, 1.5),   // Caja de UMD
            "xbox" => Dvd,                  // Caja de DVD verde
            "x360" => Dvd,                  // Caja de DVD verde transparente
            "gb" => Cm(12.8, 12.8, 2.5),    // Caja de cartón de Game Boy
            "gba" => Cm(12.5, 12.5, 2.0),   // Caja de cartón de Game Boy Advance (aprox.)
            "n64" => Cm(12.7, 18.0, 3.4),   // Caja de cartón apaisada de Nintendo 64 (aprox.)
            "gc" => Cm(18.7, 13.5, 1.4),    // Estuche de mini-DVD de GameCube (aprox.)
            "wii" => Dvd,                   // Caja de DVD blanca
            "wiiu" => Cm(19.0, 13.5, 1.4),  // Caja azul de Wii U: 190 × 135 × 14 mm, como una de DVD
            "nds" => Cm(13.6, 12.4, 1.9),   // Estuche de DS (Europa)
            "nes" => Cm(18.4, 12.7, 3.8),   // Caja de cartón de NES (EE. UU.)
            "snes" => Cm(13.3, 18.4, 3.6),  // Caja de cartón apaisada de Super Nintendo (EE. UU.)
            "switch" => Cm(17.0, 10.5, 1.1),// Estuche rojo de Nintendo Switch
            "pc" => Cm(19.0, 19.0 * 600 / 900, 1.4), // Proporción 600 × 900 (2:3), la de las carátulas de Steam
            _ => Dvd,
        };

    static BoxShape Cm(double height, double width, double depth) =>
        new(width / ReferenceCm, height / ReferenceCm, depth / ReferenceCm);
}
