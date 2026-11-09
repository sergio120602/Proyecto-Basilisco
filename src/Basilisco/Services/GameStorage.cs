using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Basilisco.Models;

namespace Basilisco.Services;

/// <summary>Lo que se guarda al desinstalar un juego: su carpeta entera o sus archivos sueltos (el principal y los que le acompañan).</summary>
/// <param name="Path">La carpeta del juego, o su archivo principal si son archivos sueltos.</param>
/// <param name="Files">Con archivos sueltos, todos ellos (el principal incluido); con carpeta, vacía.</param>
public sealed record InstallSource(string Path, bool IsFolder, IReadOnlyList<string> Files)
{
    /// <summary>La carpeta en la que está (o que es) lo que se guarda.</summary>
    public string Folder => IsFolder ? Path : System.IO.Path.GetDirectoryName(Path)!;
}

/// <summary>
/// Desinstala y vuelve a instalar juegos que no son de Steam para liberar espacio: al desinstalar, su carpeta se comprime
/// en un .zip en la carpeta de juegos desinstalados (mejor en otro disco) y se borra; al instalar, se descomprime en el mismo
/// sitio y se borra el .zip. Nada se borra hasta que el .zip está escrito entero y comprobado.
/// </summary>
public static class GameStorage
{
    /// <summary>Subcarpetas típicas donde los juegos de PC guardan el .exe: la carpeta del juego es la de encima.</summary>
    static readonly HashSet<string> ExeSubfolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "bin32", "bin64", "binaries", "win32", "win64", "x86", "x64", "x86_64", "amd64", "release", "retail", "shipping",
        "system", "exe", "executable", "game", "launcher",
    };

    /// <summary>Archivos que no son el juego y pueden estar sueltos en su carpeta (las demás cosas grandes serían otros juegos).</summary>
    const long LooseFileLimit = 64L * 1024 * 1024;

    // ---- Qué se guarda ----

    /// <summary>
    /// Lo que se guardaría al desinstalar el juego, o null si no se puede: es de Steam, ya está desinstalado, no se
    /// encuentra o su carpeta tiene más cosas (otros juegos, el escritorio, Basilisco…).
    /// </summary>
    public static InstallSource? FindSource(Game game, IReadOnlyCollection<Game> library)
    {
        if (game.SteamAppId is not null || game.Archive is not null)
            return null;
        try
        {
            // Los de las tiendas y los que tienen su carpeta elegida a mano: esa carpeta.
            if (game.InstallDir is { } installDir)
                return Directory.Exists(installDir) && IsSafeFolder(installDir, game, library) ? FolderSource(installDir) : null;

            if (GameLauncher.IsLink(game.Path))
                return null;
            var file = Path.GetFullPath(GameLauncher.FileOf(game));
            if (!File.Exists(file))
                return null;
            var folder = Path.GetDirectoryName(file);
            if (folder is null)
                return null;

            return game.Emulator is null ? PcSource(file, folder, game, library) : EmulatedSource(file, folder, game, library);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// Lo que se guardaría si el usuario elige a mano la carpeta del juego, o null si esa carpeta no vale (es la raíz de un
    /// disco, el escritorio, tiene otros juegos o tiene Basilisco). Si el juego es un archivo, tiene que estar dentro.
    /// </summary>
    public static InstallSource? SourceFromFolder(string folder, Game game, IReadOnlyCollection<Game> library)
    {
        try
        {
            folder = Path.GetFullPath(folder);
            if (!Directory.Exists(folder) || !IsSafeFolder(folder, game, library))
                return null;
            if (!GameLauncher.IsLink(game.Path) && !IsInside(Path.GetFullPath(GameLauncher.FileOf(game)), folder))
                return null;
            return FolderSource(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }

    static InstallSource FolderSource(string folder) => new(Path.GetFullPath(folder).TrimEnd('\\'), true, []);

    static InstallSource? PcSource(string file, string folder, Game game, IReadOnlyCollection<Game> library)
    {
        var extension = Path.GetExtension(file).ToLowerInvariant();
        if (extension == ".url")
            return null;
        // Un acceso directo: la carpeta de su destino.
        if (extension == ".lnk")
        {
            if (GameProcesses.ShortcutTarget(file) is not { } target || !File.Exists(target))
                return null;
            folder = Path.GetDirectoryName(Path.GetFullPath(target))!;
        }
        // De «Juego\bin\x64\juego.exe» sube hasta «Juego».
        while (ExeSubfolders.Contains(Path.GetFileName(folder)) && Path.GetDirectoryName(folder) is { } up && IsSafeFolder(up, game, library))
            folder = up;
        return IsSafeFolder(folder, game, library) ? FolderSource(folder) : null;
    }

    static InstallSource? EmulatedSource(string file, string folder, Game game, IReadOnlyCollection<Game> library)
    {
        // Juegos que son una carpeta: los de PS3 (…\JUEGO\PS3_GAME\USRDIR\EBOOT.BIN) y los de Wii U (…\JUEGO\code\juego.rpx).
        for (var dir = folder; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (Path.GetFileName(dir).Equals("PS3_GAME", StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(dir) is { } ps3Root)
                return IsSafeFolder(ps3Root, game, library) ? FolderSource(ps3Root) : null;
        }
        if (Path.GetFileName(folder).Equals("code", StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(folder) is { } wiiURoot &&
            Directory.Exists(Path.Combine(wiiURoot, "content")))
            return IsSafeFolder(wiiURoot, game, library) ? FolderSource(wiiURoot) : null;

        // La imagen del disco y las que la acompañan: «Juego.cue» con «Juego (Track 1).bin», «Juego.m3u» con sus discos…
        var stem = Path.GetFileNameWithoutExtension(file);
        var siblings = Directory.GetFiles(folder);
        var companions = siblings.Where(sibling => Path.GetFileName(sibling).StartsWith(stem, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!companions.Contains(file, StringComparer.OrdinalIgnoreCase))
            companions.Add(file);

        // Si la carpeta es solo suya (nada más que archivos pequeños, como partidas o ajustes), se guarda la carpeta entera.
        var extension = Path.GetExtension(file);
        var others = siblings.Except(companions, StringComparer.OrdinalIgnoreCase).ToList();
        var ownFolder = IsSafeFolder(folder, game, library)
            && others.All(other => !Path.GetExtension(other).Equals(extension, StringComparison.OrdinalIgnoreCase) && new FileInfo(other).Length < LooseFileLimit)
            && (Directory.GetDirectories(folder).Length == 0 || LooksLike(Path.GetFileName(folder), game.Name, stem));
        if (ownFolder)
            return FolderSource(folder);

        // Si no, solo sus archivos (los demás juegos de la carpeta se quedan donde están).
        return companions.Any(companion => OtherGameUses(companion, game, library)) ? null : new InstallSource(file, false, companions);
    }

    /// <summary>True si el nombre de la carpeta se parece al del juego («God of War (USA)» y «God of War»).</summary>
    static bool LooksLike(string folderName, string gameName, string fileStem)
    {
        var folder = Letters(folderName);
        return folder.Length >= 3 && new[] { Letters(gameName), Letters(fileStem) }.Any(name => name.Length >= 3 && (name.Contains(folder) || folder.Contains(name)));
    }

    static string Letters(string text) => new(text.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>
    /// True si se puede guardar la carpeta entera: no es la raíz de un disco ni una carpeta del sistema, no tiene Basilisco
    /// dentro ni otros juegos de la biblioteca.
    /// </summary>
    static bool IsSafeFolder(string folder, Game game, IReadOnlyCollection<Game> library)
    {
        folder = Path.GetFullPath(folder).TrimEnd('\\');
        if (GameProcesses.IsTooBroad(folder))
            return false;
        if (IsInside(AppContext.BaseDirectory, folder) || IsInside(AppInfo.DataDirectory, folder))
            return false;
        return !library.Any(other => other != game && PlacesOf(other).Any(place => IsInside(place, folder)));
    }

    static bool OtherGameUses(string file, Game game, IReadOnlyCollection<Game> library) =>
        library.Any(other => other != game && PlacesOf(other).Any(place => string.Equals(place, file, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Dónde está cada juego en el disco: su carpeta, su archivo y, si está desinstalado, de dónde salió.</summary>
    static IEnumerable<string> PlacesOf(Game game)
    {
        if (game.Archive is { } archive)
            yield return Full(archive.Source);
        if (game.InstallDir is { } installDir)
            yield return Full(installDir);
        if (!GameLauncher.IsLink(game.Path) && game.Path.Length > 0)
            yield return Full(GameLauncher.FileOf(game));
    }

    static string Full(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd('\\');
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    /// <summary>True si <paramref name="path"/> es <paramref name="folder"/> o está dentro de ella.</summary>
    public static bool IsInside(string path, string folder)
    {
        var full = Full(path);
        var root = Full(folder);
        return string.Equals(full, root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
    }

    // ---- Cuánto ocupa ----

    static readonly EnumerationOptions AllFiles = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    /// <summary>Lo que ocupa (en bytes). Se recorre en segundo plano: una carpeta de 100 GB tarda un momento.</summary>
    public static long SizeOf(InstallSource source, CancellationToken token)
    {
        if (!source.IsFolder)
            return source.Files.Sum(file => new FileInfo(file).Length);
        long total = 0;
        foreach (var file in new DirectoryInfo(source.Path).EnumerateFiles("*", AllFiles))
        {
            token.ThrowIfCancellationRequested();
            total += file.Length;
        }
        return total;
    }

    /// <summary>«12,4 GB», «830 MB»…</summary>
    public static string FormatSize(long bytes)
    {
        var spanish = CultureInfo.GetCultureInfo("es-ES");
        return bytes switch
        {
            >= 1L << 40 => (bytes / (double)(1L << 40)).ToString("0.##", spanish) + " TB",
            >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.#", spanish) + " GB",
            >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0", spanish) + " MB",
            _ => Math.Max(1, (bytes + 1023) / 1024).ToString(spanish) + " KB",
        };
    }

    /// <summary>Espacio libre en el disco de esa ruta, o null si no se sabe (carpetas de red…).</summary>
    public static long? FreeSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    // ---- Desinstalar ----

    /// <summary>
    /// Primer paso de desinstalar: comprime lo del juego en un .zip en <paramref name="archiveFolder"/> y lo comprueba.
    /// No borra nada (eso es <see cref="DeleteSourceAsync"/>, después de apuntar el .zip en la biblioteca). Si se cancela o
    /// falla, no queda ni el .zip a medias.
    /// </summary>
    /// <param name="progress">De 0 a 1.</param>
    /// <returns>Lo guardado y los archivos que lleva dentro (los que hay que borrar después, y ninguno más).</returns>
    public static Task<(GameArchive Archive, IReadOnlyList<string> Files)> CompressAsync(Game game, InstallSource source, string archiveFolder,
        ArchiveCompression compression, IProgress<double> progress, CancellationToken token) => Task.Run(() =>
    {
        if (IsInside(archiveFolder, source.Folder) && source.IsFolder)
            throw new IOException("La carpeta de juegos desinstalados está dentro de la del juego. Elige otra en Ajustes.");

        var files = Collect(source, out var emptyFolders);
        var total = files.Sum(file => file.Length);
        var level = compression switch
        {
            ArchiveCompression.Fast => CompressionLevel.Fastest,
            ArchiveCompression.Max => CompressionLevel.SmallestSize,
            _ => CompressionLevel.Optimal,
        };

        Directory.CreateDirectory(archiveFolder);
        var zipPath = UniqueZipPath(archiveFolder, game);
        var partial = zipPath + ".partial";
        try
        {
            var meter = new Meter(total, progress);
            using (var stream = new FileStream(partial, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 20))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, false, Encoding.UTF8))
            {
                foreach (var folder in emptyFolders)
                    zip.CreateEntry(folder + "/");
                foreach (var file in files)
                {
                    token.ThrowIfCancellationRequested();
                    var entry = zip.CreateEntry(file.Entry, level);
                    entry.LastWriteTime = ZipDate(File.GetLastWriteTime(file.Full));
                    using var input = new FileStream(file.Full, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
                    using var output = entry.Open();
                    Copy(input, output, meter, token);
                }
            }
            Verify(partial, files);
            File.Move(partial, zipPath, overwrite: false);
        }
        catch
        {
            TryDelete(partial);
            throw;
        }

        var archive = new GameArchive
        {
            File = zipPath,
            Source = source.Path,
            IsFolder = source.IsFolder,
            Size = total,
            CompressedSize = new FileInfo(zipPath).Length,
            Date = DateTime.Now,
        };
        return (archive, (IReadOnlyList<string>)files.Select(file => file.Full).ToList());
    }, token);

    /// <summary>
    /// Segundo paso de desinstalar: borra los archivos que se han guardado en el .zip (si mientras tanto ha aparecido alguno
    /// nuevo, se queda) y las carpetas que se hayan quedado vacías.
    /// </summary>
    /// <returns>Cuántos archivos no se pudieron borrar (estaban abiertos o protegidos: se quedan donde estaban).</returns>
    public static Task<int> DeleteSourceAsync(InstallSource source, IReadOnlyList<string> files) => Task.Run(() =>
    {
        var left = 0;
        foreach (var file in files)
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                left++;
            }
        }
        if (source.IsFolder)
            DeleteEmptyFolders(source.Path);
        return left;
    });

    /// <summary>Borra las carpetas vacías, de la más honda a la de arriba (las que tienen algo se quedan).</summary>
    static void DeleteEmptyFolders(string folder)
    {
        try
        {
            foreach (var child in Directory.GetDirectories(folder))
                DeleteEmptyFolders(child);
            if (!Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    sealed record ZipItem(string Full, string Entry, long Length);

    /// <summary>Los archivos que se guardan, con su nombre dentro del .zip; y las carpetas vacías (para que vuelvan igual).</summary>
    static List<ZipItem> Collect(InstallSource source, out List<string> emptyFolders)
    {
        emptyFolders = [];
        if (!source.IsFolder)
            return source.Files.Select(file => new ZipItem(file, Path.GetFileName(file), new FileInfo(file).Length)).ToList();

        var root = new DirectoryInfo(source.Path);
        var items = new List<ZipItem>();
        foreach (var entry in root.EnumerateFileSystemInfos("*", AllFiles))
        {
            // Los enlaces a otras carpetas no se pueden guardar en un .zip sin llevarse lo que no es del juego.
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException($"La carpeta tiene un enlace a otra carpeta ({entry.FullName}); no se puede guardar sin riesgo.");
            var relative = Path.GetRelativePath(root.FullName, entry.FullName).Replace('\\', '/');
            if (entry is FileInfo file)
                items.Add(new ZipItem(file.FullName, relative, file.Length));
            else if (entry is DirectoryInfo folder && !folder.EnumerateFileSystemInfos().Any())
                emptyFolders.Add(relative);
        }
        return items;
    }

    /// <summary>Abre el .zip recién hecho y comprueba que está todo, con su tamaño exacto.</summary>
    static void Verify(string zipPath, List<ZipItem> files)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var sizes = zip.Entries.Where(entry => !entry.FullName.EndsWith('/')).ToDictionary(entry => entry.FullName, entry => entry.Length, StringComparer.Ordinal);
        if (sizes.Count != files.Count || files.Any(file => !sizes.TryGetValue(file.Entry, out var length) || length != file.Length))
            throw new InvalidDataException("El archivo comprimido no ha salido bien. No se ha borrado nada.");
    }

    static string UniqueZipPath(string archiveFolder, Game game)
    {
        var name = string.Concat(game.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        if (name.Length == 0)
            name = "Juego";
        if (name.Length > 80)
            name = name[..80];
        var path = Path.Combine(archiveFolder, $"{name} [{game.Id[..8]}].zip");
        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(archiveFolder, $"{name} [{game.Id[..8]}] ({n}).zip");
        return path;
    }

    /// <summary>Los .zip solo guardan fechas entre 1980 y 2107.</summary>
    static DateTimeOffset ZipDate(DateTime date) =>
        date.Year < 1980 ? new DateTime(1980, 1, 1) : date.Year > 2107 ? new DateTime(2107, 12, 31) : date;

    // ---- Instalar ----

    /// <summary>Lo que ocupará al instalarlo (lo de dentro del .zip), en bytes.</summary>
    public static long InstalledSizeOf(GameArchive archive)
    {
        using var zip = ZipFile.OpenRead(archive.File);
        return zip.Entries.Sum(entry => entry.Length);
    }

    /// <summary>Descomprime el juego en el sitio de donde salió. El .zip se borra después (cuando la biblioteca ya lo da por instalado).</summary>
    public static Task InstallAsync(GameArchive archive, IProgress<double> progress, CancellationToken token) => Task.Run(() =>
    {
        var target = archive.IsFolder ? archive.Source : Path.GetDirectoryName(archive.Source)!;
        var root = Full(target) + "\\";
        using (var zip = ZipFile.OpenRead(archive.File))
        {
            var meter = new Meter(zip.Entries.Sum(entry => entry.Length), progress);
            Directory.CreateDirectory(target);
            foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested();
                var destination = Path.GetFullPath(Path.Combine(target, entry.FullName));
                // Nada puede salir de la carpeta del juego.
                if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"El archivo comprimido tiene una ruta no válida: {entry.FullName}");
                if (entry.FullName.EndsWith('/'))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using (var input = entry.Open())
                using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                    Copy(input, output, meter, token);
                File.SetLastWriteTime(destination, entry.LastWriteTime.DateTime);
            }
        }
    }, token);

    /// <summary>Borra el .zip de un juego ya instalado. Si no se puede (está abierto), se queda: no es grave.</summary>
    public static Task DeleteArchiveAsync(GameArchive archive) => Task.Run(() => TryDelete(archive.File));

    // ---- Común ----

    /// <summary>Cuenta lo copiado y avisa del progreso cada medio por ciento (no en cada trozo).</summary>
    sealed class Meter(long total, IProgress<double> progress)
    {
        long _done;
        double _last = -1;

        public void Add(long bytes)
        {
            _done += bytes;
            var fraction = total <= 0 ? 1 : Math.Min(1, _done / (double)total);
            if (fraction - _last >= 0.005 || fraction >= 1)
            {
                _last = fraction;
                progress.Report(fraction);
            }
        }
    }

    static void Copy(Stream input, Stream output, Meter meter, CancellationToken token)
    {
        var buffer = new byte[1 << 20];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            output.Write(buffer, 0, read);
            meter.Add(read);
        }
    }

    static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
