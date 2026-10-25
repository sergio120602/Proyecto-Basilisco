using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Basilisco.Services;

/// <summary>Un juego encontrado al importar desde un emulador o una tienda.</summary>
/// <param name="Path">El juego: la imagen del disco o la ROM (con emulador), el ejecutable o el enlace con el que se abre.</param>
/// <param name="Platform">Su consola (ver <see cref="Models.Platforms"/>), si se sabe.</param>
/// <param name="Core">El núcleo de RetroArch con el que se abre.</param>
/// <param name="InstallDir">La carpeta del juego (los de las tiendas), para saber cuándo sigue abierto.</param>
/// <param name="Detail">Lo que se enseña debajo del nombre al revisar.</param>
public sealed record ImportedGame(string Name, string Path, string? Platform, string? Core = null, string? InstallDir = null, string? Detail = null);

/// <summary>Lo que sale de buscar juegos: los juegos y avisos para el usuario.</summary>
public sealed record ImportedGames(List<ImportedGame> Games, List<string> Notes);

/// <summary>Un emulador compatible.</summary>
/// <param name="Console">Las consolas que emula, para enseñarlo.</param>
/// <param name="Platform">La consola de sus juegos (null si depende del juego, como en RetroArch o Dolphin).</param>
/// <param name="ExeNames">Los nombres de su ejecutable, para encontrarlo.</param>
/// <param name="Folders">Nombres de carpeta en los que suele instalarse.</param>
/// <param name="Extensions">Los archivos de juego que abre (vacío: los busca a su manera, por carpetas de juego).</param>
/// <param name="DefaultArguments">Cómo se le pasa el juego: {game} es el juego y {core}, el núcleo de RetroArch.</param>
public sealed record EmulatorInfo(string Id, string Name, string Console, string? Platform, string[] ExeNames, string[] Folders,
                                  string[] Extensions, string DefaultArguments);

/// <summary>
/// Los emuladores compatibles: dónde suelen estar, dónde guardan su lista de carpetas de juegos (para no tener que volver
/// a elegirlas) y cómo se les pasa un juego para abrirlo directamente, a pantalla completa cuando se puede.
/// </summary>
public static class Emulators
{
    public static readonly IReadOnlyList<EmulatorInfo> All =
    [
        new("duckstation", "DuckStation", "PlayStation", "ps1",
            ["duckstation-qt-x64-ReleaseLTCG.exe", "duckstation-qt-ARM64-ReleaseLTCG.exe", "duckstation-qt.exe", "duckstation.exe"], ["DuckStation"],
            [".cue", ".chd", ".pbp", ".m3u", ".iso", ".img", ".ecm", ".mds", ".bin"], "-batch -fullscreen -- \"{game}\""),
        new("pcsx2", "PCSX2", "PlayStation 2", "ps2",
            ["pcsx2-qt.exe", "pcsx2-qtx64-avx2.exe", "pcsx2-qtx64.exe", "pcsx2.exe"], ["PCSX2"],
            [".iso", ".chd", ".cso", ".zso", ".gz", ".cue", ".bin", ".mdf"], "-batch -fullscreen -- \"{game}\""),
        new("rpcs3", "RPCS3", "PlayStation 3", "ps3", ["rpcs3.exe"], ["RPCS3"], [], "--no-gui \"{game}\""),
        new("shadps4", "shadPS4", "PlayStation 4", "ps4", ["shadPS4.exe", "shadps4.exe"], ["shadPS4", "shadps4"], [], "-g \"{game}\""),
        new("kyty", "KytyPS5", "PlayStation 5", "ps5", ["KytyPS5.exe", "kyty_ps5.exe", "Kyty.exe", "kyty.exe"], ["KytyPS5", "Kyty"], [], "\"{game}\""),
        new("ppsspp", "PPSSPP", "PSP", "psp", ["PPSSPPWindows64.exe", "PPSSPPWindowsARM64.exe", "PPSSPPWindows.exe"], ["PPSSPP"],
            [".iso", ".cso", ".chd", ".pbp", ".elf"], "--fullscreen \"{game}\""),
        new("xemu", "Xemu", "Xbox", "xbox", ["xemu.exe"], ["xemu"], [".iso", ".xiso"], "-full-screen -dvd_path \"{game}\""),
        new("xenia", "Xenia", "Xbox 360", "x360", ["xenia_canary.exe", "xenia.exe"], ["xenia", "xenia_canary", "Xenia Canary"],
            [".iso", ".xex", ".zar"], "\"{game}\" --fullscreen=true"),
        new("retroarch", "RetroArch", "Varias consolas (tus listas de RetroArch)", null, ["retroarch.exe"], ["RetroArch", "RetroArch-Win64"], [],
            "-f -L \"{core}\" \"{game}\""),
        new("dolphin", "Dolphin", "GameCube y Wii", null, ["Dolphin.exe"], ["Dolphin", "Dolphin-x64", "Dolphin Emulator"],
            [".iso", ".gcm", ".gcz", ".rvz", ".wbfs", ".wia", ".ciso", ".wad", ".tgc"], "-b -e \"{game}\""),
        new("cemu", "Cemu", "Wii U", "wiiu", ["Cemu.exe"], ["Cemu"], [".wua", ".wud", ".wux"], "-f -g \"{game}\""),
        new("eden", "Eden", "Nintendo Switch", "switch", ["eden.exe"], ["Eden", "eden"], [".nsp", ".xci"], "-f -g \"{game}\""),
    ];

    public static EmulatorInfo? Find(string? id) => All.FirstOrDefault(emulator => emulator.Id == id);

    /// <summary>Los argumentos para abrir ese juego (sin núcleo de RetroArch, sin «-L»).</summary>
    public static string BuildArguments(string template, string game, string? core)
    {
        if (string.IsNullOrEmpty(core))
            template = Regex.Replace(template, @"-L\s+""?\{core\}""?\s*", "");
        return template.Replace("{game}", game).Replace("{core}", core ?? "");
    }

    // ---- Dónde está el emulador ----

    static readonly Dictionary<string, string?> Found = [];
    // Lo que se mira para todos los emuladores, leído una sola vez.
    static List<string>? _startMenu;
    static List<(string Name, string? Location, string? Icon)>? _programs;

    /// <summary>
    /// El ejecutable del emulador: el elegido por el usuario o, si no, el que se encuentre en los sitios de siempre
    /// (Program Files, AppData, carpetas de emuladores, el menú Inicio y los programas instalados).
    /// </summary>
    public static string? FindExe(EmulatorInfo emulator, AppSettings settings)
    {
        if (settings.EmulatorPaths.TryGetValue(emulator.Id, out var chosen) && File.Exists(chosen))
            return chosen;
        if (emulator.Id == "rpcs3" && settings.Rpcs3Folder is { } rpcs3 && File.Exists(Path.Combine(rpcs3, "rpcs3.exe")))
            return Path.Combine(rpcs3, "rpcs3.exe");
        lock (Found)
        {
            if (!Found.TryGetValue(emulator.Id, out var found) || found is not null && !File.Exists(found))
                Found[emulator.Id] = found = Search(emulator);
            return found;
        }
    }

    /// <summary>Busca todos los emuladores de antemano (al arrancar, en segundo plano), para que el menú se abra al momento.</summary>
    public static void Warm(AppSettings settings)
    {
        foreach (var emulator in All)
            FindExe(emulator, settings);
    }

    /// <summary>Busca el emulador sin que nadie lo haya elegido (rápido: solo mira los sitios de siempre).</summary>
    static string? Search(EmulatorInfo emulator)
    {
        string Folder(Environment.SpecialFolder folder) => Environment.GetFolderPath(folder);
        var profile = Folder(Environment.SpecialFolder.UserProfile);
        var containers = new List<string>
        {
            Folder(Environment.SpecialFolder.ProgramFiles), Folder(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Folder(Environment.SpecialFolder.LocalApplicationData), "Programs"), Folder(Environment.SpecialFolder.LocalApplicationData),
            Folder(Environment.SpecialFolder.ApplicationData), Folder(Environment.SpecialFolder.DesktopDirectory), Folder(Environment.SpecialFolder.MyDocuments),
            Path.Combine(profile, "Downloads"), Path.Combine(profile, "Emulators"), Path.Combine(profile, "Emuladores"),
            Path.Combine(profile, "scoop", "apps"),
        };
        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.DriveType is DriveType.Fixed && drive.IsReady))
        {
            containers.Add(drive.RootDirectory.FullName);
            foreach (var name in new[] { "Emulators", "Emuladores", "Emulación", "Emulacion", "Games", "Juegos" })
                containers.Add(Path.Combine(drive.RootDirectory.FullName, name));
        }

        foreach (var container in containers.Where(Directory.Exists))
        {
            foreach (var folder in SafeDirectories(container).Where(dir => emulator.Folders.Any(name =>
                         Path.GetFileName(dir).StartsWith(name, StringComparison.OrdinalIgnoreCase))))
            {
                if (ExeIn(emulator, folder, depth: 2) is { } exe)
                    return exe;
            }
        }

        // Accesos directos del menú Inicio.
        _startMenu ??= new[] { Folder(Environment.SpecialFolder.StartMenu), Folder(Environment.SpecialFolder.CommonStartMenu) }
            .Where(Directory.Exists).SelectMany(menu => SafeFiles(menu, "*.lnk", recursive: true)).ToList();
        {
            foreach (var link in _startMenu)
            {
                if (!emulator.Folders.Concat([emulator.Name]).Any(name => Path.GetFileName(link).Contains(name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (GameProcesses.ShortcutTarget(link) is { } target && emulator.ExeNames.Any(exe => Path.GetFileName(target).Equals(exe, StringComparison.OrdinalIgnoreCase)) &&
                    File.Exists(target))
                    return target;
            }
        }

        // Programas instalados.
        _programs ??= StoreLibraries.InstalledPrograms().ToList();
        foreach (var (name, location, icon) in _programs)
        {
            if (!name.Contains(emulator.Name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (location is not null && Directory.Exists(location) && ExeIn(emulator, location, depth: 1) is { } exe)
                return exe;
            if (icon is not null && File.Exists(icon) && emulator.ExeNames.Any(n => Path.GetFileName(icon).Equals(n, StringComparison.OrdinalIgnoreCase)))
                return icon;
        }
        return null;
    }

    static string? ExeIn(EmulatorInfo emulator, string folder, int depth)
    {
        foreach (var name in emulator.ExeNames)
        {
            var exe = Path.Combine(folder, name);
            if (File.Exists(exe))
                return exe;
        }
        if (depth <= 0)
            return null;
        foreach (var sub in SafeDirectories(folder))
        {
            if (ExeIn(emulator, sub, depth - 1) is { } exe)
                return exe;
        }
        return null;
    }

    // ---- Las carpetas de juegos que ya tiene el emulador ----

    /// <summary>Las carpetas de juegos de la configuración del emulador (las que ya le pusiste), si se encuentran.</summary>
    public static List<string> ConfiguredFolders(EmulatorInfo emulator, string? exe)
    {
        var dir = exe is null ? null : Path.GetDirectoryName(exe);
        string Documents(params string[] parts) => Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), .. parts]);
        string Roaming(params string[] parts) => Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), .. parts]);
        string Local(params string[] parts) => Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), .. parts]);
        string? Beside(params string[] parts) => dir is null ? null : Path.Combine([dir, .. parts]);

        var folders = new List<string>();
        try
        {
            switch (emulator.Id)
            {
                case "duckstation":
                    foreach (var ini in new[] { Beside("settings.ini"), Local("DuckStation", "settings.ini"), Documents("DuckStation", "settings.ini") })
                        folders.AddRange(IniValues(ini, "GameList", key => key is "RecursivePaths" or "Paths"));
                    break;
                case "pcsx2":
                    foreach (var ini in new[] { Beside("inis", "PCSX2.ini"), Documents("PCSX2", "inis", "PCSX2.ini") })
                        folders.AddRange(IniValues(ini, "GameList", key => key is "RecursivePaths" or "Paths"));
                    break;
                case "shadps4":
                    foreach (var toml in new[] { Beside("user", "config.toml"), Roaming("shadPS4", "config.toml") })
                    {
                        if (toml is null || !File.Exists(toml))
                            continue;
                        var text = File.ReadAllText(toml);
                        foreach (Match list in Regex.Matches(text, @"installDirs\s*=\s*\[(.*?)\]", RegexOptions.Singleline))
                            folders.AddRange(Regex.Matches(list.Groups[1].Value, "\"([^\"]+)\"|'([^']+)'").Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value));
                        foreach (Match single in Regex.Matches(text, @"^\s*installDir\s*=\s*[""']([^""']+)[""']", RegexOptions.Multiline))
                            folders.Add(single.Groups[1].Value);
                    }
                    break;
                case "ppsspp":
                    foreach (var ini in new[] { Beside("memstick", "PSP", "SYSTEM", "ppsspp.ini"), Documents("PPSSPP", "PSP", "SYSTEM", "ppsspp.ini") })
                    {
                        folders.AddRange(IniValues(ini, "General", key => key == "CurrentDirectory"));
                        folders.AddRange(IniValues(ini, "PinnedPaths", key => key.StartsWith("Path", StringComparison.Ordinal)));
                        folders.AddRange(IniValues(ini, "Recent", key => key.StartsWith("FileName", StringComparison.Ordinal)).Select(Path.GetDirectoryName).OfType<string>());
                    }
                    break;
                case "xemu":
                    foreach (var toml in new[] { Beside("xemu.toml"), Roaming("xemu", "xemu", "xemu.toml") })
                    {
                        if (toml is null || !File.Exists(toml))
                            continue;
                        foreach (Match m in Regex.Matches(File.ReadAllText(toml), @"^\s*(\w+)\s*=\s*[""']([^""']+)[""']", RegexOptions.Multiline))
                        {
                            if (m.Groups[1].Value is "dvd_path")
                                folders.Add(Path.GetDirectoryName(m.Groups[2].Value) ?? "");
                            else if (m.Groups[1].Value.EndsWith("_dir", StringComparison.Ordinal) && m.Groups[1].Value.Contains("game"))
                                folders.Add(m.Groups[2].Value);
                        }
                    }
                    break;
                case "dolphin":
                    foreach (var ini in new[] { Beside("User", "Config", "Dolphin.ini"), Roaming("Dolphin Emulator", "Config", "Dolphin.ini"), Documents("Dolphin Emulator", "Config", "Dolphin.ini") })
                        folders.AddRange(IniValues(ini, "General", key => Regex.IsMatch(key, @"^ISOPath\d+$")));
                    break;
                case "cemu":
                    foreach (var xml in new[] { Beside("settings.xml"), Beside("portable", "settings.xml"), Roaming("Cemu", "settings.xml") })
                    {
                        if (xml is null || !File.Exists(xml))
                            continue;
                        var root = XDocument.Load(xml).Root;
                        folders.AddRange(root?.Descendants("GamePaths").Elements("Entry").Select(e => e.Value) ?? []);
                    }
                    break;
                case "eden":
                    foreach (var ini in new[] { Beside("user", "config", "qt-config.ini"), Roaming("eden", "config", "qt-config.ini") })
                        folders.AddRange(IniValues(ini, "UI", key => Regex.IsMatch(key, @"^Paths\\gamedirs\\\d+\\path$")));
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or ArgumentException)
        {
        }
        return folders.Select(folder => folder.Trim().Replace('/', '\\').TrimEnd('\\'))
            .Where(folder => folder.Length > 2 && !folder.StartsWith("SDMC", StringComparison.OrdinalIgnoreCase) && Directory.Exists(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Los valores de las claves de una sección de un .ini (las repetidas también, como en DuckStation).</summary>
    static IEnumerable<string> IniValues(string? path, string section, Func<string, bool> key)
    {
        if (path is null || !File.Exists(path))
            yield break;
        var inSection = false;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inSection = line.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            var equals = line.IndexOf('=');
            if (!inSection || equals <= 0 || !key(line[..equals].Trim()))
                continue;
            var value = line[(equals + 1)..].Trim().Trim('"');
            if (value.StartsWith("@ByteArray(", StringComparison.Ordinal))
                value = value[11..].TrimEnd(')');
            if (value.Length > 0)
                yield return value;
        }
    }

    // ---- Buscar los juegos ----

    /// <summary>Busca los juegos del emulador en sus carpetas (y en su biblioteca propia, en RPCS3, RetroArch y Cemu).</summary>
    public static ImportedGames Scan(EmulatorInfo emulator, string? exe, IReadOnlyList<string> folders, CancellationToken cancellation)
    {
        var games = new List<ImportedGame>();
        var notes = new List<string>();
        var dir = exe is null ? null : Path.GetDirectoryName(exe);
        switch (emulator.Id)
        {
            case "rpcs3":
                if (dir is not null)
                    games.AddRange(Rpcs3Library(dir));
                foreach (var folder in folders)
                    games.AddRange(SfoGames(folder, "ps3", cancellation));
                break;
            case "shadps4":
                foreach (var folder in folders)
                    games.AddRange(SfoGames(folder, "ps4", cancellation));
                break;
            case "kyty":
                foreach (var folder in folders)
                    games.AddRange(Ps5Games(folder, cancellation));
                break;
            case "retroarch":
                if (dir is null)
                    notes.Add("elige primero dónde está RetroArch: sus juegos salen de tus listas (playlists)");
                else
                    games.AddRange(RetroArchPlaylists(dir, notes));
                break;
            case "cemu":
                if (dir is not null)
                    games.AddRange(CemuTitles(Path.Combine(dir, "mlc01")));
                games.AddRange(CemuTitles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cemu", "mlc01")));
                foreach (var folder in folders)
                {
                    games.AddRange(CemuFolders(folder, cancellation));
                    games.AddRange(FileGames(emulator, folder, cancellation));
                }
                break;
            default:
                foreach (var folder in folders)
                    games.AddRange(FileGames(emulator, folder, cancellation));
                break;
        }
        if (emulator.Id != "retroarch" && folders.Count == 0 && games.Count == 0)
            notes.Add("añade la carpeta donde tienes los juegos");
        return new ImportedGames(games.DistinctBy(game => game.Path, StringComparer.OrdinalIgnoreCase).OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), notes);
    }

    /// <summary>Los archivos de juego de la carpeta (y sus subcarpetas), sin las partes sueltas de un mismo juego.</summary>
    static IEnumerable<ImportedGame> FileGames(EmulatorInfo emulator, string folder, CancellationToken cancellation)
    {
        var files = SafeFiles(folder, "*", recursive: true, cancellation)
            .Where(file => emulator.Extensions.Contains(Path.GetExtension(file).ToLowerInvariant())).ToList();
        var byFolder = files.GroupBy(file => Path.GetDirectoryName(file) ?? "", StringComparer.OrdinalIgnoreCase);
        foreach (var group in byFolder)
        {
            var here = group.ToList();
            bool Has(string ext) => here.Any(file => Path.GetExtension(file).Equals(ext, StringComparison.OrdinalIgnoreCase));
            foreach (var file in here)
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                // Un .cue con sus .bin, o varios discos en un .m3u: el juego es el que los junta.
                if (ext == ".bin" && Has(".cue") || ext == ".cue" && Has(".m3u") || ext == ".ecm" && Has(".cue"))
                    continue;
                if (emulator.Id == "eden" && !IsSwitchBaseGame(file))
                    continue;
                if (emulator.Id == "ppsspp" && ext == ".elf")
                    continue;
                var name = ext == ".pbp" && PbpTitle(file) is { } title ? title : NameOf(file);
                var platform = emulator.Id == "dolphin" ? DolphinPlatform(file) : emulator.Platform;
                yield return new ImportedGame(name, file, platform, Detail: file);
            }
        }
    }

    /// <summary>En Switch, las actualizaciones y los DLC también son .nsp: se quedan solo los juegos (su id acaba en 000).</summary>
    static bool IsSwitchBaseGame(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        if (Regex.IsMatch(name, @"\b(UPD|Update|DLC|Patch)\b", RegexOptions.IgnoreCase))
            return false;
        var id = Regex.Match(name, @"\[(0100[0-9A-Fa-f]{12})\]");
        return !id.Success || id.Groups[1].Value.EndsWith("000", StringComparison.Ordinal);
    }

    /// <summary>
    /// El nombre del juego a partir de su archivo: sin la extensión ni lo que va entre paréntesis o corchetes (la región,
    /// el número de serie, la versión). Si el archivo se llama de forma genérica (EBOOT, default), el de su carpeta.
    /// </summary>
    public static string NameOf(string file)
    {
        var raw = Path.GetFileNameWithoutExtension(file);
        if (raw.ToLowerInvariant() is "eboot" or "default" or "main" or "game" or "disc" || Regex.IsMatch(raw, @"^disc ?\d$", RegexOptions.IgnoreCase))
            raw = Path.GetFileName(Path.GetDirectoryName(file)) ?? raw;
        var name = Regex.Replace(raw, @"\([^)]*\)|\[[^\]]*\]|\{[^}]*\}", " ");
        name = Regex.Replace(name.Replace('_', ' '), @"\s+", " ").Trim(' ', '-', '.');
        return name.Length > 0 ? name : raw;
    }

    // ---- PlayStation: PARAM.SFO ----

    /// <summary>Los datos de un PARAM.SFO de PS3/PS4/PSP (TITLE, TITLE_ID, CATEGORY…).</summary>
    public static Dictionary<string, string> ReadSfo(byte[] data)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (data.Length < 20 || data[0] != 0 || data[1] != (byte)'P' || data[2] != (byte)'S' || data[3] != (byte)'F')
            return values;
        var keys = BitConverter.ToInt32(data, 8);
        var table = BitConverter.ToInt32(data, 12);
        var count = BitConverter.ToInt32(data, 16);
        for (var i = 0; i < count && 20 + i * 16 + 16 <= data.Length; i++)
        {
            var entry = 20 + i * 16;
            var keyOffset = BitConverter.ToUInt16(data, entry);
            var format = BitConverter.ToUInt16(data, entry + 2);
            var length = BitConverter.ToInt32(data, entry + 4);
            var dataOffset = BitConverter.ToInt32(data, entry + 12);
            var keyStart = keys + keyOffset;
            var keyEnd = Array.IndexOf(data, (byte)0, keyStart);
            var valueStart = table + dataOffset;
            if (keyEnd < 0 || valueStart + length > data.Length)
                continue;
            var key = Encoding.ASCII.GetString(data, keyStart, keyEnd - keyStart);
            values[key] = format == 0x0404 && length >= 4
                ? BitConverter.ToInt32(data, valueStart).ToString()
                : Encoding.UTF8.GetString(data, valueStart, length).TrimEnd('\0').Trim();
        }
        return values;
    }

    static Dictionary<string, string> ReadSfo(string path)
    {
        try
        {
            return File.Exists(path) ? ReadSfo(File.ReadAllBytes(path)) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>El título que lleva dentro un EBOOT.PBP (los juegos de PSP descargados y los de PS1 convertidos).</summary>
    static string? PbpTitle(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            var header = new byte[40];
            if (stream.Read(header, 0, 40) < 40 || header[1] != (byte)'P' || header[2] != (byte)'B' || header[3] != (byte)'P')
                return null;
            var start = BitConverter.ToInt32(header, 8);
            var end = BitConverter.ToInt32(header, 12);
            if (end <= start || end - start > 1 << 20)
                return null;
            var sfo = new byte[end - start];
            stream.Position = start;
            stream.ReadExactly(sfo);
            return ReadSfo(sfo).GetValueOrDefault("TITLE") is { Length: > 0 } title ? title : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            return null;
        }
    }

    /// <summary>Los juegos de PS3 de RPCS3: los de disco que conoce (games.yml) y los instalados (dev_hdd0/game).</summary>
    static IEnumerable<ImportedGame> Rpcs3Library(string dir)
    {
        var yml = Path.Combine(dir, "config", "games.yml");
        if (!File.Exists(yml))
            yml = Path.Combine(dir, "games.yml");
        if (File.Exists(yml))
        {
            foreach (var line in File.ReadLines(yml))
            {
                var match = Regex.Match(line, @"^\s*([A-Z]{4}\d{5})\s*:\s*(.+?)\s*$");
                if (match.Success && Ps3Game(match.Groups[2].Value.Trim('"', '\'').Replace('/', '\\').TrimEnd('\\')) is { } game)
                    yield return game;
            }
        }
        var hdd = Path.Combine(dir, "dev_hdd0", "game");
        foreach (var folder in SafeDirectories(hdd))
        {
            if (Ps3Game(folder) is { } game)
                yield return game;
        }
    }

    /// <summary>Un juego de PS3 en una carpeta (de disco, con PS3_GAME, o instalado): su EBOOT.BIN con el título de su PARAM.SFO.</summary>
    static ImportedGame? Ps3Game(string folder)
    {
        foreach (var root in new[] { Path.Combine(folder, "PS3_GAME"), folder })
        {
            var eboot = Path.Combine(root, "USRDIR", "EBOOT.BIN");
            var sfo = ReadSfo(Path.Combine(root, "PARAM.SFO"));
            if (!File.Exists(eboot) || sfo.Count == 0)
                continue;
            // Solo los juegos (de disco o instalados), no las actualizaciones («GD») ni otros datos.
            if (sfo.GetValueOrDefault("CATEGORY") is { Length: > 0 } category && category is not ("DG" or "HG"))
                return null;
            var title = sfo.GetValueOrDefault("TITLE") is { Length: > 0 } t ? t : NameOf(folder);
            return new ImportedGame(Clean(title), eboot, "ps3", Detail: $"{sfo.GetValueOrDefault("TITLE_ID")} · {folder}");
        }
        return null;
    }

    /// <summary>Juegos de PS3 o PS4 en carpetas (hasta tres niveles): las que tienen PARAM.SFO y EBOOT.</summary>
    static IEnumerable<ImportedGame> SfoGames(string folder, string platform, CancellationToken cancellation)
    {
        foreach (var dir in Folders(folder, 3, cancellation))
        {
            if (platform == "ps3")
            {
                if ((Directory.Exists(Path.Combine(dir, "PS3_GAME")) || File.Exists(Path.Combine(dir, "USRDIR", "EBOOT.BIN"))) && Ps3Game(dir) is { } game)
                    yield return game;
                continue;
            }
            var eboot = Path.Combine(dir, "eboot.bin");
            var sfo = ReadSfo(Path.Combine(dir, "sce_sys", "param.sfo"));
            // Solo los juegos («gd»): las actualizaciones («gp») y los contenidos extra van aparte.
            if (!File.Exists(eboot) || sfo.Count == 0 || sfo.GetValueOrDefault("CATEGORY") is { } category && !category.StartsWith("gd", StringComparison.OrdinalIgnoreCase))
                continue;
            var title = sfo.GetValueOrDefault("TITLE") is { Length: > 0 } t ? t : NameOf(dir);
            yield return new ImportedGame(Clean(title), eboot, platform, Detail: $"{sfo.GetValueOrDefault("TITLE_ID")} · {dir}");
        }
    }

    /// <summary>Juegos de PS5 en carpetas: los que tienen eboot.bin y sce_sys/param.json (con su nombre).</summary>
    static IEnumerable<ImportedGame> Ps5Games(string folder, CancellationToken cancellation)
    {
        foreach (var dir in Folders(folder, 3, cancellation))
        {
            var eboot = Path.Combine(dir, "eboot.bin");
            var param = Path.Combine(dir, "sce_sys", "param.json");
            if (!File.Exists(eboot) || !File.Exists(param))
                continue;
            string? title = null, id = null;
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(param));
                var root = json.RootElement;
                id = root.TryGetProperty("titleId", out var titleId) ? titleId.GetString() : null;
                if (root.TryGetProperty("localizedParameters", out var localized))
                {
                    var language = localized.TryGetProperty("defaultLanguage", out var lang) ? lang.GetString() : null;
                    foreach (var candidate in new[] { language, "en-US", "en-GB", "es-ES" }.OfType<string>())
                    {
                        if (localized.TryGetProperty(candidate, out var entry) && entry.TryGetProperty("titleName", out var name))
                        {
                            title = name.GetString();
                            break;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
            }
            yield return new ImportedGame(Clean(title ?? NameOf(dir)), eboot, "ps5", Detail: $"{id} · {dir}");
        }
    }

    // ---- Wii U (Cemu) ----

    /// <summary>Los juegos instalados en la memoria de Cemu (mlc01/usr/title/00050000), sin actualizaciones ni DLC.</summary>
    static IEnumerable<ImportedGame> CemuTitles(string mlc)
    {
        foreach (var title in SafeDirectories(Path.Combine(mlc, "usr", "title", "00050000")))
        {
            if (CemuGame(title) is { } game)
                yield return game;
        }
    }

    static IEnumerable<ImportedGame> CemuFolders(string folder, CancellationToken cancellation)
    {
        foreach (var dir in Folders(folder, 3, cancellation))
        {
            if (Directory.Exists(Path.Combine(dir, "code")) && CemuGame(dir) is { } game)
                yield return game;
        }
    }

    /// <summary>Un juego de Wii U sin empaquetar: su .rpx (en code) y el nombre de meta/meta.xml.</summary>
    static ImportedGame? CemuGame(string dir)
    {
        var rpx = SafeFiles(Path.Combine(dir, "code"), "*.rpx", recursive: false).FirstOrDefault();
        if (rpx is null)
            return null;
        string? name = null;
        var meta = Path.Combine(dir, "meta", "meta.xml");
        try
        {
            if (File.Exists(meta) && XDocument.Load(meta).Root is { } root)
                name = new[] { "longname_es", "longname_en", "shortname_en" }.Select(tag => root.Element(tag)?.Value).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
        }
        return new ImportedGame(Clean(name?.Replace('\n', ' ') ?? NameOf(dir)), rpx, "wiiu", Detail: dir);
    }

    // ---- GameCube o Wii (Dolphin) ----

    /// <summary>GameCube o Wii, por el código del disco (la primera letra) o la marca que llevan las imágenes de Wii.</summary>
    static string DolphinPlatform(string file)
    {
        var ext = Path.GetExtension(file).ToLowerInvariant();
        if (ext is ".wbfs" or ".wad")
            return "wii";
        try
        {
            using var stream = File.OpenRead(file);
            var header = new byte[0x100];
            var read = stream.Read(header, 0, header.Length);
            var offset = ext switch
            {
                // RVZ y WIA guardan una copia de la cabecera del disco a partir del byte 0x58.
                ".rvz" or ".wia" => 0x58,
                _ => 0,
            };
            if (ext == ".ciso")
            {
                stream.Position = 0x8000;
                read = stream.Read(header, 0, header.Length);
            }
            if (read < offset + 0x20)
                return "gc";
            if (header[offset + 0x18] == 0x5D && header[offset + 0x19] == 0x1C && header[offset + 0x1A] == 0x9E && header[offset + 0x1B] == 0xA3)
                return "wii";
            if (header[offset + 0x1C] == 0xC2 && header[offset + 0x1D] == 0x33 && header[offset + 0x1E] == 0x9F && header[offset + 0x1F] == 0x3D)
                return "gc";
            return (char)header[offset] is 'R' or 'S' or 'H' or 'W' ? "wii" : "gc";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "gc";
        }
    }

    // ---- RetroArch ----

    /// <summary>Consola de cada lista de RetroArch (sus nombres son los de las bases de datos de libretro).</summary>
    static string? RetroArchPlatform(string playlist)
    {
        var name = Path.GetFileNameWithoutExtension(playlist);
        return name switch
        {
            "Sony - PlayStation" => "ps1",
            "Sony - PlayStation 2" => "ps2",
            "Sony - PlayStation 3" => "ps3",
            "Sony - PlayStation Portable" => "psp",
            "Nintendo - Game Boy" or "Nintendo - Game Boy Color" => "gb",
            "Nintendo - Game Boy Advance" => "gba",
            "Nintendo - Nintendo 64" or "Nintendo - Nintendo 64DD" => "n64",
            "Nintendo - Nintendo Entertainment System" or "Nintendo - Family Computer Disk System" => "nes",
            "Nintendo - Super Nintendo Entertainment System" or "Nintendo - Satellaview" or "Nintendo - Sufami Turbo" => "snes",
            "Nintendo - Nintendo DS" or "Nintendo - Nintendo DSi" => "nds",
            "Nintendo - GameCube" => "gc",
            "Nintendo - Wii" => "wii",
            "Microsoft - Xbox" => "xbox",
            _ when name.StartsWith("MAME", StringComparison.OrdinalIgnoreCase) || name.StartsWith("FBNeo", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Arcade", StringComparison.OrdinalIgnoreCase) || name.StartsWith("SNK - Neo Geo", StringComparison.OrdinalIgnoreCase) => "arcade",
            _ => null,
        };
    }

    /// <summary>Los juegos de las listas (playlists) de RetroArch, con el núcleo con el que se abren.</summary>
    static IEnumerable<ImportedGame> RetroArchPlaylists(string dir, List<string> notes)
    {
        var playlists = Path.Combine(dir, "playlists");
        var config = Path.Combine(dir, "retroarch.cfg");
        if (File.Exists(config))
        {
            foreach (var line in File.ReadLines(config))
            {
                var match = Regex.Match(line, @"^\s*playlist_directory\s*=\s*""(.*)""");
                if (!match.Success || match.Groups[1].Value is not { Length: > 0 } value || value == "default")
                    continue;
                // «:\» es la carpeta de RetroArch (en las versiones portables).
                if (value.StartsWith(":\\", StringComparison.Ordinal) || value.StartsWith(":/", StringComparison.Ordinal))
                    value = Path.Combine(dir, value[2..]);
                playlists = Path.IsPathRooted(value) ? value : Path.Combine(dir, value);
            }
        }
        var files = SafeFiles(playlists, "*.lpl", recursive: false).Where(file => !Path.GetFileName(file).StartsWith("content_", StringComparison.OrdinalIgnoreCase)).ToList();
        if (files.Count == 0)
            notes.Add("RetroArch no tiene listas de juegos: en RetroArch, «Importar contenido» crea una por consola");
        var withoutCore = 0;
        var games = new List<ImportedGame>();
        foreach (var file in files)
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(file));
                var root = json.RootElement;
                var defaultCore = root.TryGetProperty("default_core_path", out var d) ? d.GetString() : null;
                if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var item in items.EnumerateArray())
                {
                    var path = item.TryGetProperty("path", out var p) ? p.GetString() : null;
                    if (string.IsNullOrEmpty(path))
                        continue;
                    var label = item.TryGetProperty("label", out var l) && l.GetString() is { Length: > 0 } text ? text : NameOf(path.Split('#')[0]);
                    var core = item.TryGetProperty("core_path", out var c) ? c.GetString() : null;
                    if (string.IsNullOrEmpty(core) || core == "DETECT")
                        core = string.IsNullOrEmpty(defaultCore) || defaultCore == "DETECT" ? null : defaultCore;
                    if (core is not null && !File.Exists(core) && File.Exists(Path.Combine(dir, "cores", Path.GetFileName(core))))
                        core = Path.Combine(dir, "cores", Path.GetFileName(core));
                    if (core is null)
                        withoutCore++;
                    var coreName = item.TryGetProperty("core_name", out var cn) && cn.GetString() is { Length: > 0 } named && named != "DETECT" ? named : core is null ? null : Path.GetFileNameWithoutExtension(core);
                    games.Add(new ImportedGame(Clean(Regex.Replace(label, @"\s*\([^)]*\)|\s*\[[^\]]*\]", "").Trim() is { Length: > 0 } clean ? clean : label),
                        path.Replace('/', '\\'), RetroArchPlatform(file), core,
                        Detail: $"{Path.GetFileNameWithoutExtension(file)}{(coreName is null ? " · sin núcleo (RetroArch preguntará)" : $" · {coreName}")}"));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                notes.Add($"no se ha podido leer la lista «{Path.GetFileNameWithoutExtension(file)}» (las del formato antiguo no se leen)");
            }
        }
        if (withoutCore > 0)
            notes.Add(withoutCore == 1 ? "1 juego no tiene núcleo asignado en su lista" : $"{withoutCore} juegos no tienen núcleo asignado en su lista");
        return games;
    }

    // ---- Utilidades ----

    static string Clean(string title) => Regex.Replace(title.Replace("™", "").Replace("®", "").Replace('\n', ' '), @"\s+", " ").Trim();

    /// <summary>La carpeta y sus subcarpetas hasta esa profundidad.</summary>
    static IEnumerable<string> Folders(string folder, int depth, CancellationToken cancellation)
    {
        if (!Directory.Exists(folder))
            yield break;
        var pending = new Queue<(string Dir, int Depth)>([(folder, 0)]);
        while (pending.Count > 0 && !cancellation.IsCancellationRequested)
        {
            var (dir, level) = pending.Dequeue();
            yield return dir;
            if (level < depth)
            {
                foreach (var sub in SafeDirectories(dir))
                    pending.Enqueue((sub, level + 1));
            }
        }
    }

    internal static IEnumerable<string> SafeDirectories(string folder)
    {
        try
        {
            return Directory.Exists(folder)
                ? Directory.EnumerateDirectories(folder).Where(dir => !new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.System)).ToList()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }
    }

    /// <summary>Los archivos de la carpeta (y, si se pide, de las de dentro, hasta seis niveles), sin fallar en las que no se pueden leer.</summary>
    internal static IEnumerable<string> SafeFiles(string folder, string pattern, bool recursive, CancellationToken cancellation = default)
    {
        if (!Directory.Exists(folder))
            return [];
        try
        {
            return Directory.EnumerateFiles(folder, pattern, new EnumerationOptions
            {
                RecurseSubdirectories = recursive, MaxRecursionDepth = 6, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System,
            }).TakeWhile(_ => !cancellation.IsCancellationRequested).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }
    }
}
