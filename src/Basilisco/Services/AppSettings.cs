using System.IO;
using System.Text.Json;

namespace Basilisco.Services;

/// <summary>
/// Ajustes de la aplicación, guardados junto al .exe en basilisco-data/settings.json para que sigan siendo portables.
/// </summary>
public sealed class AppSettings
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    static string FilePath => Path.Combine(AppInfo.DataDirectory, "settings.json");

    /// <summary>Clave de la API de SteamGridDB, para descargar portadas.</summary>
    public string? SteamGridDbApiKey { get; set; }

    /// <summary>Clave de la API de TheGamesDB, para descargar contraportadas.</summary>
    public string? TheGamesDbApiKey { get; set; }

    /// <summary>Clave de la Web API de Steam, para importar la biblioteca completa. El token de sesión no se guarda nunca.</summary>
    public string? SteamApiKey { get; set; }

    /// <summary>Cuenta de RetroAchievements: tu usuario y la clave de la Web API (retroachievements.org → Ajustes).</summary>
    public string? RetroAchievementsUser { get; set; }
    public string? RetroAchievementsApiKey { get; set; }

    /// <summary>Clave de la API de YouTube (opcional): con ella, el vídeo de cada canción se busca solo.</summary>
    public string? YouTubeApiKey { get; set; }

    /// <summary>Volumen de la banda sonora en la caja abierta (de 0 a 1).</summary>
    public double SoundtrackVolume { get; set; } = 0.7;

    /// <summary>Tope de espacio de las páginas de los manuales de imágenes, en MB (si se pasa, se sueltan las de los menos leídos).</summary>
    public int ManualSpaceLimitMb { get; set; } = 2048;

    /// <summary>Carpeta de RPCS3 (o directamente la de sus trofeos), para leer los trofeos de los juegos de PS3.</summary>
    public string? Rpcs3Folder { get; set; }

    /// <summary>El ejecutable de cada emulador (por su id), si lo ha elegido el usuario o se ha encontrado.</summary>
    public Dictionary<string, string> EmulatorPaths { get; set; } = [];

    /// <summary>Los argumentos con los que se abre cada emulador, si se han cambiado ({game}: el juego; {core}: el núcleo).</summary>
    public Dictionary<string, string> EmulatorArguments { get; set; } = [];

    /// <summary>Carpetas de juegos añadidas a mano para cada emulador (además de las que tenga en su configuración).</summary>
    public Dictionary<string, List<string>> EmulatorFolders { get; set; } = [];

    /// <summary>Orden de los juegos en el carrusel.</summary>
    public Models.SortMode SortMode { get; set; } = Models.SortMode.Custom;

    /// <summary>Fondo general para todos los juegos (archivo en basilisco-data/backgrounds). Null: el de siempre.</summary>
    public string? Background { get; set; }

    public bool BackgroundIsVideo { get; set; }

    /// <summary>Color general de las letras (#RRGGBB). Null: el de siempre.</summary>
    public string? TextColor { get; set; }

    /// <summary>Modo claro, nocturno o el mismo que Windows.</summary>
    public ColorMode Theme { get; set; } = ColorMode.System;

    /// <summary>Altura del carrusel (barra «Altura»): con más, las cajas se ven más abajo.</summary>
    public double CarouselShift { get; set; }

    /// <summary>Altura automática: las cajas bajan lo justo para no meterse debajo del nombre del juego.</summary>
    public bool CarouselAutoShift { get; set; } = true;

    /// <summary>Alineación moderna del carrusel: las bases de todas las cajas en línea recta.</summary>
    public bool CarouselAligned { get; set; }

    /// <summary>Calidad de las cajas: la resolución a la que se pintan (menos calidad, menos memoria).</summary>
    public BoxQuality Quality { get; set; } = BoxQuality.High;

    /// <summary>Reflejos de las cajas en el suelo.</summary>
    // Con otro nombre que en la 0.6.2 para que, al actualizar, empiecen quitados (hacen que la animación vaya más suave).
    [System.Text.Json.Serialization.JsonPropertyName("FloorReflections")]
    public bool Reflections { get; set; }

    /// <summary>Los fondos en vídeo se mueven; con false se quedan quietos en el primer fotograma (gastan mucho menos).</summary>
    public bool AnimatedBackgrounds { get; set; } = true;

    /// <summary>Zoom del carrusel (barra de abajo a la derecha).</summary>
    public double Zoom { get; set; } = 1.2;

    /// <summary>Escala de las texturas según la calidad.</summary>
    public double QualityScale => Quality switch { BoxQuality.Medium => 0.75, BoxQuality.Low => 0.5, _ => 1.0 };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return new AppSettings();
    }

    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(AppInfo.DataDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>Calidad de las cajas del carrusel.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<BoxQuality>))]
public enum BoxQuality { High, Medium, Low }
