using System.Text.Json.Serialization;

namespace Basilisco.Models;

/// <summary>Formato de la caja de un juego.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CoverFormat>))]
public enum CoverFormat
{
    /// <summary>Caja cuadrada con la franja de la consola a la izquierda; carátula de 1024 × 1024.</summary>
    Square,

    /// <summary>Caja rectangular con la franja de la consola arriba; carátula de 600 × 800.</summary>
    Portrait,
}

/// <summary>Cara de la caja con imagen propia.</summary>
public enum Face { Front, Back, Spine }

/// <summary>Lo que hay dentro de la caja abierta: el disco y una imagen que hace de manual. Son del juego, no del formato.</summary>
public enum GameExtra { Disc, Manual }

/// <summary>
/// Imágenes de un juego para un formato: cada formato tiene las suyas, así que al cambiar de formato
/// y volver, el juego recupera su portada, contraportada y lomo de antes.
/// </summary>
public sealed class FaceSet
{
    /// <summary>Archivos dentro de basilisco-data/covers (null: sin imagen propia).</summary>
    public string? Front { get; set; }
    public string? Back { get; set; }
    public string? Spine { get; set; }

    /// <summary>Encuadres (null: la imagen entera).</summary>
    public ImageFrame? FrontFrame { get; set; }
    public ImageFrame? BackFrame { get; set; }
    public ImageFrame? SpineFrame { get; set; }

    [JsonIgnore]
    public bool IsEmpty => Front is null && Back is null && Spine is null;

    public string? Get(Face face) => face switch { Face.Front => Front, Face.Back => Back, _ => Spine };

    public void Set(Face face, string? file)
    {
        switch (face)
        {
            case Face.Front: Front = file; break;
            case Face.Back: Back = file; break;
            default: Spine = file; break;
        }
    }

    public ImageFrame? GetFrame(Face face) => face switch { Face.Front => FrontFrame, Face.Back => BackFrame, _ => SpineFrame };

    /// <summary>El encuadre por defecto no se guarda: el archivo queda limpio y se reconoce como «entera».</summary>
    public void SetFrame(Face face, ImageFrame? frame)
    {
        frame = frame == ImageFrame.Default ? null : frame;
        switch (face)
        {
            case Face.Front: FrontFrame = frame; break;
            case Face.Back: BackFrame = frame; break;
            default: SpineFrame = frame; break;
        }
    }
}
