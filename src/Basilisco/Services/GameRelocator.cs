using System.IO;

namespace Basilisco.Services;

/// <summary>
/// Encuentra un juego que se ha movido de carpeta. Busca la misma ruta final (carpeta del juego + ejecutable)
/// colgando de las carpetas cercanas que todavía existen: por ejemplo, si
/// «common\Zelda\zelda.exe» ya no está pero existe «common\ports\Zelda\zelda.exe», lo encuentra.
/// </summary>
public static class GameRelocator
{
    /// <summary>Cuántas carpetas hacia arriba se sube desde la ruta antigua para buscar.</summary>
    const int MaxLevelsUp = 3;

    public static string? FindMoved(string missingPath)
    {
        try
        {
            var root = Path.GetPathRoot(missingPath);
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return null; // Disco desconectado: no tiene sentido buscar.

            var ancestor = Path.GetDirectoryName(missingPath);
            while (ancestor is not null && !Directory.Exists(ancestor))
                ancestor = Path.GetDirectoryName(ancestor);

            for (var level = 0; ancestor is not null && level < MaxLevelsUp; level++)
            {
                var tail = Path.GetRelativePath(ancestor, missingPath);
                if (Search(ancestor, tail) is { } found)
                    return found;
                ancestor = Path.GetDirectoryName(ancestor);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
        }
        return null;
    }

    // Prueba «carpeta\tail», «carpeta\*\tail» y «carpeta\*\*\tail».
    static string? Search(string folder, string tail)
    {
        var direct = Path.Combine(folder, tail);
        if (File.Exists(direct))
            return direct;

        foreach (var child in Subfolders(folder))
        {
            var candidate = Path.Combine(child, tail);
            if (File.Exists(candidate))
                return candidate;
        }

        foreach (var child in Subfolders(folder))
        {
            foreach (var grandchild in Subfolders(child))
            {
                var candidate = Path.Combine(grandchild, tail);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }

    static IEnumerable<string> Subfolders(string folder)
    {
        try
        {
            return Directory.GetDirectories(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }
    }
}
