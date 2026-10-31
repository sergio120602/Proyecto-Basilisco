using System.Text.Json.Serialization;

namespace Basilisco.Models;

public sealed class Game
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";

    /// <summary>Ejecutable, acceso directo o cualquier archivo que se pueda abrir.</summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Emulador con el que se abre (ver <see cref="Services.Emulators"/>), o null si se abre tal cual. Con emulador,
    /// <see cref="Path"/> es el juego (la imagen del disco o la ROM) y se lanza el emulador con él.
    /// </summary>
    public string? Emulator { get; set; }

    /// <summary>Núcleo de RetroArch con el que se abre (la .dll), si el emulador es RetroArch.</summary>
    public string? EmulatorCore { get; set; }

    /// <summary>Tienda de la que se importó («epic», «gog», «ea», «battlenet»), o null.</summary>
    public string? Store { get; set; }

    /// <summary>Carpeta donde está instalado (los de las tiendas), para saber cuándo sigue abierto aunque se abra con su lanzador.</summary>
    public string? InstallDir { get; set; }

    /// <summary>Juego de Steam (se lanza con steam://rungameid/…), o null si es un archivo.</summary>
    public int? SteamAppId { get; set; }

    /// <summary>Si es de Steam Familiar, quién lo comparte.</summary>
    public string? SteamOwner { get; set; }

    /// <summary>
    /// SteamID de todos los miembros de la familia que tienen el juego (tú incluido), para el menú «Steam familiar».
    /// Vacía en bibliotecas antiguas, hasta que se vuelve a leer la familia de Steam.
    /// </summary>
    public List<string> SteamOwnerIds { get; set; } = [];

    /// <summary>Encuadre de la imagen del disco del juego en la caja abierta (null: lo llena).</summary>
    public ImageFrame? DiscFrame { get; set; }

    /// <summary>Banda sonora: la lista de canciones (de MusicBrainz) y la música importada para cada una.</summary>
    public Soundtrack? Soundtrack { get; set; }

    /// <summary>Manual hecho con imágenes (de Pinterest), en orden. Si tiene, es el que se lee (antes que el PDF).</summary>
    public List<ManualPage>? ManualPages { get; set; }

    /// <summary>El manual se lee de página en página (true) o como un libro abierto, de dos en dos (false: modo libreta).</summary>
    public bool ManualSinglePage { get; set; }

    /// <summary>Giro de las páginas del manual en PDF (número de página → grados), en el modo de una sola página.</summary>
    public Dictionary<int, int>? ManualPdfRotations { get; set; }

    /// <summary>Última vez que se abrió el manual (para soltar antes los que menos se leen si se pasa el tope de espacio).</summary>
    public DateTime? ManualOpened { get; set; }

    /// <summary>Perfil de trofeos (juegos sin logros de Steam): creados a mano o de RetroAchievements, RPCS3 o PlayStation.</summary>
    public TrophyProfile? Trophies { get; set; }

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

    /// <summary>Número máximo de jugadores (de LaunchBox o puesto a mano), para el grupo «Nº Jugadores». Null si no se sabe.</summary>
    public int? Players { get; set; }

    /// <summary>True si ya se buscó en LaunchBox (encontrado o no), para no volver a buscarlo cada vez.</summary>
    public bool PlayersLooked { get; set; }

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

    /// <summary>Imagen del interior de la caja abierta, vista a través del plástico (dentro de basilisco-data/covers).</summary>
    public string? Inside { get; set; }

    /// <summary>Encuadre de la imagen del manual en la caja abierta. Null: llena el librito.</summary>
    public ImageFrame? ManualFrame { get; set; }

    /// <summary>Encuadre de la imagen del interior. Null: llena la caja abierta de lado a lado.</summary>
    public ImageFrame? InsideFrame { get; set; }

    /// <summary>El manual para leer, en PDF (dentro de basilisco-data/manuals). Null: aún no tiene.</summary>
    public string? ManualPdf { get; set; }

    /// <summary>Nombre del manual en PDF que se lee (null en bibliotecas anteriores a la 0.9.2: «Manual en PDF»).</summary>
    public string? ManualPdfName { get; set; }

    /// <summary>Los demás manuales en PDF guardados del juego, para elegir otro al abrir el manual.</summary>
    public List<SavedManual>? OtherManualPdfs { get; set; }

    public DateTime AddedAt { get; set; }
    public DateTime? LastPlayed { get; set; }
    public int PlayCount { get; set; }

    /// <summary>Imágenes del formato pedido (las del rectangular se crean vacías la primera vez).</summary>
    public FaceSet Faces(CoverFormat format) => format == CoverFormat.Portrait ? Portrait ??= new FaceSet() : Square;

    /// <summary>Imágenes del formato que se está usando.</summary>
    [JsonIgnore]
    public FaceSet CurrentFaces => Faces(Format);

    /// <summary>Cuántos manuales tiene para leer: el de Pinterest y los PDF.</summary>
    [JsonIgnore]
    public int ManualCount => (ManualPages is { Count: > 0 } ? 1 : 0) + (ManualPdf is null ? 0 : 1) + (OtherManualPdfs?.Count ?? 0);

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
