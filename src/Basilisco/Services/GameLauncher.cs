using System.Diagnostics;
using System.IO;
using Basilisco.Models;

namespace Basilisco.Services;

public static class GameLauncher
{
    /// <summary>True si el juego se lanza con un enlace (steam://…) en lugar de con un archivo del disco.</summary>
    public static bool IsLink(string path) => path.Contains("://", StringComparison.Ordinal);

    /// <summary>Tipos de archivo que se pueden añadir como juego.</summary>
    public const string FileFilter =
        "Juegos y aplicaciones (*.exe;*.lnk;*.url;*.bat;*.cmd)|*.exe;*.lnk;*.url;*.bat;*.cmd|Todos los archivos (*.*)|*.*";

    public static void Launch(Game game)
    {
        // UseShellExecute permite abrir .exe, accesos directos (.lnk), .url, .bat y cualquier archivo asociado.
        var info = new ProcessStartInfo(game.Path) { UseShellExecute = true };

        // Muchos juegos buscan sus archivos relativos a su propia carpeta.
        var extension = Path.GetExtension(game.Path).ToLowerInvariant();
        if (extension is ".exe" or ".bat" or ".cmd")
            info.WorkingDirectory = Path.GetDirectoryName(game.Path) ?? "";

        Process.Start(info);
    }
}
