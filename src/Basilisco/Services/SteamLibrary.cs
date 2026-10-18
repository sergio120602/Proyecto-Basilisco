using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Basilisco.Services;

/// <summary>Cuenta de Steam iniciada en este PC.</summary>
public sealed record SteamAccount(string SteamId, string PersonaName);

/// <summary>De dónde sale un juego de Steam.</summary>
public enum SteamSource { Own, Family }

/// <summary>Un juego de la cuenta de Steam (propio o compartido por la familia).</summary>
/// <param name="InstallDir">Carpeta del juego si está instalado en este PC.</param>
/// <param name="Owner">Quién lo comparte, si es de la familia.</param>
public sealed record SteamApp(int AppId, string Name, SteamSource Source, bool Installed, string? InstallDir, string? Owner, int PlaytimeMinutes)
{
    public string LaunchUri => $"steam://rungameid/{AppId}";
}

/// <summary>Lee la instalación local de Steam: cuentas, bibliotecas y juegos instalados. No necesita internet.</summary>
public static partial class SteamLocal
{
    // Herramientas que Steam instala como si fueran juegos.
    static readonly HashSet<int> NotGames = [228980, 1070560, 1391110, 1628350, 1493710, 2180100, 1826330];

    /// <summary>Carpeta de Steam (del registro), o null si no está instalado.</summary>
    public static string? SteamPath()
    {
        var path = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                   ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        return path is not null && Directory.Exists(path) ? Path.GetFullPath(path) : null;
    }

    /// <summary>Cuentas con las que se ha iniciado sesión en este PC; la más reciente primero.</summary>
    public static List<SteamAccount> Accounts()
    {
        if (SteamPath() is not { } steam || Read(Path.Combine(steam, "config", "loginusers.vdf")) is not { } file ||
            file.GetValueOrDefault("users") is not Dictionary<string, object> users)
            return [];

        return users
            .Where(user => user.Value is Dictionary<string, object>)
            .Select(user => (Id: user.Key, Data: (Dictionary<string, object>)user.Value))
            .OrderByDescending(user => Text(user.Data, "MostRecent") == "1")
            .ThenByDescending(user => long.TryParse(Text(user.Data, "Timestamp"), out var time) ? time : 0)
            .Select(user => new SteamAccount(user.Id, Text(user.Data, "PersonaName") ?? user.Id))
            .ToList();
    }

    static (DateTime When, List<string> Folders)? _folders;

    /// <summary>Carpetas de biblioteca de Steam (puede haber varias, en distintos discos). Se recuerdan 30 s.</summary>
    public static List<string> LibraryFolders()
    {
        if (_folders is { } cached && DateTime.Now - cached.When < TimeSpan.FromSeconds(30))
            return cached.Folders;
        var folders = ReadLibraryFolders();
        _folders = (DateTime.Now, folders);
        return folders;
    }

    static List<string> ReadLibraryFolders()
    {
        if (SteamPath() is not { } steam)
            return [];

        var folders = new List<string> { steam };
        if (Read(Path.Combine(steam, "steamapps", "libraryfolders.vdf"))?.GetValueOrDefault("libraryfolders") is Dictionary<string, object> libraries)
        {
            foreach (var library in libraries.Values.OfType<Dictionary<string, object>>())
            {
                if (Text(library, "path") is { } path && Directory.Exists(path))
                    folders.Add(Path.GetFullPath(path));
            }
        }
        return folders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Juegos instalados. Los que instaló otro miembro de la familia se marcan como de la familia.</summary>
    public static List<SteamApp> InstalledApps(string? mySteamId)
    {
        var apps = new Dictionary<int, SteamApp>();
        foreach (var library in LibraryFolders())
        {
            var steamapps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamapps))
                continue;

            foreach (var manifest in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
            {
                if (Read(manifest)?.GetValueOrDefault("AppState") is not Dictionary<string, object> state ||
                    !int.TryParse(Text(state, "appid"), out var appId) || NotGames.Contains(appId) ||
                    Text(state, "name") is not { Length: > 0 } name || IsTool(name))
                    continue;

                var installDir = Text(state, "installdir") is { } dir ? Path.Combine(steamapps, "common", dir) : null;
                var owner = Text(state, "LastOwner");
                var family = mySteamId is not null && owner is not null && owner != "0" && owner != mySteamId;
                apps[appId] = new SteamApp(appId, name, family ? SteamSource.Family : SteamSource.Own, true, installDir, family ? owner : null, 0);
            }
        }
        return apps.Values.ToList();
    }

    /// <summary>
    /// Minutos jugados y última partida de cada juego según Steam, de la cuenta con la sesión más reciente en este PC
    /// (userdata/&lt;cuenta&gt;/config/localconfig.vdf). Incluye los juegos de la familia. Sin Steam: vacío.
    /// </summary>
    public static Dictionary<int, (int Minutes, DateTime? LastPlayed)> Playtimes()
    {
        var times = new Dictionary<int, (int, DateTime?)>();
        if (SteamPath() is not { } steam || Accounts().FirstOrDefault() is not { } account ||
            !ulong.TryParse(account.SteamId, out var steamId64) || steamId64 < SteamId64Base)
            return times;

        var file = Path.Combine(steam, "userdata", (steamId64 - SteamId64Base).ToString(), "config", "localconfig.vdf");
        if (Read(file)?.GetValueOrDefault("UserLocalConfigStore") is not Dictionary<string, object> store ||
            Child(Child(Child(Child(store, "Software"), "Valve"), "Steam"), "apps") is not { } apps)
            return times;

        foreach (var (key, value) in apps)
        {
            if (!int.TryParse(key, out var appId) || appId == 0 || value is not Dictionary<string, object> app ||
                !int.TryParse(Text(app, "Playtime"), out var minutes))
                continue;
            DateTime? lastPlayed = long.TryParse(Text(app, "LastPlayed"), out var seconds) && seconds > 0
                ? DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime
                : null;
            times[appId] = (minutes, lastPlayed);
        }
        return times;
    }

    // SteamID64 de la cuenta 0; la carpeta de userdata es el SteamID64 menos esto.
    const ulong SteamId64Base = 76561197960265728;

    static Dictionary<string, object>? Child(Dictionary<string, object>? data, string key) =>
        data?.GetValueOrDefault(key) as Dictionary<string, object>;

    /// <summary>Carpeta donde está instalado el juego (steamapps/common/…), o null si no está instalado.</summary>
    public static string? InstallDirOf(int appId)
    {
        foreach (var library in LibraryFolders())
        {
            var steamapps = Path.Combine(library, "steamapps");
            if (Read(Path.Combine(steamapps, $"appmanifest_{appId}.acf"))?.GetValueOrDefault("AppState") is Dictionary<string, object> state &&
                Text(state, "installdir") is { Length: > 0 } dir)
                return Path.Combine(steamapps, "common", dir);
        }
        return null;
    }

    /// <summary>True si el juego está instalado en alguna biblioteca de este PC.</summary>
    public static bool IsInstalled(int appId) =>
        LibraryFolders().Any(library => File.Exists(Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf")));

    static bool IsTool(string name) =>
        name.Contains("Redistributable", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Proton", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Steamworks Shared", StringComparison.OrdinalIgnoreCase);

    static string? Text(Dictionary<string, object> data, string key) => data.GetValueOrDefault(key) as string;

    static Dictionary<string, object>? Read(string path)
    {
        try
        {
            return File.Exists(path) ? Vdf.Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>Lector mínimo del formato de texto de Valve (KeyValues / VDF) que usa Steam para su configuración.</summary>
public static class Vdf
{
    public static Dictionary<string, object> Parse(string text)
    {
        var index = 0;
        return ParseObject(text, ref index);
    }

    static Dictionary<string, object> ParseObject(string s, ref int i)
    {
        var data = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            SkipSpace(s, ref i);
            if (i >= s.Length)
                return data;
            if (s[i] == '}')
            {
                i++;
                return data;
            }

            if (ReadToken(s, ref i) is not { } key)
                return data;
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == '{')
            {
                i++;
                data[key] = ParseObject(s, ref i);
            }
            else if (ReadToken(s, ref i) is { } value)
            {
                data[key] = value;
            }
            else
            {
                return data;
            }
        }
    }

    static void SkipSpace(string s, ref int i)
    {
        while (i < s.Length)
        {
            if (char.IsWhiteSpace(s[i]))
                i++;
            else if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/')
                while (i < s.Length && s[i] != '\n') i++;
            else
                return;
        }
    }

    static string? ReadToken(string s, ref int i)
    {
        if (i >= s.Length)
            return null;

        var builder = new StringBuilder();
        if (s[i] == '"')
        {
            i++;
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    builder.Append(s[i] switch { 'n' => '\n', 't' => '\t', _ => s[i] });
                }
                else
                {
                    builder.Append(s[i]);
                }
                i++;
            }
            i++;
            return builder.ToString();
        }

        while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '{' && s[i] != '}' && s[i] != '"')
            builder.Append(s[i++]);
        return builder.Length > 0 ? builder.ToString() : null;
    }
}

/// <summary>
/// Steam Web API. La biblioteca completa se pide con la clave de la API (permanente; el perfil debe tener los
/// juegos visibles) o con el token de sesión. Steam Familiar solo admite el token de sesión, que caduca en un día.
/// </summary>
public static partial class SteamWeb
{
    public const string ApiKeyUrl = "https://steamcommunity.com/dev/apikey";

    /// <summary>Página que, con la sesión de Steam iniciada en el navegador, enseña el token («webapi_token»).</summary>
    public const string TokenUrl = "https://store.steampowered.com/pointssummary/ajaxgetasyncconfig";

    const string Api = "https://api.steampowered.com";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(40) };

    /// <summary>
    /// Acepta el token tal cual o la página entera que devuelve Steam ({"data":{"webapi_token":"…"}}).
    /// Devuelve el token y, si se puede leer, la cuenta y la hora a la que caduca.
    /// </summary>
    public static (string Token, string? SteamId, DateTime? Expires)? ParseToken(string pasted)
    {
        var text = pasted.Trim();
        if (TokenInJson().Match(text) is { Success: true } match)
            text = match.Groups[1].Value;
        if (text.Length < 20 || text.Contains(' '))
            return null;

        // Es un JWT: la parte del medio dice de quién es («sub») y cuándo caduca («exp»).
        string? steamId = null;
        DateTime? expires = null;
        var parts = text.Split('.');
        if (parts.Length == 3)
        {
            try
            {
                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
                if (json.RootElement.TryGetProperty("sub", out var sub))
                    steamId = sub.GetString();
                if (json.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds))
                    expires = DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime;
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
            }
        }
        return (text, steamId, expires);
    }

    /// <summary>Juegos de la cuenta (instalados o no). Con clave o con token; el token también sirve con el perfil privado.</summary>
    public static async Task<List<SteamApp>> OwnedGamesAsync(string steamId, string? apiKey, string? token, CancellationToken cancellation)
    {
        var auth = token is not null ? $"access_token={Uri.EscapeDataString(token)}" : $"key={Uri.EscapeDataString(apiKey ?? "")}";
        using var json = await GetAsync($"{Api}/IPlayerService/GetOwnedGames/v1/?{auth}&steamid={steamId}" +
                                        "&include_appinfo=1&include_played_free_games=1&skip_unvetted_apps=0&format=json", cancellation);
        var apps = new List<SteamApp>();
        if (!json.RootElement.TryGetProperty("response", out var response))
            return apps;
        if (!response.TryGetProperty("games", out var games))
        {
            throw new ArtProviderException(token is null
                ? "Steam no ha devuelto ningún juego. Si tu perfil es privado, pon «Detalles de juegos» como públicos en la privacidad de tu perfil, o usa el token de sesión."
                : "Steam no ha devuelto ningún juego de esta cuenta.");
        }

        foreach (var game in games.EnumerateArray())
        {
            if (!game.TryGetProperty("appid", out var id) || !id.TryGetInt32(out var appId))
                continue;
            var name = game.TryGetProperty("name", out var n) ? n.GetString() ?? $"App {appId}" : $"App {appId}";
            var minutes = game.TryGetProperty("playtime_forever", out var p) && p.TryGetInt32(out var m) ? m : 0;
            apps.Add(new SteamApp(appId, name, SteamSource.Own, false, null, null, minutes));
        }
        return apps;
    }

    /// <summary>Juegos que comparte contigo tu familia de Steam (sin los tuyos ni los excluidos del préstamo).</summary>
    public static async Task<(string? FamilyName, List<SteamApp> Apps)> FamilyGamesAsync(string steamId, string token, string? apiKey, CancellationToken cancellation)
    {
        var auth = $"access_token={Uri.EscapeDataString(token)}";
        using var group = await GetAsync($"{Api}/IFamilyGroupsService/GetFamilyGroupForUser/v1/?{auth}&steamid={steamId}&include_family_group_response=true", cancellation);
        var response = group.RootElement.GetProperty("response");
        if (!response.TryGetProperty("family_groupid", out var groupId) || response.TryGetProperty("is_not_member_of_any_group", out var none) && none.GetBoolean())
            throw new ArtProviderException("Esta cuenta no pertenece a ninguna familia de Steam.");

        string? familyName = null;
        if (response.TryGetProperty("family_group", out var family) && family.TryGetProperty("name", out var name))
            familyName = name.GetString();

        using var shared = await GetAsync($"{Api}/IFamilyGroupsService/GetSharedLibraryApps/v1/?{auth}&family_groupid={groupId.GetString() ?? groupId.ToString()}" +
                                          $"&include_own=false&include_excluded=false&include_free=false&include_non_games=false&steamid={steamId}&language=spanish", cancellation);
        var apps = new List<(int AppId, string Name, string? Owner)>();
        if (shared.RootElement.GetProperty("response").TryGetProperty("apps", out var list))
        {
            foreach (var app in list.EnumerateArray())
            {
                if (!app.TryGetProperty("appid", out var id) || !id.TryGetInt32(out var appId))
                    continue;
                var appName = app.TryGetProperty("name", out var n) ? n.GetString() ?? $"App {appId}" : $"App {appId}";
                var owner = app.TryGetProperty("owner_steamids", out var owners) && owners.GetArrayLength() > 0 ? owners[0].GetString() : null;
                apps.Add((appId, appName, owner));
            }
        }

        // Nombre de cada dueño en lugar de su SteamID, si Steam lo da.
        var names = await PersonaNamesAsync(apps.Select(app => app.Owner).OfType<string>().Distinct().ToList(), apiKey, token, cancellation);
        return (familyName, apps.Select(app => new SteamApp(app.AppId, app.Name, SteamSource.Family, false, null,
            app.Owner is null ? null : names.GetValueOrDefault(app.Owner, app.Owner), 0)).ToList());
    }

    /// <summary>Nombres de perfil de varias cuentas. Si Steam no responde, se devuelve vacío (se enseñará el SteamID).</summary>
    public static async Task<Dictionary<string, string>> PersonaNamesAsync(List<string> steamIds, string? apiKey, string? token, CancellationToken cancellation)
    {
        var names = new Dictionary<string, string>();
        if (steamIds.Count == 0)
            return names;

        var auth = apiKey is { Length: > 0 } ? $"key={Uri.EscapeDataString(apiKey)}" : token is not null ? $"access_token={Uri.EscapeDataString(token)}" : null;
        if (auth is null)
            return names;

        try
        {
            using var json = await GetAsync($"{Api}/ISteamUser/GetPlayerSummaries/v2/?{auth}&steamids={string.Join(',', steamIds.Take(100))}", cancellation);
            if (json.RootElement.GetProperty("response").TryGetProperty("players", out var players))
            {
                foreach (var player in players.EnumerateArray())
                {
                    if (player.TryGetProperty("steamid", out var id) && player.TryGetProperty("personaname", out var persona) &&
                        id.GetString() is { } key && persona.GetString() is { } value)
                        names[key] = value;
                }
            }
        }
        catch (ArtProviderException)
        {
        }
        return names;
    }

    static async Task<JsonDocument> GetAsync(string url, CancellationToken cancellation)
    {
        HttpResponseMessage response;
        try
        {
            response = await Http.GetAsync(url, cancellation);
        }
        catch (HttpRequestException)
        {
            throw new ArtProviderException("No se ha podido conectar con Steam. Comprueba tu conexión a internet.");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new ArtProviderException("Steam ha rechazado la clave o el token. Si es el token, puede haber caducado (dura unas 24 horas): consigue uno nuevo.");
            if (!response.IsSuccessStatusCode)
                throw new ArtProviderException($"Steam ha respondido con un error ({(int)response.StatusCode}). Prueba más tarde.");

            try
            {
                return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            }
            catch (JsonException)
            {
                throw new ArtProviderException("Steam ha devuelto una respuesta que no se entiende. Prueba más tarde.");
            }
        }
    }

    [GeneratedRegex("\"webapi_token\"\\s*:\\s*\"([^\"]+)\"")]
    private static partial Regex TokenInJson();
}
