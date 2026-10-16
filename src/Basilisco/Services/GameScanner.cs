using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Basilisco.Services;

/// <summary>Juego detectado al escanear una carpeta.</summary>
public sealed record FoundGame(string Name, string Path);

/// <summary>Progreso del escaneo: carpetas revisadas y juegos encontrados hasta ahora.</summary>
public readonly record struct ScanProgress(int Folders, int Games);

/// <summary>
/// Busca juegos dentro de una carpeta. Cada subcarpeta que contenga un juego cuenta como un juego
/// (con su ejecutable principal), los accesos directos sueltos cuentan cada uno como un juego,
/// y se descartan instaladores, desinstaladores, redistribuibles y demás herramientas.
/// </summary>
public sealed partial class GameScanner
{
    /// <summary>Profundidad máxima a la que se busca un ejecutable por debajo de la carpeta de un juego.</summary>
    const int MaxDepth = 6;

    /// <summary>Por debajo de este tamaño un .exe casi nunca es un juego (suelen ser lanzadores auxiliares).</summary>
    const long MinExeSize = 50 * 1024;

    const int ProgressEvery = 25;

    // «Juego 2» antes que «Juego 10».
    static readonly StringComparer NaturalOrder =
        StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);

    static readonly string[] ShortcutExtensions = [".lnk", ".url"];

    // Partes del nombre de un .exe que delatan que no es el juego.
    static readonly string[] ExcludedExeWords =
    [
        "unins", "uninstall", "setup", "install", "redist", "vcredist", "vc_redist", "dxsetup", "dxwebsetup",
        "directx", "dotnet", "netfx", "crash", "report", "prereq", "easyanticheat", "eac_", "battleye", "beservice",
        "updater", "helper", "cefprocess", "cefsharp", "qtwebengine", "webengine", "crashpad", "notification",
        "oalinst", "physx", "dxdiag", "config", "settings", "server", "editor", "touchup", "cleanup", "repair",
        "register", "activation", "7z", "python", "java", "vulkan-1", "msiexec",
    ];

    // Carpetas que nunca contienen el juego en sí.
    static readonly HashSet<string> IgnoredFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "_CommonRedist", "CommonRedist", "Redist", "Redists", "Redistributables", "__Installer", "Installer", "Installers",
        "Support", "DirectX", "DirectX9", "vcredist", "Prerequisites", "PrereqSetup", "EasyAntiCheat", "BattlEye",
        "ThirdParty", "Crashpad", "dotNetFx", "DotNet", "$RECYCLE.BIN", "System Volume Information", "Windows",
        "node_modules", ".git", "__pycache__", "MonoBleedingEdge", "Mono", "jre", "java", "python", "SDK",
        "Docs", "Documentation", "Manual", "Manuals", "Extras", "Soundtrack", "OST", "Artbook",
    };

    // Subcarpetas típicas de un mismo juego: si los ejecutables están en ellas, es un solo juego, no varios.
    static readonly HashSet<string> GenericFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "bin32", "bin64", "binaries", "x64", "x86", "win64", "win32", "windows", "game", "data", "app",
        "program", "system", "release", "retail", "engine", "base", "common", "launcher", "client",
    };

    readonly IProgress<ScanProgress>? _progress;
    readonly CancellationToken _cancellation;
    readonly List<FoundGame> _found = [];
    int _folders;

    GameScanner(IProgress<ScanProgress>? progress, CancellationToken cancellation)
    {
        _progress = progress;
        _cancellation = cancellation;
    }

    /// <summary>Escanea la carpeta (puede tardar; llámalo fuera del hilo de la interfaz).</summary>
    public static List<FoundGame> Scan(string root, IProgress<ScanProgress>? progress, CancellationToken cancellation)
    {
        var scanner = new GameScanner(progress, cancellation);
        scanner.ScanRoot(new DirectoryInfo(root));
        progress?.Report(new ScanProgress(scanner._folders, scanner._found.Count));
        return scanner._found
            .DistinctBy(game => game.Path, StringComparer.OrdinalIgnoreCase)
            .OrderBy(game => game.Name, NaturalOrder)
            .ToList();
    }

    void ScanRoot(DirectoryInfo root)
    {
        // Accesos directos sueltos (por ejemplo, una carpeta con los accesos de todos tus juegos): uno por juego.
        foreach (var file in Files(root).Where(IsShortcut))
            Add(PrettifyName(Path.GetFileNameWithoutExtension(file.Name)), file.FullName);

        var looseExes = Files(root).Where(IsGameExe).ToList();
        var children = GameChildren(root, depth: 0);

        if (children.Count == 0)
        {
            // La carpeta elegida es la de un solo juego.
            if (looseExes.Count > 0)
                Add(PrettifyName(root.Name), PickBest(looseExes, root).FullName);
            else if (LooksLikeSingleGame(root, depth: 0) is { } exe)
                Add(PrettifyName(root.Name), exe.FullName);
            return;
        }

        // Es una colección: los .exe sueltos son juegos sueltos y cada subcarpeta con juego, otro.
        foreach (var exe in looseExes)
            Add(PrettifyName(Path.GetFileNameWithoutExtension(exe.Name)), exe.FullName);
        foreach (var (child, _) in children)
            ScanGameFolder(child, depth: 1);
    }

    /// <summary>Una carpeta dentro de la colección: o es un juego, o es otra colección (por ejemplo «Juegos/PS1»).</summary>
    void ScanGameFolder(DirectoryInfo folder, int depth)
    {
        ReportFolder();

        var exes = Files(folder).Where(IsGameExe).ToList();
        if (exes.Count > 0)
        {
            Add(PrettifyName(folder.Name), PickBest(exes, folder).FullName);
            return;
        }

        var children = GameChildren(folder, depth);
        if (children.Count == 0)
            return;

        if (IsCollection(children))
        {
            foreach (var (child, _) in children)
                ScanGameFolder(child, depth + 1);
        }
        else
        {
            // El ejecutable está más abajo (por ejemplo «Juego/bin/x64/juego.exe»): sigue siendo un solo juego.
            Add(PrettifyName(folder.Name), PickBest(children.SelectMany(c => c.Exes).ToList(), folder).FullName);
        }
    }

    FileInfo? LooksLikeSingleGame(DirectoryInfo folder, int depth)
    {
        var exes = Subfolders(folder).SelectMany(child => CollectExes(child, depth + 1)).ToList();
        return exes.Count == 0 ? null : PickBest(exes, folder);
    }

    /// <summary>Subcarpetas que tienen algún ejecutable de juego dentro, con esos ejecutables.</summary>
    List<(DirectoryInfo Folder, List<FileInfo> Exes)> GameChildren(DirectoryInfo folder, int depth) =>
        Subfolders(folder)
            .Select(child => (child, CollectExes(child, depth + 1)))
            .Where(entry => entry.Item2.Count > 0)
            .ToList();

    /// <summary>
    /// Varias subcarpetas con ejecutables son varios juegos, salvo que sean las típicas de un mismo juego
    /// («bin», «x64»…) o que todas tengan el mismo ejecutable (versiones de 32 y 64 bits).
    /// </summary>
    static bool IsCollection(List<(DirectoryInfo Folder, List<FileInfo> Exes)> children)
    {
        var distinct = children.Where(child => !GenericFolders.Contains(child.Folder.Name)).ToList();
        if (distinct.Count < 2)
            return false;

        var exeNames = distinct
            .Select(child => string.Join('|', child.Exes.Select(exe => exe.Name.ToLowerInvariant()).Order()))
            .Distinct()
            .Count();
        return exeNames > 1;
    }

    List<FileInfo> CollectExes(DirectoryInfo folder, int depth)
    {
        var result = new List<FileInfo>();
        if (depth > MaxDepth)
            return result;

        ReportFolder();
        result.AddRange(Files(folder).Where(IsGameExe));
        foreach (var child in Subfolders(folder))
            result.AddRange(CollectExes(child, depth + 1));
        return result;
    }

    /// <summary>Elige el ejecutable principal: el que se llama como la carpeta, el menos escondido y el más grande.</summary>
    static FileInfo PickBest(List<FileInfo> exes, DirectoryInfo gameFolder)
    {
        var folderKey = Normalize(gameFolder.Name);
        return exes.MaxBy(exe =>
        {
            var key = Normalize(Path.GetFileNameWithoutExtension(exe.Name));
            var path = exe.FullName.ToLowerInvariant();
            var depth = Path.GetRelativePath(gameFolder.FullName, exe.DirectoryName ?? "").Count(c => c == Path.DirectorySeparatorChar);
            if (exe.DirectoryName == gameFolder.FullName)
                depth = -1;

            double score = 0;
            if (key.Length > 0 && folderKey.Length > 0)
            {
                if (key == folderKey)
                    score += 100;
                else if (key.Length >= 3 && (folderKey.Contains(key) || key.Contains(folderKey)))
                    score += 60;
            }
            score -= 15 * (depth + 1);
            score += Math.Log10(exe.Length + 1) * 5;
            if (key.Contains("shipping")) score += 20;
            if (key.Contains("launcher")) score -= 10;
            if (path.Contains("win64") || path.Contains("x64")) score += 5;
            if (path.Contains("win32") || path.Contains("x86")) score -= 5;
            return score;
        })!;
    }

    bool IsGameExe(FileInfo file)
    {
        if (!file.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            if (file.Length < MinExeSize)
                return false;
        }
        catch (IOException)
        {
            return false;
        }

        var name = file.Name.ToLowerInvariant();
        return !ExcludedExeWords.Any(name.Contains);
    }

    static bool IsShortcut(FileInfo file) =>
        ShortcutExtensions.Contains(file.Extension.ToLowerInvariant());

    void Add(string name, string path)
    {
        _found.Add(new FoundGame(name, path));
        _progress?.Report(new ScanProgress(_folders, _found.Count));
    }

    void ReportFolder()
    {
        _cancellation.ThrowIfCancellationRequested();
        if (++_folders % ProgressEvery == 0)
            _progress?.Report(new ScanProgress(_folders, _found.Count));
    }

    // Las carpetas sin permiso, ocultas, del sistema o enlaces (que podrían dar vueltas) se saltan sin más.
    static IEnumerable<DirectoryInfo> Subfolders(DirectoryInfo folder)
    {
        try
        {
            return folder.GetDirectories()
                .Where(child => !IgnoredFolders.Contains(child.Name) &&
                                (child.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) == 0)
                .ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return [];
        }
    }

    static IEnumerable<FileInfo> Files(DirectoryInfo folder)
    {
        try
        {
            return folder.GetFiles();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return [];
        }
    }

    static string Normalize(string text) => NonAlphanumeric().Replace(text.ToLowerInvariant(), "");

    /// <summary>«Hollow.Knight» o «hollow_knight» → «Hollow Knight»; «ico-pc-v0.7.2» → «Ico-Pc».</summary>
    public static string PrettifyName(string raw)
    {
        // Fuera la versión y la plataforma del final («-v0.7.2», «-win32-x86_64»…), que no son parte del nombre.
        var name = raw;
        string previous;
        do
        {
            previous = name;
            name = VersionOrPlatformSuffix().Replace(name, "");
        } while (name != previous && name.Length > 0);
        if (name.Length == 0)
            name = raw;

        name = name.Replace('_', ' ');
        if (!name.Contains(' ') && name.Count(c => c == '.') >= 1)
            name = name.Replace('.', ' ');
        name = Whitespace().Replace(name, " ").Trim();

        if (name.Length > 0 && name == name.ToLowerInvariant())
            name = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name);
        return name.Length == 0 ? raw : name;
    }

    [GeneratedRegex("[^a-z0-9]")]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[\s_.-]+(v?\d+(\.\d+)+|v\d+|windows|win32|win64|x86_64|x86|x64|amd64|portable)$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionOrPlatformSuffix();
}
