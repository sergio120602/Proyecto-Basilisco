using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Basilisco.Models;

namespace Basilisco.Services;

/// <summary>Logros de un juego de Steam: cuántos tienes de cuántos. Total 0: el juego no tiene logros.</summary>
public sealed record AchievementProgress(int Unlocked, int Total, DateTime Read)
{
    public double Fraction => Total == 0 ? 0 : (double)Unlocked / Total;
    public bool Complete => Total > 0 && Unlocked >= Total;
}

/// <summary>Un logro del juego, para la página de logros.</summary>
/// <param name="Hidden">Logro oculto: Steam no da su descripción hasta que se consigue.</param>
/// <param name="GlobalPercent">Tanto por ciento de los jugadores que lo tienen (null si Steam no lo da).</param>
public sealed record AchievementInfo(string ApiName, string Name, string Description, bool Hidden, string? Icon, string? IconGray,
                                     bool Unlocked, DateTime? UnlockedAt, double? GlobalPercent)
{
    /// <summary>
    /// Bronce los fáciles (los tiene la mitad o más de los jugadores), plata los de dificultad media (del 15 al 50 %) y oro
    /// los difíciles (menos del 15 %). Sin el dato, bronce.
    /// </summary>
    public TrophyTier Tier => TierFor(GlobalPercent);

    public static TrophyTier TierFor(double? percent) => percent switch
    {
        null or >= 50 => TrophyTier.Bronze,
        >= 15 => TrophyTier.Silver,
        _ => TrophyTier.Gold,
    };
}

/// <summary>Por qué no se pueden leer los logros, en una frase para enseñar.</summary>
public sealed class AchievementsException(string message) : Exception(message);

/// <summary>
/// Logros de Steam de tu cuenta, con la clave de la Web API (ISteamUserStats/GetPlayerAchievements). Lo último que se
/// supo de cada juego se guarda en basilisco-data/achievements.json, para enseñarlo al momento al elegir el juego; y se
/// vuelve a pedir a Steam si tiene más de 30 minutos.
/// </summary>
public static class SteamAchievements
{
    static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);
    static readonly string CachePath = Path.Combine(AppInfo.DataDirectory, "achievements.json");
    static readonly HttpClient Http = CreateClient();
    static Dictionary<int, AchievementProgress>? _cache;

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Basilisco", AppInfo.Version));
        return client;
    }

    static Dictionary<int, AchievementProgress> Cache
    {
        get
        {
            if (_cache is not null)
                return _cache;
            try
            {
                _cache = File.Exists(CachePath)
                    ? JsonSerializer.Deserialize<Dictionary<int, AchievementProgress>>(File.ReadAllText(CachePath)) ?? []
                    : [];
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _cache = [];
            }
            return _cache;
        }
    }

    /// <summary>Lo último que se supo del juego (puede ser viejo), o null si nunca se ha leído.</summary>
    public static AchievementProgress? Cached(int appId) => Cache.GetValueOrDefault(appId);

    /// <summary>True si hay que volver a preguntar a Steam (no se sabe o tiene más de 30 minutos).</summary>
    public static bool IsStale(int appId) => Cached(appId) is not { } known || DateTime.Now - known.Read > MaxAge;

    /// <summary>Pide a Steam los logros del juego y los guarda.</summary>
    /// <exception cref="AchievementsException">Si Steam no los da (perfil privado, sin conexión…).</exception>
    public static async Task<AchievementProgress> FetchAsync(int appId, string steamId, string apiKey, CancellationToken cancellation)
    {
        var url = $"https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v1/?appid={appId}&steamid={steamId}" +
                  $"&key={Uri.EscapeDataString(apiKey)}&l=spanish";
        HttpResponseMessage response;
        try
        {
            response = await Http.GetAsync(url, cancellation);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
        {
            throw new AchievementsException("No se ha podido conectar con Steam para ver los logros.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellation);
            string? error = null;
            var unlocked = 0;
            var total = 0;
            try
            {
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.TryGetProperty("playerstats", out var stats))
                {
                    if (stats.TryGetProperty("error", out var message))
                        error = message.GetString();
                    if (stats.TryGetProperty("achievements", out var list) && list.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var achievement in list.EnumerateArray())
                        {
                            total++;
                            if (achievement.TryGetProperty("achieved", out var achieved) && achieved.TryGetInt32(out var value) && value == 1)
                                unlocked++;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new AchievementsException("Steam ha rechazado la clave de la Web API.");
                throw new AchievementsException("Steam no ha dado los logros. Prueba más tarde.");
            }

            if (error is not null)
            {
                // «Requested app has no stats»: el juego no tiene logros (o nunca lo has abierto).
                if (error.Contains("no stats", StringComparison.OrdinalIgnoreCase))
                    total = unlocked = 0;
                else if (error.Contains("not public", StringComparison.OrdinalIgnoreCase) || response.StatusCode == HttpStatusCode.Forbidden)
                    throw new AchievementsException("Tu perfil de Steam no deja ver los logros: pon «Detalles de juegos» como públicos.");
                else
                    throw new AchievementsException($"Steam no ha dado los logros ({error}).");
            }
            else if (!response.IsSuccessStatusCode)
            {
                throw new AchievementsException(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? "Steam ha rechazado la clave de la Web API."
                    : "Steam no ha dado los logros. Prueba más tarde.");
            }

            var progress = new AchievementProgress(unlocked, total, DateTime.Now);
            Cache[appId] = progress;
            Save();
            return progress;
        }
    }

    static readonly Dictionary<int, (DateTime Read, List<AchievementInfo> List)> Details = [];

    /// <summary>
    /// Todos los logros del juego para la página de logros: nombre, descripción e iconos (GetSchemaForGame), si los tienes
    /// (GetPlayerAchievements) y cuántos jugadores los tienen (GetGlobalAchievementPercentagesForApp). Se recuerdan 30 minutos.
    /// </summary>
    public static async Task<List<AchievementInfo>> DetailsAsync(int appId, string steamId, string apiKey, CancellationToken cancellation)
    {
        if (Details.TryGetValue(appId, out var known) && DateTime.Now - known.Read < MaxAge)
            return known.List;

        var auth = $"key={Uri.EscapeDataString(apiKey)}";
        var schemaTask = GetJsonAsync($"https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?{auth}&appid={appId}&l=spanish", cancellation);
        var playerTask = GetJsonAsync($"https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v1/?{auth}&appid={appId}&steamid={steamId}&l=spanish", cancellation);
        var globalTask = GetJsonAsync($"https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/?gameid={appId}", cancellation);

        using var schema = await schemaTask ?? throw new AchievementsException("Steam no ha dado la lista de logros de este juego. Prueba más tarde.");
        using var player = await playerTask;
        using var global = await globalTask;

        // Tus logros: cuáles tienes y cuándo los conseguiste.
        var mine = new Dictionary<string, DateTime?>();
        if (player?.RootElement.TryGetProperty("playerstats", out var stats) == true)
        {
            if (stats.TryGetProperty("error", out var error) && error.GetString() is { } message && !message.Contains("no stats", StringComparison.OrdinalIgnoreCase))
                throw new AchievementsException(message.Contains("not public", StringComparison.OrdinalIgnoreCase)
                    ? "Tu perfil de Steam no deja ver los logros: pon «Detalles de juegos» como públicos."
                    : $"Steam no ha dado tus logros ({message}).");
            if (stats.TryGetProperty("achievements", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var achievement in list.EnumerateArray())
                {
                    if (achievement.TryGetProperty("apiname", out var name) && name.GetString() is { } api &&
                        achievement.TryGetProperty("achieved", out var achieved) && achieved.TryGetInt32(out var value) && value == 1)
                    {
                        mine[api] = achievement.TryGetProperty("unlocktime", out var time) && time.TryGetInt64(out var seconds) && seconds > 0
                            ? DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime
                            : null;
                    }
                }
            }
        }

        // Cuántos jugadores tienen cada uno (en tanto por ciento; a veces llega como texto).
        var percents = new Dictionary<string, double>();
        if (global?.RootElement.TryGetProperty("achievementpercentages", out var percentages) == true &&
            percentages.TryGetProperty("achievements", out var globalList) && globalList.ValueKind == JsonValueKind.Array)
        {
            foreach (var achievement in globalList.EnumerateArray())
            {
                if (!achievement.TryGetProperty("name", out var name) || name.GetString() is not { } api || !achievement.TryGetProperty("percent", out var percent))
                    continue;
                if (percent.ValueKind == JsonValueKind.Number)
                    percents[api] = percent.GetDouble();
                else if (double.TryParse(percent.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    percents[api] = parsed;
            }
        }

        var result = new List<AchievementInfo>();
        if (schema.RootElement.TryGetProperty("game", out var game) && game.TryGetProperty("availableGameStats", out var available) &&
            available.TryGetProperty("achievements", out var achievements) && achievements.ValueKind == JsonValueKind.Array)
        {
            foreach (var achievement in achievements.EnumerateArray())
            {
                if (!achievement.TryGetProperty("name", out var name) || name.GetString() is not { } api)
                    continue;
                string? Text(string property) => achievement.TryGetProperty(property, out var value) ? value.GetString() : null;
                var unlocked = mine.TryGetValue(api, out var when);
                result.Add(new AchievementInfo(api, Text("displayName") ?? api, Text("description") ?? "",
                    achievement.TryGetProperty("hidden", out var hidden) && hidden.TryGetInt32(out var h) && h == 1,
                    Text("icon"), Text("icongray"), unlocked, when, percents.TryGetValue(api, out var p) ? p : null));
            }
        }

        Details[appId] = (DateTime.Now, result);
        Cache[appId] = new AchievementProgress(result.Count(a => a.Unlocked), result.Count, DateTime.Now);
        Save();
        return result;
    }

    /// <summary>Pide un JSON a Steam; null si no responde o responde con un error sin JSON.</summary>
    static async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken cancellation)
    {
        try
        {
            using var response = await Http.GetAsync(url, cancellation);
            var body = await response.Content.ReadAsStringAsync(cancellation);
            return JsonDocument.Parse(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
        {
            return null;
        }
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(AppInfo.DataDirectory);
            var temp = CachePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Cache));
            File.Move(temp, CachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
