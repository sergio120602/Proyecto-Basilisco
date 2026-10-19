using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Basilisco.Services;

/// <summary>
/// Manuales escaneados de The Manual Project, en Vimm's Lair (vimm.net/manual): miles de manuales originales en PDF de
/// consolas de PlayStation, Nintendo, Sega y Xbox. Basilisco busca en su catálogo y enseña la portada de cada manual,
/// pero la descarga del PDF solo la acepta un navegador de verdad (la web rechaza a los programas para frenar a los
/// robots), así que se abre la página del manual en el navegador, el usuario pulsa «Download pdf» y Basilisco recoge
/// el PDF de la carpeta de Descargas en cuanto termina.
/// </summary>
public sealed partial class VimmManualsProvider : ArtProvider
{
    const string NoKey = "-";
    const string Site = "https://vimm.net/";

    /// <summary>Un manual de la lista de resultados.</summary>
    sealed record Manual(string Id, string System, string Title, string Region, string? Dpi);

    readonly Dictionary<string, List<Manual>> _found = [];

    /// <summary>Consola del juego (de Basilisco), para poner primero los manuales de esa consola.</summary>
    public string? PlatformHint { get; set; }

    public override string Name => "Vimm's Lair";
    public override string ImageKind => "manual";
    public override bool RequiresKey => false;
    public override string KeyHelp => "";
    public override string KeyUrl => Site + "manual";
    public override string? GetKey(AppSettings settings) => NoKey;
    public override void SetKey(AppSettings settings, string? key) { }

    public override string DownloadingText =>
        "Se ha abierto el manual en tu navegador: pulsa «Download pdf» en esa página. Basilisco lo recogerá de tu carpeta de Descargas en cuanto termine.";

    public override async Task<List<ArtGame>> SearchAsync(string term, string key, CancellationToken cancellation)
    {
        _found.Clear();
        foreach (var attempt in Attempts(term))
        {
            var html = await GetAsync($"manual/?p=list&q={Uri.EscapeDataString(attempt)}", cancellation);
            foreach (Match row in Row().Matches(html))
            {
                var manual = new Manual(row.Groups["id"].Value, WebUtility.HtmlDecode(row.Groups["system"].Value).Trim(),
                    WebUtility.HtmlDecode(row.Groups["title"].Value).Trim(),
                    string.Join(", ", Flag().Matches(row.Groups["region"].Value).Select(flag => RegionName(flag.Groups[1].Value))),
                    row.Groups["dpi"].Value.Trim() is { Length: > 0 } dpi ? dpi : null);
                var group = $"{manual.System}|{manual.Title}";
                if (!_found.TryGetValue(group, out var list))
                    _found[group] = list = [];
                if (list.All(other => other.Id != manual.Id))
                    list.Add(manual);
            }
            if (_found.Count > 0)
                break;
        }

        // Primero los de la consola del juego, y de ellos los que más se parecen a lo buscado.
        var wanted = SystemNames(PlatformHint);
        var words = Words(term);
        return _found
            .OrderByDescending(entry => wanted.Contains(Squash(entry.Value[0].System)))
            .ThenByDescending(entry => Words(entry.Value[0].Title).Count(words.Contains) - Words(entry.Value[0].Title).Count(word => !words.Contains(word)) * 0.1)
            .ThenBy(entry => entry.Value[0].Title)
            .Select(entry => new ArtGame(entry.Key, entry.Value[0].Title,
                $"{entry.Value[0].System} · {(entry.Value.Count == 1 ? "1 manual" : $"{entry.Value.Count} manuales")}"))
            .ToList();
    }

    public override Task<List<ArtImage>> GetImagesAsync(ArtGame game, string key, CancellationToken cancellation) =>
        Task.FromResult(_found.GetValueOrDefault(game.Id, [])
            .Select(manual => new ArtImage($"{Site}manual/{manual.Id}",
                $"https://dl.vimm.net/image.php?type=manual&id={manual.Id}&page=0&destDPI=50", 0, 0,
                string.Join(" · ", new[] { manual.Region, manual.Dpi is { } dpi ? $"{dpi} ppp" : null }.Where(text => !string.IsNullOrEmpty(text))),
                Tag: manual))
            .ToList());

    /// <summary>Abre el manual en el navegador y espera a que su PDF aparezca en la carpeta de Descargas.</summary>
    public override async Task<string> DownloadImageAsync(ArtImage image, CancellationToken cancellation)
    {
        var downloads = DownloadsFolder();
        var before = Directory.Exists(downloads)
            ? Directory.EnumerateFiles(downloads, "*.pdf").ToDictionary(path => path, File.GetLastWriteTimeUtc, StringComparer.OrdinalIgnoreCase)
            : [];
        try
        {
            Process.Start(new ProcessStartInfo(image.Url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new ArtProviderException($"No se ha podido abrir el navegador: {ex.Message}");
        }

        // Un PDF nuevo (o que ha cambiado) que ya no crece: la descarga ha terminado.
        var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var deadline = DateTime.Now.AddMinutes(15);
        while (DateTime.Now < deadline)
        {
            await Task.Delay(1000, cancellation);
            if (!Directory.Exists(downloads))
                continue;
            foreach (var path in Directory.EnumerateFiles(downloads, "*.pdf"))
            {
                var written = File.GetLastWriteTimeUtc(path);
                if (before.TryGetValue(path, out var old) && old >= written)
                    continue;
                var size = new FileInfo(path).Length;
                if (size > 0 && sizes.TryGetValue(path, out var last) && last == size && IsReadable(path))
                    return path;
                sizes[path] = size;
            }
        }
        throw new ArtProviderException("No ha llegado ningún PDF nuevo a la carpeta de Descargas. Si lo has guardado en otra carpeta, usa «Elegir PDF…».");
    }

    static bool IsReadable(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    static IEnumerable<string> Attempts(string term)
    {
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var text = term.Trim();
        var withoutThe = Regex.Replace(text, @"^the\s+", "", RegexOptions.IgnoreCase);
        // Vimm pone el artículo al final («Legend of Zelda, The: The Wind Waker»): se prueba también lo que va tras los dos puntos.
        var parts = withoutThe.Split(':', '-', '–');
        var subtitle = parts.Length > 1 ? Regex.Replace(parts[^1].Trim(), @"^the\s+", "", RegexOptions.IgnoreCase) : "";
        foreach (var candidate in new[] { text, withoutThe, parts[0].Trim(), subtitle })
        {
            if (candidate.Length > 1 && tried.Add(candidate))
                yield return candidate;
        }
    }

    /// <summary>Cómo puede llamar Vimm a cada consola de Basilisco (sin espacios y en minúsculas).</summary>
    static string[] SystemNames(string? platform) => platform switch
    {
        "ps1" => ["ps1", "playstation"],
        "ps2" => ["ps2", "playstation2"],
        "ps3" => ["ps3", "playstation3"],
        "psp" => ["psp"],
        "xbox" => ["xbox"],
        "x360" => ["xbox360", "x360"],
        "n64" => ["n64", "nintendo64"],
        "gc" => ["gamecube", "gc"],
        "wii" => ["wii"],
        "wiiu" => ["wiiu"],
        "gb" => ["gb", "gameboy"],
        "gba" => ["gba", "gameboyadvance"],
        "nds" => ["ds", "nds", "nintendods"],
        _ => [],
    };

    /// <summary>Las palabras de un título, sin artículos («The», «A»…).</summary>
    static HashSet<string> Words(string text) =>
        LaunchBoxDatabase.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => word is not ("the" or "a" or "of")).ToHashSet();

    static string Squash(string text) => Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9]", "");

    static string RegionName(string region) => region switch
    {
        "USA" => "EE. UU.",
        "Europe" => "Europa",
        "Japan" => "Japón",
        "Spain" => "España",
        "Australia" => "Australia",
        "Germany" => "Alemania",
        "France" => "Francia",
        "Italy" => "Italia",
        "UK" or "United Kingdom" => "Reino Unido",
        _ => region,
    };

    static async Task<string> GetAsync(string path, CancellationToken cancellation)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Site + path);
            request.Headers.Accept.ParseAdd("text/html");
            using var response = await Http.SendAsync(request, cancellation);
            // Sin resultados, la búsqueda responde «no encontrado».
            if (response.StatusCode == HttpStatusCode.NotFound)
                return "";
            if (!response.IsSuccessStatusCode)
                throw new ArtProviderException($"Vimm's Lair ha respondido con un error ({(int)response.StatusCode}). Prueba más tarde.");
            return await response.Content.ReadAsStringAsync(cancellation);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
        {
            throw new ArtProviderException("No se ha podido conectar con Vimm's Lair. Comprueba tu conexión a internet.");
        }
    }

    /// <summary>La carpeta de Descargas del usuario (la que use de verdad, aunque la haya movido).</summary>
    static string DownloadsFolder()
    {
        if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out var pointer) == 0)
        {
            try
            {
                if (Marshal.PtrToStringUni(pointer) is { Length: > 0 } path)
                    return path;
            }
            finally
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    [DllImport("shell32.dll")]
    static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

    // Cada fila de resultados: consola, título (con el enlace al manual), banderas de las regiones… y la resolución.
    [GeneratedRegex(@"<tr>\s*<td[^>]*>(?<system>[^<]+)</td>\s*<td[^>]*><a href=""/manual/(?<id>\d+)"">(?<title>[^<]+)</a></td>\s*<td[^>]*>(?<region>.*?)</td>.*?<td[^>]*>(?<dpi>\d*)</td>\s*</tr>", RegexOptions.Singleline)]
    private static partial Regex Row();

    [GeneratedRegex(@"title=""([^""]+)""")]
    private static partial Regex Flag();
}
