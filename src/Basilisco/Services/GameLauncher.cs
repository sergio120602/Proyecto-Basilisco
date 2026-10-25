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

    /// <summary>
    /// El emulador de cada juego que se abre con uno: su ejecutable (null si no se encuentra) y los argumentos con los que
    /// se le pasa el juego. Lo pone la ventana principal, que tiene los ajustes.
    /// </summary>
    public static Func<string, (string? Exe, string Arguments)>? EmulatorOf { get; set; }

    /// <summary>
    /// El archivo del juego que tiene que existir para poder abrirlo: el de <see cref="Game.Path"/>, salvo en los juegos
    /// de RetroArch que están dentro de un comprimido («juegos.zip#juego.gba»), que es el comprimido.
    /// </summary>
    public static string FileOf(Game game) =>
        game.Emulator == "retroarch" && game.Path.IndexOf('#') is > 0 and var hash && !File.Exists(game.Path) ? game.Path[..hash] : game.Path;

    /// <returns>El proceso lanzado, si Windows lo da (para saber cuándo se cierra el juego).</returns>
    public static Process? Launch(Game game)
    {
        // Con emulador: se abre el emulador con el juego, directamente y a pantalla completa cuando se puede.
        if (game.Emulator is { } id)
        {
            var emulator = Emulators.Find(id);
            var (exe, arguments) = EmulatorOf?.Invoke(id) ?? (null, emulator?.DefaultArguments ?? "\"{game}\"");
            if (exe is null || !File.Exists(exe))
                throw new FileNotFoundException(
                    $"No se encuentra {emulator?.Name ?? id}. Elige dónde está en «Importación Juegos NO PC» → {emulator?.Name ?? id}.");
            return Process.Start(new ProcessStartInfo(exe, Emulators.BuildArguments(arguments, game.Path, game.EmulatorCore))
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
            });
        }

        // Los de Battle.net se abren con Battle.net (que inicia la sesión y comprueba las actualizaciones).
        if (game.Path.StartsWith("battlenet://", StringComparison.OrdinalIgnoreCase))
        {
            var battleNet = StoreLibraries.BattleNetExe() ?? throw new FileNotFoundException("No se encuentra Battle.net en este PC.");
            // Battle.net sigue abierto después de jugar: el juego se sigue por su carpeta, no por este proceso.
            Process.Start(new ProcessStartInfo(battleNet, $"--exec=\"launch {game.Path["battlenet://".Length..]}\"") { UseShellExecute = false })?.Dispose();
            return null;
        }

        // UseShellExecute permite abrir .exe, accesos directos (.lnk), .url, .bat y cualquier archivo asociado.
        var info = new ProcessStartInfo(game.Path) { UseShellExecute = true };

        // Muchos juegos buscan sus archivos relativos a su propia carpeta.
        var extension = Path.GetExtension(game.Path).ToLowerInvariant();
        if (extension is ".exe" or ".bat" or ".cmd")
            info.WorkingDirectory = Path.GetDirectoryName(game.Path) ?? "";

        return Process.Start(info);
    }
}
