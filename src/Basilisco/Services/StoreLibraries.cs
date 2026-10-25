using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;

namespace Basilisco.Services;

/// <summary>Una tienda de juegos de PC compatible (además de Steam).</summary>
public sealed record StoreInfo(string Id, string Name, string Description);

/// <summary>
/// Los juegos instalados de Epic Games, GOG, EA (Origin) y Battle.net, leídos de este PC (de sus archivos y del registro
/// de Windows), sin cuentas ni internet. Los de Epic y Battle.net se abren con su lanzador (para que inicien sesión); los
/// de GOG y EA, con su ejecutable.
/// </summary>
public static class StoreLibraries
{
    public static readonly IReadOnlyList<StoreInfo> All =
    [
        new("epic", "Epic Games", "Los juegos instalados con Epic Games Launcher. Se abren con Epic, para que inicie tu sesión."),
        new("gog", "GOG", "Los juegos instalados con GOG Galaxy o con los instaladores de GOG."),
        new("ea", "EA (Origin)", "Los juegos instalados con la aplicación de EA o con Origin."),
        new("battlenet", "Battle.net", "Los juegos de Blizzard (y los de Activision) instalados con Battle.net. Se abren con Battle.net."),
    ];

    public static StoreInfo? Find(string? id) => All.FirstOrDefault(store => store.Id == id);

    public static ImportedGames Scan(StoreInfo store) => store.Id switch
    {
        "epic" => Epic(),
        "gog" => Gog(),
        "ea" => Ea(),
        _ => BattleNet(),
    };

    // ---- Epic Games ----

    static ImportedGames Epic()
    {
        var games = new List<ImportedGame>();
        var notes = new List<string>();
        var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifests))
            return new ImportedGames(games, ["no se ha encontrado Epic Games Launcher en este PC"]);
        foreach (var file in Emulators.SafeFiles(manifests, "*.item", recursive: false))
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(file));
                var root = json.RootElement;
                string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                var app = Text("AppName");
                var ns = Text("CatalogNamespace");
                var item = Text("CatalogItemId");
                var location = Text("InstallLocation");
                if (app is null || ns is null || item is null || root.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.GetBoolean())
                    continue;
                // Los contenidos descargables tienen su propio manifiesto, que apunta al juego principal.
                if (Text("MainGameAppName") is { Length: > 0 } main && main != app)
                    continue;
                if (root.TryGetProperty("AppCategories", out var categories) && categories.ValueKind == JsonValueKind.Array &&
                    !categories.EnumerateArray().Any(category => category.GetString() == "games"))
                    continue;
                var link = $"com.epicgames.launcher://apps/{Uri.EscapeDataString($"{ns}:{item}:{app}")}?action=launch&silent=true";
                games.Add(new ImportedGame(Text("DisplayName") ?? app, link, "pc", InstallDir: location, Detail: location));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
            }
        }
        if (games.Count == 0)
            notes.Add("Epic Games está instalado, pero no tiene juegos instalados");
        return new ImportedGames(games, notes);
    }

    // ---- GOG ----

    static ImportedGames Gog()
    {
        var games = new List<ImportedGame>();
        foreach (var path in new[] { @"SOFTWARE\WOW6432Node\GOG.com\Games", @"SOFTWARE\GOG.com\Games" })
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            if (key is null)
                continue;
            foreach (var id in key.GetSubKeyNames())
            {
                using var game = key.OpenSubKey(id);
                if (game is null)
                    continue;
                // Los DLC dependen del juego principal.
                if (game.GetValue("dependsOn") is string depends && depends.Length > 0)
                    continue;
                var exe = game.GetValue("exe") as string ?? game.GetValue("launchCommand") as string;
                exe = exe?.Trim('"');
                if (exe is null || !File.Exists(exe))
                    continue;
                var name = game.GetValue("gameName") as string ?? game.GetValue("startMenu") as string ?? Path.GetFileNameWithoutExtension(exe);
                var folder = game.GetValue("path") as string;
                games.Add(new ImportedGame(name, exe, "pc", InstallDir: folder, Detail: folder));
            }
        }
        return new ImportedGames(games.DistinctBy(game => game.Path, StringComparer.OrdinalIgnoreCase).ToList(),
            games.Count == 0 ? ["no se ha encontrado ningún juego de GOG instalado"] : []);
    }

    // ---- EA (Origin) ----

    /// <summary>
    /// Los juegos de EA y de Origin dejan en su carpeta __Installer/installerdata.xml, con su nombre y el ejecutable con el
    /// que se abren (a veces escrito como una clave del registro más el nombre del .exe).
    /// </summary>
    static ImportedGames Ea()
    {
        var games = new List<ImportedGame>();
        var notes = new List<string>();
        foreach (var (name, location, icon) in InstalledPrograms())
        {
            if (location is null)
                continue;
            var data = Path.Combine(location, "__Installer", "installerdata.xml");
            if (!File.Exists(data))
                continue;
            string? exe = null, title = null;
            try
            {
                var root = XDocument.Load(data).Root;
                title = root?.Descendants("gameTitle").FirstOrDefault(t => (string?)t.Attribute("locale") is "es_ES")?.Value
                        ?? root?.Descendants("gameTitle").FirstOrDefault(t => (string?)t.Attribute("locale") is "en_US")?.Value
                        ?? root?.Descendants("gameTitle").FirstOrDefault()?.Value;
                foreach (var launcher in root?.Descendants("launcher") ?? [])
                {
                    if ((string?)launcher.Element("trial") == "1" || launcher.Element("filePath")?.Value is not { } filePath)
                        continue;
                    if (ResolveEaPath(filePath, location) is { } resolved && File.Exists(resolved))
                    {
                        exe = resolved;
                        if ((string?)launcher.Element("requires64BitOS") == "1")
                            break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
            }
            exe ??= icon is not null && File.Exists(icon) && icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !IsHelper(icon) ? icon : MainExe(location);
            if (exe is null)
            {
                notes.Add($"no se ha encontrado el ejecutable de «{title ?? name}»");
                continue;
            }
            games.Add(new ImportedGame(string.IsNullOrWhiteSpace(title) ? name : title.Trim(), exe, "pc", InstallDir: location, Detail: location));
        }
        if (games.Count == 0 && notes.Count == 0)
            notes.Add("no se ha encontrado ningún juego de EA instalado");
        return new ImportedGames(games.DistinctBy(game => game.Path, StringComparer.OrdinalIgnoreCase).ToList(), notes);
    }

    /// <summary>«[HKEY_LOCAL_MACHINE\SOFTWARE\…\Install Dir]juego.exe» → la carpeta que dice el registro más el .exe.</summary>
    static string? ResolveEaPath(string filePath, string location)
    {
        var match = Regex.Match(filePath, @"^\[(HKEY_[A-Z_]+)\\(.+)\\([^\\\]]+)\](.*)$");
        if (!match.Success)
            return Path.IsPathRooted(filePath) ? filePath : Path.Combine(location, filePath);
        var hive = match.Groups[1].Value == "HKEY_CURRENT_USER" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(match.Groups[2].Value);
            if (key?.GetValue(match.Groups[3].Value) is string folder && folder.Length > 0)
                return Path.Combine(folder, match.Groups[4].Value.TrimStart('\\'));
        }
        return Path.Combine(location, match.Groups[4].Value.TrimStart('\\'));
    }

    // ---- Battle.net ----

    /// <summary>El código de cada juego para «Battle.net.exe --exec="launch código"», por el uid de su desinstalador.</summary>
    static readonly (string Uid, string Code)[] BlizzardCodes =
    [
        ("prometheus", "Pro"), ("wow_classic_era", "WoWC"), ("wow_classic", "WoWC"), ("wow", "WoW"), ("diablo3", "D3"), ("hero", "Hero"),
        ("s2", "S2"), ("s1", "S1"), ("hs_beta", "WTCG"), ("fenris", "Fen"), ("osi", "OSI"), ("w3", "W3"), ("viper", "VIPR"), ("odin", "ODIN"),
        ("lazarus", "LAZR"), ("zeus", "ZEUS"), ("fore", "FORE"), ("auks", "AUKS"), ("spot", "SPOT"), ("wlby", "WLBY"), ("anbs", "ANBS"),
        ("rtro", "RTRO"), ("gryphon", "GRY"), ("aqua", "AQUA"), ("pinta", "PNTA"),
    ];

    static ImportedGames BattleNet()
    {
        var games = new List<ImportedGame>();
        foreach (var (name, location, icon, uninstall) in InstalledProgramsWithUninstall())
        {
            if (uninstall is null || !uninstall.Contains("Blizzard Uninstaller", StringComparison.OrdinalIgnoreCase))
                continue;
            var uid = Regex.Match(uninstall, @"--uid=([\w-]+)").Groups[1].Value.ToLowerInvariant();
            var code = BlizzardCodes.FirstOrDefault(entry => uid.StartsWith(entry.Uid, StringComparison.Ordinal)).Code;
            var exe = icon is not null && File.Exists(icon) && icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !IsHelper(icon) ? icon : null;
            if (code is null && exe is null)
                continue;
            games.Add(new ImportedGame(name, code is not null ? $"battlenet://{code}" : exe!, "pc", InstallDir: location,
                Detail: code is not null ? $"Se abre con Battle.net · {location}" : location));
        }
        return new ImportedGames(games.DistinctBy(game => game.Path, StringComparer.OrdinalIgnoreCase).ToList(),
            games.Count == 0 ? ["no se ha encontrado ningún juego de Battle.net instalado"] : []);
    }

    /// <summary>Battle.net.exe, para abrir sus juegos.</summary>
    public static string? BattleNetExe()
    {
        foreach (var (name, location, icon) in InstalledPrograms())
        {
            if (name != "Battle.net")
                continue;
            if (location is not null && File.Exists(Path.Combine(location, "Battle.net.exe")))
                return Path.Combine(location, "Battle.net.exe");
        }
        var standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Battle.net", "Battle.net.exe");
        return File.Exists(standard) ? standard : null;
    }

    // ---- Programas instalados ----

    /// <summary>Los programas instalados (del registro de Windows): su nombre, su carpeta y su icono (casi siempre su .exe).</summary>
    public static IEnumerable<(string Name, string? Location, string? Icon)> InstalledPrograms() =>
        InstalledProgramsWithUninstall().Select(program => (program.Name, program.Location, program.Icon));

    static IEnumerable<(string Name, string? Location, string? Icon, string? Uninstall)> InstalledProgramsWithUninstall()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (key is null)
                continue;
            foreach (var id in key.GetSubKeyNames())
            {
                using var program = key.OpenSubKey(id);
                if (program?.GetValue("DisplayName") is not string name || name.Length == 0 || !seen.Add($"{id}|{name}"))
                    continue;
                var location = (program.GetValue("InstallLocation") as string)?.Trim('"').TrimEnd('\\');
                var icon = (program.GetValue("DisplayIcon") as string)?.Split(',')[0].Trim('"');
                yield return (name, string.IsNullOrEmpty(location) ? null : location, string.IsNullOrEmpty(icon) ? null : icon, program.GetValue("UninstallString") as string);
            }
        }
    }

    /// <summary>Instaladores, desinstaladores e informes de errores: no son el juego.</summary>
    static bool IsHelper(string exe)
    {
        var name = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
        return name.Contains("unins") || name.Contains("setup") || name.Contains("install") || name.Contains("crash") || name.Contains("report") ||
               name.Contains("launcher") && name.Contains("ea") || name.Contains("redist") || name.Contains("vc_") || name.Contains("dxsetup") ||
               name.Contains("touchup") || name.Contains("cleanup") || name.Contains("activation");
    }

    /// <summary>El ejecutable principal de una carpeta de juego: el .exe más grande de arriba que no sea un instalador.</summary>
    static string? MainExe(string folder) =>
        Emulators.SafeFiles(folder, "*.exe", recursive: false).Where(exe => !IsHelper(exe))
            .OrderByDescending(exe => new FileInfo(exe).Length).FirstOrDefault();
}
