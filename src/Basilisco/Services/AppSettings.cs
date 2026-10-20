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

    /// <summary>Zoom del carrusel (barra de abajo a la derecha).</summary>
    public double Zoom { get; set; } = 1.2;

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
