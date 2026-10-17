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
