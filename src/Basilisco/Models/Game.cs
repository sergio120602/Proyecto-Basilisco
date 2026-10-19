using System.Text.Json.Serialization;

namespace Basilisco.Models;

public sealed class Game
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";

    /// <summary>Ejecutable, acceso directo o cualquier archivo que se pueda abrir.</summary>
    public string Path { get; set; } = "";

    /// <summary>Juego de Steam (se lanza con steam://rungameid/…), o null si es un archivo.</summary>
    public int? SteamAppId { get; set; }

    /// <summary>Si es de Steam Familiar, quién lo comparte.</summary>
    public string? SteamOwner { get; set; }

    /// <summary>Formato con el que se ve la caja.</summary>
    public CoverFormat Format { get; set; } = CoverFormat.Square;

    /// <summary>Imágenes de la caja cuadrada.</summary>
    public FaceSet Square { get; set; } = new();

    /// <summary>Imágenes de la caja rectangular (null hasta que se usa ese formato por primera vez).</summary>
    public FaceSet? Portrait { get; set; }

    /// <summary>Consola del juego (ver <see cref="Platforms"/>); decide el borde de su caja. Null si no tiene.</summary>
    public string? Platform { get; set; }

    /// <summary>Filtros a los que pertenece el juego.</summary>
    public List<string> FilterIds { get; set; } = [];

    /// <summary>Fecha de lanzamiento (de LaunchBox o puesta a mano), para ordenar por ella. Null si no se sabe.</summary>
    public DateTime? ReleaseDate { get; set; }

    /// <summary>Minutos jugados (se cuentan solos al jugar desde Basilisco, vienen de Steam o se ponen a mano).</summary>
    public int PlayMinutes { get; set; }

    /// <summary>Fondo propio del juego, dentro de basilisco-data/backgrounds: una imagen o un vídeo. Null: el de siempre.</summary>
    public string? Background { get; set; }

    /// <summary>True si el fondo es un vídeo (fondo dinámico).</summary>
    public bool BackgroundIsVideo { get; set; }

    /// <summary>Color de las letras con este juego seleccionado (#RRGGBB). Null: el de siempre.</summary>
    public string? TextColor { get; set; }

    /// <summary>Imagen del disco (dentro de basilisco-data/covers), para la caja abierta. Null: se dibuja uno con la portada.</summary>
    public string? Disc { get; set; }

    /// <summary>Imagen que hace de manual en la caja abierta (un póster, un fondo del juego…). Null: el interior liso.</summary>
    public string? Manual { get; set; }

    /// <summary>El manual para leer, en PDF (dentro de basilisco-data/manuals). Null: aún no tiene.</summary>
    public string? ManualPdf { get; set; }

    public DateTime AddedAt { get; set; }
    public DateTime? LastPlayed { get; set; }
    public int PlayCount { get; set; }

    /// <summary>Imágenes del formato pedido (las del rectangular se crean vacías la primera vez).</summary>
    public FaceSet Faces(CoverFormat format) => format == CoverFormat.Portrait ? Portrait ??= new FaceSet() : Square;

    /// <summary>Imágenes del formato que se está usando.</summary>
    [JsonIgnore]
    public FaceSet CurrentFaces => Faces(Format);

    // Bibliotecas de versiones anteriores a la 0.1.3: las imágenes estaban sueltas en el juego.
    // Al leerlas pasan a la caja cuadrada; al guardar ya no se escriben.
    [JsonInclude, JsonPropertyName("CoverImage"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? LegacyCoverImage { get => null; set => Square.Front ??= value; }

    [JsonInclude, JsonPropertyName("BackImage"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? LegacyBackImage { get => null; set => Square.Back ??= value; }

    [JsonInclude, JsonPropertyName("BackFrame"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ImageFrame? LegacyBackFrame { get => null; set => Square.BackFrame ??= value; }

    [JsonInclude, JsonPropertyName("SpineImage"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? LegacySpineImage { get => null; set => Square.Spine ??= value; }

    [JsonInclude, JsonPropertyName("SpineFrame"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ImageFrame? LegacySpineFrame { get => null; set => Square.SpineFrame ??= value; }
}
