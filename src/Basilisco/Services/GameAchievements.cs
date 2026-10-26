using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Basilisco.Models;

namespace Basilisco.Services;

/// <summary>
/// Logros que trae el propio juego, guardados en su carpeta: los de los ports que llevan su propio sistema de logros (como
/// rac-rs, el de Ratchet &amp; Clank, con los de RetroAchievements importados). Se buscan junto al juego y se vuelven a leer
/// cuando cambian, así que lo que desbloqueas jugando aparece en su perfil de trofeos.
/// </summary>
/// <remarks>
/// El formato (el de rac-rs) es una carpeta «achievements» con:
/// set.txt (una línea «game», id y nombre; y una «a» por logro: id, puntos, tipo, título, descripción y su lógica, con
/// tabuladores), unlocked.txt (una línea por logro conseguido: id y la hora en segundos Unix) y badges.bin (los iconos,
/// «RACB», cuántos, su tamaño y los píxeles RGBA, en el orden de set.txt).
/// </remarks>
public static class GameAchievements
{
    const string SetFile = "set.txt";
    const string UnlockedFile = "unlocked.txt";
    const string BadgesFile = "badges.bin";

    /// <summary>Dónde la guardan los juegos, desde su carpeta (o la de más arriba, como la de un lanzador .cmd).</summary>
    static readonly string[] Places = ["achievements", Path.Combine("player", "achievements"), Path.Combine("user", "achievements"), Path.Combine("save", "achievements")];

    /// <summary>
    /// La carpeta de logros del juego, si tiene: junto a su archivo o hasta dos carpetas más arriba (el ejecutable de un
    /// port suele estar en build\release y sus datos, arriba). Null si no tiene.
    /// </summary>
    public static string? Find(Game game)
    {
        var start = game.InstallDir;
        if (start is null && !GameLauncher.IsLink(game.Path))
        {
            var path = Path.GetExtension(game.Path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? GameProcesses.ShortcutTarget(game.Path) ?? game.Path : game.Path;
            start = Path.GetDirectoryName(path);
        }
        var dir = start;
        for (var level = 0; level < 3 && dir is not null && Path.GetPathRoot(dir) != dir; level++, dir = Path.GetDirectoryName(dir))
        {
            foreach (var place in Places)
            {
                var folder = Path.Combine(dir, place);
                if (IsSet(folder))
                    return folder;
            }
        }
        return null;
    }

    /// <summary>True si la carpeta tiene una lista de logros que se entiende.</summary>
    public static bool IsSet(string folder)
    {
        var set = Path.Combine(folder, SetFile);
        try
        {
            return File.Exists(set) && File.ReadLines(set).Take(50).Any(line => line.StartsWith("a\t", StringComparison.Ordinal) && line.Split('\t').Length >= 6);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Cuándo cambiaron por última vez la lista o los conseguidos (para volver a leerlos solo entonces).</summary>
    public static DateTime Changed(string folder)
    {
        var times = new[] { SetFile, UnlockedFile }.Select(name => Path.Combine(folder, name)).Where(File.Exists).Select(File.GetLastWriteTime);
        return times.DefaultIfEmpty(DateTime.MinValue).Max();
    }

    /// <summary>Los logros (con los que tienes y cuándo los conseguiste) y el nombre del juego. Null si no se pueden leer.</summary>
    public static (string Title, List<OwnTrophy> Trophies)? Read(string folder)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(Path.Combine(folder, SetFile));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var unlocked = new Dictionary<string, DateTime?>();
        try
        {
            var file = Path.Combine(folder, UnlockedFile);
            if (File.Exists(file))
            {
                // Compartido: el juego puede estar escribiendo en él mientras se juega.
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                while (reader.ReadLine() is { } line)
                {
                    var fields = line.Split('\t');
                    if (fields[0].Trim() is not { Length: > 0 } id)
                        continue;
                    DateTime? when = fields.Length > 1 && long.TryParse(fields[1].Trim(), out var unix) && unix > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime : null;
                    unlocked.TryAdd(id, when);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        var title = Path.GetFileName(Path.GetDirectoryName(folder.TrimEnd('\\'))) ?? "";
        var trophies = new List<OwnTrophy>();
        var order = 0;
        var icons = Icons(folder);
        foreach (var fields in lines.Select(line => line.Split('\t')))
        {
            if (fields is ["game", _, var name, ..] && name.Length > 0)
            {
                title = name;
                continue;
            }
            if (fields is not ["a", var id, var points, var kind, var name2, var description, ..])
                continue;
            var index = order++;
            // RetroAchievements añade un aviso de «emulador desconocido» a los clientes que no conoce: no es un logro del juego.
            if (id == "101000001" || name2.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase) && points == "0")
                continue;
            var got = unlocked.TryGetValue(id, out var when);
            var (icon, locked) = icons?.Invoke(index, id) ?? (null, null);
            // Con la columna «badge» (una más antes de la lógica, como el port de Budokai Tenkaichi 3), su imagen está en
            // achievements/badges/<badge>.png.
            if (icon is null && fields.Length >= 8 && BadgeImage(folder, fields[6]) is { } image)
                (icon, locked) = (image, GrayBadge(folder, image));
            trophies.Add(new OwnTrophy
            {
                Id = "game-" + id,
                Title = name2.Trim(),
                Description = description.Trim(),
                Tier = TierFor(int.TryParse(points, out var p) ? p : 0, kind),
                Unlocked = got,
                UnlockedAt = when,
                Icon = icon,
                IconLocked = locked,
            });
        }
        if (trophies.Any(t => t.Icon is null))
            FindIcons(folder, trophies);
        return (title, trophies);
    }

    // ---- Imágenes de achievements/badges ----

    /// <summary>La imagen de esa insignia en achievements/badges (en .png, .jpg o .webp), o null.</summary>
    static string? BadgeImage(string folder, string badge)
    {
        badge = badge.Trim();
        if (badge.Length == 0 || badge.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;
        return new[] { ".png", ".jpg", ".jpeg", ".webp" }.Select(ext => Path.Combine(folder, "badges", badge + ext)).FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// La de cuando aún no lo tienes: la que trae el juego («&lt;badge&gt;_lock.png») o, si no, la misma en gris, creada una vez
    /// en basilisco-data/trophies (y otra vez si cambia la imagen).
    /// </summary>
    static string? GrayBadge(string folder, string image)
    {
        var name = Path.GetFileNameWithoutExtension(image);
        foreach (var suffix in new[] { "_lock", "_locked", "_gray", "_grey" })
        {
            var own = Path.Combine(Path.GetDirectoryName(image)!, name + suffix + Path.GetExtension(image));
            if (File.Exists(own))
                return own;
        }
        var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(folder.ToLowerInvariant())))[..12];
        var gray = Path.Combine(AppInfo.DataDirectory, "trophies", key, "badges", name + "_lock.png");
        try
        {
            if (File.Exists(gray) && File.GetLastWriteTimeUtc(gray) >= File.GetLastWriteTimeUtc(image))
                return gray;
            Directory.CreateDirectory(Path.GetDirectoryName(gray)!);
            BitmapSource source;
            using (var stream = File.OpenRead(image))
            {
                var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
                source = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            }
            var (w, h) = (source.PixelWidth, source.PixelHeight);
            var pixels = new byte[w * h * 4];
            source.CopyPixels(pixels, w * 4, 0);
            for (var p = 0; p < pixels.Length; p += 4)
            {
                var level = (byte)(((pixels[p + 2] * 77 + pixels[p + 1] * 151 + pixels[p] * 28) >> 8) / 2 + 16);
                pixels[p] = pixels[p + 1] = pixels[p + 2] = level;
            }
            Save(pixels, w, h, gray);
            return gray;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException
                                   or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    // ---- Imágenes sueltas en la carpeta del juego ----

    static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];

    /// <summary>Carpetas que no tienen imágenes de logros y pueden tener miles de archivos (compilaciones, cachés, registros).</summary>
    static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "build", "target", "toolchain", "node_modules", ".git", "obj", "cache", "shader_cache", "shaders", "logs", "$systemupdate",
    };

    /// <summary>
    /// A los logros sin icono (sin badges.bin, o con alguno que falte) se les busca su imagen en la carpeta del juego y en
    /// todas las de dentro: una que se llame como el número del logro («26.png», «achievement_26.png», «trophy-26.jpg»…) o
    /// como su título. La que acaba en «_lock», «_locked», «_gray» o «_grey» es la de cuando aún no lo tienes.
    /// </summary>
    static void FindIcons(string folder, List<OwnTrophy> trophies)
    {
        // La carpeta del juego: la de encima de «achievements» (y de «player», «user» o «save», si va dentro de una).
        var root = Path.GetDirectoryName(folder.TrimEnd('\\'));
        if (root is not null && Path.GetFileName(root).ToLowerInvariant() is "player" or "user" or "save" or "userdata")
            root = Path.GetDirectoryName(root);
        if (root is null || Path.GetPathRoot(root) == root)
            return;

        var normal = new Dictionary<string, string>();
        var locked = new Dictionary<string, string>();
        foreach (var image in Images(root))
        {
            var name = Path.GetFileNameWithoutExtension(image).ToLowerInvariant();
            var isLocked = false;
            foreach (var suffix in new[] { "_locked", "_lock", "_gray", "_grey", "-locked", "-lock", "-gray", "-grey" })
            {
                if (name.EndsWith(suffix, StringComparison.Ordinal))
                {
                    name = name[..^suffix.Length];
                    isLocked = true;
                    break;
                }
            }
            var prefixed = false;
            foreach (var prefix in new[] { "achievements", "achievement", "achiev", "ach", "trophies", "trophy", "trop", "badge", "logro", "trofeo" })
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length)
                {
                    name = name[prefix.Length..].TrimStart('_', '-', ' ');
                    prefixed = true;
                    break;
                }
            }
            // Un «1.png» cualquiera puede ser una textura del juego: sin prefijo, solo si está en una carpeta de logros.
            var relative = image[root.Length..].ToLowerInvariant();
            if (!prefixed && !new[] { "achiev", "troph", "badge", "logro", "trofeo" }.Any(relative.Contains))
                continue;
            (isLocked ? locked : normal).TryAdd(Key(name), image);
        }
        if (normal.Count == 0 && locked.Count == 0)
            return;

        foreach (var trophy in trophies.Where(t => t.Icon is null))
        {
            var id = trophy.Id.StartsWith("game-", StringComparison.Ordinal) ? trophy.Id[5..] : trophy.Id;
            foreach (var key in new[] { Key(id), Key(trophy.Title) })
            {
                if (key.Length == 0 || !normal.TryGetValue(key, out var icon) && !locked.TryGetValue(key, out icon))
                    continue;
                trophy.Icon = normal.GetValueOrDefault(key) ?? icon;
                trophy.IconLocked ??= locked.GetValueOrDefault(key);
                break;
            }
        }
    }

    /// <summary>Solo letras y números, en minúsculas («Look, Plumber's Crack!» → «lookplumberscrack»; «026» → «26»).</summary>
    static string Key(string text)
    {
        var key = new string(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return key.All(char.IsDigit) && key.Length > 0 ? key.TrimStart('0') is { Length: > 0 } number ? number : "0" : key;
    }

    /// <summary>Las imágenes de la carpeta y de todas las de dentro (sin las pesadas de arriba), hasta 20 000 archivos mirados.</summary>
    static IEnumerable<string> Images(string root)
    {
        var pending = new Stack<(string Dir, int Depth)>([(root, 0)]);
        var seen = 0;
        while (pending.Count > 0 && seen < 20_000)
        {
            var (dir, depth) = pending.Pop();
            string[] files, dirs;
            try
            {
                files = Directory.GetFiles(dir);
                dirs = depth < 8 ? Directory.GetDirectories(dir) : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                continue;
            }
            seen += files.Length;
            foreach (var file in files)
            {
                if (ImageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    yield return file;
            }
            foreach (var sub in dirs)
            {
                if (!SkippedFolders.Contains(Path.GetFileName(sub)))
                    pending.Push((sub, depth + 1));
            }
        }
    }

    /// <summary>
    /// El trofeo según los puntos (los de RetroAchievements van de 1 a 100): de 25 en adelante, oro; de 10, plata; los demás,
    /// bronce. El de terminar el juego, oro como mínimo.
    /// </summary>
    static TrophyTier TierFor(int points, string kind) =>
        points >= 25 || kind == "win_condition" ? TrophyTier.Gold : points >= 10 ? TrophyTier.Silver : TrophyTier.Bronze;

    /// <summary>
    /// Los iconos de badges.bin pasados a PNG (el normal y el gris de los que aún no tienes), en basilisco-data/trophies: se
    /// crean una vez y se rehacen si cambia el archivo. Devuelve dónde está cada uno (por su orden en la lista), o null.
    /// </summary>
    static Func<int, string, (string?, string?)>? Icons(string folder)
    {
        var badges = Path.Combine(folder, BadgesFile);
        if (!File.Exists(badges))
            return null;
        var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(folder.ToLowerInvariant())))[..12];
        var target = Path.Combine(AppInfo.DataDirectory, "trophies", key);
        var stamp = Path.Combine(target, "badges.stamp");
        var version = $"{new FileInfo(badges).Length}-{File.GetLastWriteTimeUtc(badges).Ticks}";
        try
        {
            if (!File.Exists(stamp) || File.ReadAllText(stamp) != version)
            {
                if (Directory.Exists(target))
                    Directory.Delete(target, recursive: true);
                Directory.CreateDirectory(target);
                if (!WriteIcons(File.ReadAllBytes(badges), target))
                    return null;
                File.WriteAllText(stamp, version);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
        return (index, _) =>
        {
            var icon = Path.Combine(target, $"{index}.png");
            var gray = Path.Combine(target, $"{index}_lock.png");
            return (File.Exists(icon) ? icon : null, File.Exists(gray) ? gray : null);
        };
    }

    static bool WriteIcons(byte[] data, string target)
    {
        if (data.Length < 12 || Encoding.ASCII.GetString(data, 0, 4) != "RACB")
            return false;
        var count = BitConverter.ToInt32(data, 4);
        var size = BitConverter.ToInt32(data, 8);
        if (count <= 0 || size is <= 0 or > 512 || data.Length < 12 + (long)count * size * size * 4)
            return false;
        var bytes = size * size * 4;
        for (var i = 0; i < count; i++)
        {
            var rgba = data.AsSpan(12 + i * bytes, bytes);
            var color = new byte[bytes];
            var gray = new byte[bytes];
            for (var p = 0; p < bytes; p += 4)
            {
                byte r = rgba[p], g = rgba[p + 1], b = rgba[p + 2], a = rgba[p + 3];
                color[p] = b;
                color[p + 1] = g;
                color[p + 2] = r;
                color[p + 3] = a;
                var level = (byte)(((r * 77 + g * 151 + b * 28) >> 8) / 2 + 16);
                gray[p] = gray[p + 1] = gray[p + 2] = level;
                gray[p + 3] = a;
            }
            Save(color, size, size, Path.Combine(target, $"{i}.png"));
            Save(gray, size, size, Path.Combine(target, $"{i}_lock.png"));
        }
        return true;
    }

    static void Save(byte[] bgra, int width, int height, string path)
    {
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
