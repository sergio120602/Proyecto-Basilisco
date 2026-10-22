using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Basilisco.Services;

/// <summary>Logros de un juego de Steam: cuántos tienes de cuántos. Total 0: el juego no tiene logros.</summary>
public sealed record AchievementProgress(int Unlocked, int Total, DateTime Read)
{
    public double Fraction => Total == 0 ? 0 : (double)Unlocked / Total;
    public bool Complete => Total > 0 && Unlocked >= Total;
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
