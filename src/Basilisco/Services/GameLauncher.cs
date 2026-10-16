using System.Diagnostics;
using System.IO;
using Basilisco.Models;

namespace Basilisco.Services;

public static class GameLauncher
{
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
