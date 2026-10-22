using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Basilisco.Models;

namespace Basilisco.Services;

/// <summary>Error que se le puede enseñar al usuario tal cual (al conectar un servicio de trofeos).</summary>
public sealed class TrophyServiceException(string message) : Exception(message);

/// <summary>Un juego encontrado en un servicio de trofeos, para vincularlo.</summary>
/// <param name="Detail">Lo que se enseña al lado: la consola, cuántos trofeos tiene…</param>
/// <param name="Service">Para PlayStation, «trophy» o «trophy2».</param>
public sealed record TrophyGame(string Id, string Name, string Detail, string? Service = null);

static class TrophyHttp
{
    public static readonly HttpClient Client = Create();

    static HttpClient Create()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Basilisco", AppInfo.Version));
        return client;
    }

    public static async Task<JsonDocument> GetJsonAsync(HttpRequestMessage request, string service, CancellationToken cancellation)
    {
        HttpResponseMessage response;
        try
        {
            response = await Client.SendAsync(request, cancellation);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
        {
            throw new TrophyServiceException($"No se ha podido conectar con {service}. Comprueba tu conexión a internet.");
        }
        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new TrophyServiceException($"{service} ha rechazado la cuenta o la clave. Revisa que estén bien.");
            if (!response.IsSuccessStatusCode)
                throw new TrophyServiceException($"{service} ha respondido con un error ({(int)response.StatusCode}). Prueba más tarde.");
            try
            {
                return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            }
            catch (JsonException)
            {
                throw new TrophyServiceException($"{service} ha devuelto una respuesta que no se entiende. Prueba más tarde.");
            }
        }
    }

    public static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null,
        } : null;

    public static int Int(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && (value.ValueKind == JsonValueKind.Number ? value.TryGetInt32(out var n) : int.TryParse(value.GetString(), out n)) ? n : 0;
}

/// <summary>
/// RetroAchievements (retroachievements.org): logros para juegos de consolas antiguas jugados en emuladores. Se usa su
/// Web API oficial con tu usuario y tu clave de la Web API (se consigue en retroachievements.org, en Ajustes).
/// </summary>
public static class RetroAchievements
{
    const string Api = "https://retroachievements.org/API/";
    const string Media = "https://media.retroachievements.org";
    static readonly Dictionary<int, List<TrophyGame>> GameLists = [];

    /// <summary>Consolas de RetroAchievements de cada consola de Basilisco (vacío si no tiene).</summary>
    public static int[] Consoles(string? platform) => platform switch
    {
        "nes" => [7], "snes" => [3], "n64" => [2], "gb" => [4, 6], "gba" => [5], "nds" => [18], "gc" => [16], "wii" => [19],
        "ps1" => [12], "ps2" => [21], "psp" => [41], "arcade" => [27],
        _ => [],
    };

    static string Auth(string user, string key) => $"z={Uri.EscapeDataString(user)}&y={Uri.EscapeDataString(key)}";

    /// <summary>Comprueba la cuenta: devuelve el nombre de usuario tal como lo tiene RetroAchievements.</summary>
    public static async Task<string> SignInAsync(string user, string key, CancellationToken cancellation)
    {
        using var json = await TrophyHttp.GetJsonAsync(new HttpRequestMessage(HttpMethod.Get,
            $"{Api}API_GetUserProfile.php?u={Uri.EscapeDataString(user)}&{Auth(user, key)}"), "RetroAchievements", cancellation);
        if (json.RootElement.ValueKind != JsonValueKind.Object || TrophyHttp.Text(json.RootElement, "User") is not { Length: > 0 } name)
            throw new TrophyServiceException("RetroAchievements no reconoce ese usuario. Revisa el nombre y la clave.");
        return name;
    }

    /// <summary>Juegos con logros de las consolas dadas cuyo nombre se parece al buscado (los más parecidos primero).</summary>
    public static async Task<List<TrophyGame>> SearchAsync(string name, int[] consoles, string user, string key, CancellationToken cancellation)
    {
        var all = new List<TrophyGame>();
        foreach (var console in consoles)
        {
            if (!GameLists.TryGetValue(console, out var list))
            {
                using var json = await TrophyHttp.GetJsonAsync(new HttpRequestMessage(HttpMethod.Get,
                    $"{Api}API_GetGameList.php?i={console}&f=1&{Auth(user, key)}"), "RetroAchievements", cancellation);
                list = [];
                if (json.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var game in json.RootElement.EnumerateArray())
                    {
                        if (TrophyHttp.Text(game, "ID") is not { } id || TrophyHttp.Text(game, "Title") is not { } title)
                            continue;
                        var count = TrophyHttp.Int(game, "NumAchievements");
                        list.Add(new TrophyGame(id, title, $"{TrophyHttp.Text(game, "ConsoleName")} · {count} logros"));
                    }
                }
                GameLists[console] = list;
            }
            all.AddRange(list);
        }
        return GameSearch.Find(all, game => game.Name, name, 30);
    }

    /// <summary>Los logros del juego con tu progreso. El tanto por ciento es el de los jugadores que lo tienen.</summary>
    public static async Task<(string Title, List<OwnTrophy> Trophies)> TrophiesAsync(string gameId, string user, string key, CancellationToken cancellation)
    {
        using var json = await TrophyHttp.GetJsonAsync(new HttpRequestMessage(HttpMethod.Get,
            $"{Api}API_GetGameInfoAndUserProgress.php?g={Uri.EscapeDataString(gameId)}&u={Uri.EscapeDataString(user)}&{Auth(user, key)}"),
            "RetroAchievements", cancellation);
        var root = json.RootElement;
        var players = Math.Max(1, TrophyHttp.Int(root, "NumDistinctPlayers"));
        var trophies = new List<OwnTrophy>();
        if (root.TryGetProperty("Achievements", out var achievements) && achievements.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in achievements.EnumerateObject().OrderBy(entry => TrophyHttp.Int(entry.Value, "DisplayOrder")))
            {
                var a = entry.Value;
                var earned = TrophyHttp.Text(a, "DateEarnedHardcore") ?? TrophyHttp.Text(a, "DateEarned");
                var percent = 100.0 * TrophyHttp.Int(a, "NumAwarded") / players;
                var badge = TrophyHttp.Text(a, "BadgeName");
                trophies.Add(new OwnTrophy
                {
                    Id = "ra-" + (TrophyHttp.Text(a, "ID") ?? entry.Name),
                    Title = TrophyHttp.Text(a, "Title") ?? "",
                    Description = TrophyHttp.Text(a, "Description") ?? "",
                    Percent = percent,
                    Tier = AchievementInfo.TierFor(percent),
                    Unlocked = earned is not null,
                    UnlockedAt = DateTime.TryParse(earned, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when) ? when.ToLocalTime() : null,
                    Icon = badge is null ? null : $"{Media}/Badge/{badge}.png",
                    IconLocked = badge is null ? null : $"{Media}/Badge/{badge}_lock.png",
                });
            }
        }
        return (TrophyHttp.Text(root, "Title") ?? "", trophies);
    }
}

/// <summary>
/// Trofeos de RPCS3 (emulador de PS3). RPCS3 no tiene API: guarda los trofeos de cada juego en
/// dev_hdd0/home/00000001/trophy/&lt;juego&gt;: TROPCONF.SFM (la lista, con su tipo bronce, plata, oro o platino),
/// TROPUSR.DAT (cuáles tienes) y los iconos TROPxxx.PNG. Se leen de ahí.
/// </summary>
public static class Rpcs3Trophies
{
    /// <summary>Carpeta de trofeos a partir de la de RPCS3 (o de la de trofeos directamente), o null si no la hay.</summary>
    public static string? TrophyFolder(string? folder)
    {
        if (folder is null || !Directory.Exists(folder))
            return null;
        // Ya es la carpeta de trofeos (cada juego en una carpeta con su TROPCONF.SFM).
        if (Directory.EnumerateDirectories(folder).Any(dir => File.Exists(Path.Combine(dir, "TROPCONF.SFM"))))
            return folder;
        var home = Path.Combine(folder, "dev_hdd0", "home");
        if (!Directory.Exists(home))
            return null;
        return Directory.EnumerateDirectories(home).Select(user => Path.Combine(user, "trophy")).FirstOrDefault(Directory.Exists);
    }

    /// <summary>Los juegos que tienen trofeos en RPCS3: la carpeta de cada uno y su nombre.</summary>
    public static List<TrophyGame> Games(string trophyFolder)
    {
        var games = new List<TrophyGame>();
        foreach (var dir in Directory.EnumerateDirectories(trophyFolder))
        {
            if (Read(dir) is not { } set)
                continue;
            var unlocked = set.Trophies.Count(t => t.Unlocked);
            games.Add(new TrophyGame(dir, set.Title, $"{Path.GetFileName(dir)} · {unlocked} de {set.Trophies.Count} trofeos"));
        }
        return games;
    }

    /// <summary>Lee los trofeos de un juego (su carpeta). Null si no se entiende.</summary>
    public static (string Title, List<OwnTrophy> Trophies)? Read(string folder)
    {
        var conf = Path.Combine(folder, "TROPCONF.SFM");
        if (!File.Exists(conf))
            return null;
        var xml = new XmlDocument();
        try
        {
            var text = File.ReadAllText(conf);
            var start = text.IndexOf('<');
            xml.LoadXml(start > 0 ? text[start..] : text);
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var states = ReadStates(Path.Combine(folder, "TROPUSR.DAT"));
        var trophies = new List<OwnTrophy>();
        foreach (XmlElement trophy in xml.GetElementsByTagName("trophy"))
        {
            if (!int.TryParse(trophy.GetAttribute("id"), out var id))
                continue;
            var icon = Path.Combine(folder, $"TROP{id:000}.PNG");
            var (unlocked, when) = states.GetValueOrDefault(id);
            trophies.Add(new OwnTrophy
            {
                Id = $"rpcs3-{id}",
                Title = trophy["name"]?.InnerText.Trim() ?? "",
                Description = trophy["detail"]?.InnerText.Trim() ?? "",
                Hidden = trophy.GetAttribute("hidden") == "yes",
                Tier = trophy.GetAttribute("ttype") switch { "P" => TrophyTier.Platinum, "G" => TrophyTier.Gold, "S" => TrophyTier.Silver, _ => TrophyTier.Bronze },
                Unlocked = unlocked,
                UnlockedAt = when,
                Icon = File.Exists(icon) ? icon : null,
            });
        }
        var title = xml.GetElementsByTagName("title-name").Cast<XmlNode>().FirstOrDefault()?.InnerText.Trim() ?? Path.GetFileName(folder);
        return (title, trophies);
    }

    /// <summary>
    /// TROPUSR.DAT (big-endian): cabecera (0x30 bytes) y tablas de 32 bytes con su tipo, número de entradas y dónde
    /// empiezan. La tabla 6 tiene una entrada por trofeo: su número, si está conseguido y cuándo (en microsegundos desde
    /// el año 1, como el reloj de la PS3).
    /// </summary>
    static Dictionary<int, (bool Unlocked, DateTime? When)> ReadStates(string path)
    {
        var states = new Dictionary<int, (bool, DateTime?)>();
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return states;
        }
        if (data.Length < 0x30 || BinaryPrimitives.ReadUInt32BigEndian(data) != 0x818F54AD)
            return states;

        var tables = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8));
        for (var t = 0; t < tables && 0x30 + t * 32 + 32 <= data.Length; t++)
        {
            var header = data.AsSpan(0x30 + t * 32);
            if (BinaryPrimitives.ReadUInt32BigEndian(header) != 6)
                continue;
            var count = BinaryPrimitives.ReadUInt32BigEndian(header[12..]);
            var offset = (long)BinaryPrimitives.ReadUInt64BigEndian(header[16..]);
            for (var i = 0; i < count && offset + 48 <= data.Length; i++)
            {
                var entry = data.AsSpan((int)offset);
                var size = BinaryPrimitives.ReadUInt32BigEndian(entry[4..]);
                var id = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[16..]);
                var unlocked = BinaryPrimitives.ReadUInt32BigEndian(entry[20..]) != 0;
                var stamp = BinaryPrimitives.ReadUInt64BigEndian(entry[32..]);
                DateTime? when = null;
                if (unlocked && stamp > 0)
                {
                    var date = new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks((long)Math.Min(stamp, (ulong)(DateTime.MaxValue.Ticks / 10)) * 10);
                    if (date.Year is >= 2006 and <= 2100)
                        when = date.ToLocalTime();
                }
                states[id] = (unlocked, when);
                offset += 16 + Math.Max(size, 32);
            }
        }
        return states;
    }
}

/// <summary>
/// PlayStation Network: tus trofeos de PS3, PS4, PS5 y Vita (los mismos que enseña PSN Profiles, que los saca de aquí).
/// Se entra con el código «npsso» de tu sesión de PlayStation (como el token de Steam): solo vive en memoria mientras
/// Basilisco está abierto y no se guarda en ningún sitio.
/// </summary>
public static class PlayStationTrophies
{
    /// <summary>Página que, con la sesión de PlayStation iniciada en el navegador, enseña el código («npsso»).</summary>
    public const string NpssoUrl = "https://ca.account.sony.com/api/v1/ssocookie";

    const string Auth = "https://ca.account.sony.com/api/authz/v3/oauth";
    const string Trophies = "https://m.np.playstation.com/api/trophy/v1";
    // La aplicación oficial de PlayStation (es la que usa también PSN Profiles para leer tus trofeos).
    const string ClientId = "09515159-7237-4370-9b40-3806e67c0891";
    const string ClientBasic = "MDk1MTUxNTktNzIzNy00MzcwLTliNDAtMzgwNmU2N2MwODkxOnVjUGprYTV0bnRCMktxc1A=";
    const string Redirect = "com.scee.psxandroid.scecompcall://redirect";

    static string? _accessToken;
    static DateTime _expires;
    static List<TrophyGame>? _titles;

    public static bool IsConnected => _accessToken is not null && DateTime.Now < _expires;

    /// <summary>Acepta el código tal cual o la página entera que enseña Sony ({"npsso":"…"}).</summary>
    public static string? ParseNpsso(string pasted)
    {
        var text = pasted.Trim();
        if (Regex.Match(text, "\"npsso\"\\s*:\\s*\"([^\"]+)\"") is { Success: true } match)
            text = match.Groups[1].Value;
        return text.Length >= 40 && !text.Contains(' ') ? text : null;
    }

    public static async Task ConnectAsync(string npsso, CancellationToken cancellation)
    {
        // 1. Con la sesión (npsso), Sony da un código de un solo uso…
        using var authorize = new HttpRequestMessage(HttpMethod.Get,
            $"{Auth}/authorize?access_type=offline&client_id={ClientId}&redirect_uri={Uri.EscapeDataString(Redirect)}" +
            "&response_type=code&scope=psn:mobile.v2.core%20psn:clientapp");
        authorize.Headers.Add("Cookie", $"npsso={npsso}");
        string? code;
        try
        {
            using var response = await TrophyHttp.Client.SendAsync(authorize, cancellation);
            var location = response.Headers.Location?.ToString() ?? "";
            code = Regex.Match(location, "[?&]code=([^&]+)") is { Success: true } found ? found.Groups[1].Value : null;
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
        {
            throw new TrophyServiceException("No se ha podido conectar con PlayStation. Comprueba tu conexión a internet.");
        }
        if (code is null)
            throw new TrophyServiceException("PlayStation no ha aceptado el código. Consigue uno nuevo (caduca al cerrar la sesión).");

        // 2. …que se cambia por el permiso para leer tus trofeos.
        using var token = new HttpRequestMessage(HttpMethod.Post, $"{Auth}/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code, ["redirect_uri"] = Redirect, ["grant_type"] = "authorization_code", ["token_format"] = "jwt",
            }),
        };
        token.Headers.Authorization = new AuthenticationHeaderValue("Basic", ClientBasic);
        using var json = await TrophyHttp.GetJsonAsync(token, "PlayStation", cancellation);
        _accessToken = TrophyHttp.Text(json.RootElement, "access_token") ?? throw new TrophyServiceException("PlayStation no ha dado el permiso. Prueba otra vez.");
        _expires = DateTime.Now.AddSeconds(Math.Max(60, TrophyHttp.Int(json.RootElement, "expires_in") - 60));
        _titles = null;
    }

    static HttpRequestMessage Get(string url)
    {
        if (!IsConnected)
            throw new TrophyServiceException("Conéctate a PlayStation con tu código de sesión.");
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        // En español si el juego tiene sus trofeos traducidos.
        request.Headers.AcceptLanguage.ParseAdd("es-ES");
        return request;
    }

    /// <summary>Tus juegos con trofeos cuyo nombre se parece al buscado.</summary>
    public static async Task<List<TrophyGame>> SearchAsync(string name, CancellationToken cancellation)
    {
        if (_titles is null)
        {
            using var json = await TrophyHttp.GetJsonAsync(Get($"{Trophies}/users/me/trophyTitles?limit=800"), "PlayStation", cancellation);
            _titles = [];
            if (json.RootElement.TryGetProperty("trophyTitles", out var titles))
            {
                foreach (var title in titles.EnumerateArray())
                {
                    if (TrophyHttp.Text(title, "npCommunicationId") is not { } id)
                        continue;
                    var progress = TrophyHttp.Int(title, "progress");
                    _titles.Add(new TrophyGame(id, TrophyHttp.Text(title, "trophyTitleName") ?? id,
                        $"{TrophyHttp.Text(title, "trophyTitlePlatform")} · {progress} %", TrophyHttp.Text(title, "npServiceName") ?? "trophy"));
                }
            }
        }
        return GameSearch.Find(_titles, game => game.Name, name, 30);
    }

    public static async Task<List<OwnTrophy>> TrophiesAsync(string id, string service, CancellationToken cancellation)
    {
        var query = $"npServiceName={service}";
        using var all = await TrophyHttp.GetJsonAsync(Get($"{Trophies}/npCommunicationIds/{id}/trophyGroups/all/trophies?{query}"), "PlayStation", cancellation);
        using var mine = await TrophyHttp.GetJsonAsync(Get($"{Trophies}/users/me/npCommunicationIds/{id}/trophyGroups/all/trophies?{query}"), "PlayStation", cancellation);

        var earned = new Dictionary<int, (bool Earned, DateTime? When, double? Rate)>();
        if (mine.RootElement.TryGetProperty("trophies", out var progress))
        {
            foreach (var trophy in progress.EnumerateArray())
            {
                var when = DateTime.TryParse(TrophyHttp.Text(trophy, "earnedDateTime"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
                    ? date.ToLocalTime() : (DateTime?)null;
                var rate = double.TryParse(TrophyHttp.Text(trophy, "trophyEarnedRate"), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : (double?)null;
                earned[TrophyHttp.Int(trophy, "trophyId")] = (trophy.TryGetProperty("earned", out var e) && e.ValueKind == JsonValueKind.True, when, rate);
            }
        }

        var list = new List<OwnTrophy>();
        if (all.RootElement.TryGetProperty("trophies", out var trophies))
        {
            foreach (var trophy in trophies.EnumerateArray())
            {
                var trophyId = TrophyHttp.Int(trophy, "trophyId");
                var (got, when, rate) = earned.GetValueOrDefault(trophyId);
                list.Add(new OwnTrophy
                {
                    Id = $"psn-{trophyId}",
                    Title = TrophyHttp.Text(trophy, "trophyName") ?? "",
                    Description = TrophyHttp.Text(trophy, "trophyDetail") ?? "",
                    Hidden = trophy.TryGetProperty("trophyHidden", out var hidden) && hidden.ValueKind == JsonValueKind.True,
                    Tier = TrophyHttp.Text(trophy, "trophyType") switch
                    {
                        "platinum" => TrophyTier.Platinum, "gold" => TrophyTier.Gold, "silver" => TrophyTier.Silver, _ => TrophyTier.Bronze,
                    },
                    Unlocked = got,
                    UnlockedAt = when,
                    Percent = rate,
                    Icon = TrophyHttp.Text(trophy, "trophyIconUrl"),
                });
            }
        }
        return list;
    }
}

/// <summary>
/// Traducción al español de los nombres y descripciones de los trofeos que vienen en inglés, con MyMemory (servicio
/// gratuito, sin clave). Cada texto se traduce una sola vez: se guarda en basilisco-data/translations.json. Si el
/// servicio no responde o se agota su cupo del día, el texto se queda como estaba y se vuelve a intentar otro día.
/// </summary>
public static class TrophyTranslator
{
    static readonly string CachePath = Path.Combine(AppInfo.DataDirectory, "translations.json");
    static Dictionary<string, string>? _cache;
    static bool _quotaFinished;

    static Dictionary<string, string> Cache
    {
        get
        {
            if (_cache is not null)
                return _cache;
            try
            {
                _cache = File.Exists(CachePath) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(CachePath)) ?? [] : [];
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _cache = [];
            }
            return _cache;
        }
    }

    static readonly HashSet<string> English =
    [
        "the", "a", "an", "of", "and", "to", "in", "on", "with", "all", "your", "you", "for", "from", "complete", "defeat", "collect",
        "get", "find", "beat", "kill", "win", "obtain", "earn", "unlock", "reach", "without", "every", "first", "level", "chapter", "game",
    ];

    static readonly HashSet<string> Spanish =
    [
        "el", "la", "los", "las", "de", "del", "y", "en", "con", "un", "una", "tu", "todos", "todas", "consigue", "derrota", "completa",
        "sin", "para", "por", "al", "que", "nivel", "capítulo",
    ];

    /// <summary>¿Parece inglés? Con palabras típicas de cada idioma; si no hay ninguna, inglés salvo que lleve letras del español.</summary>
    public static bool LooksEnglish(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Any(c => "áéíóúñ¿¡ÁÉÍÓÚÑ".Contains(c)))
            return false;
        var words = Regex.Split(text.ToLowerInvariant(), "[^a-záéíóúñü]+").Where(word => word.Length > 0).ToList();
        int english = words.Count(English.Contains), spanish = words.Count(Spanish.Contains);
        if (english == 0 && spanish == 0)
            return words.Any(word => word.Length > 2);
        return english > spanish;
    }

    /// <summary>La traducción ya guardada, o null.</summary>
    public static string? Cached(string text) => Cache.GetValueOrDefault(text);

    /// <summary>Traduce los textos que lo necesiten (de cuatro en cuatro). Devuelve el texto traducido de cada uno.</summary>
    public static async Task<Dictionary<string, string>> TranslateAsync(IEnumerable<string> texts, CancellationToken cancellation)
    {
        var result = new Dictionary<string, string>();
        var pending = new List<string>();
        foreach (var text in texts.Distinct())
        {
            if (Cache.TryGetValue(text, out var known))
                result[text] = known;
            else if (LooksEnglish(text) && text.Length <= 480)
                pending.Add(text);
        }
        if (pending.Count == 0 || _quotaFinished)
            return result;

        using var gate = new SemaphoreSlim(4);
        var tasks = pending.Select(async text =>
        {
            await gate.WaitAsync(cancellation);
            try
            {
                if (await TranslateOneAsync(text, cancellation) is { } translated)
                    lock (result)
                        result[text] = Cache[text] = translated;
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks);
        Save();
        return result;
    }

    static async Task<string?> TranslateOneAsync(string text, CancellationToken cancellation)
    {
        if (_quotaFinished)
            return null;
        try
        {
            var url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair=en%7Ces";
            using var response = await TrophyHttp.Client.GetAsync(url, cancellation);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            var root = json.RootElement;
            if (root.TryGetProperty("quotaFinished", out var quota) && quota.ValueKind == JsonValueKind.True)
            {
                _quotaFinished = true;
                return null;
            }
            if (TrophyHttp.Int(root, "responseStatus") != 200 || !root.TryGetProperty("responseData", out var data))
                return null;
            var translated = TrophyHttp.Text(data, "translatedText");
            return string.IsNullOrWhiteSpace(translated) || translated.Contains("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase)
                ? null
                : WebUtility.HtmlDecode(translated);
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
            File.WriteAllText(temp, JsonSerializer.Serialize(Cache, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), Encoding.UTF8);
            File.Move(temp, CachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
