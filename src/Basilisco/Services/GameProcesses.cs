using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Basilisco.Models;

namespace Basilisco.Services;

/// <summary>
/// Sabe si un juego sigue abierto mirando los procesos que se ejecutan desde su carpeta. Así se cuenta el tiempo
/// aunque el juego se abra con un lanzador que se cierra enseguida, o aunque vuelvas a Basilisco mientras juegas.
/// </summary>
public static class GameProcesses
{
    /// <summary>Carpeta del juego (la del .exe, la del destino del acceso directo o la de Steam), o null si no se sabe.</summary>
    public static string? FolderOf(Game game)
    {
        string? folder = null;
        if (game.SteamAppId is { } appId)
            folder = SteamLocal.InstallDirOf(appId);
        else if (game.InstallDir is { } installed)
            folder = installed;
        else if (game.Emulator is not null)
            folder = null;
        else if (!GameLauncher.IsLink(game.Path))
        {
            var target = Path.GetExtension(game.Path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? ShortcutTarget(game.Path) : game.Path;
            if (target is not null && Path.GetExtension(target).ToLowerInvariant() is ".exe" or ".bat" or ".cmd")
                folder = Path.GetDirectoryName(target);
        }
        return folder is not null && Directory.Exists(folder) && !IsTooBroad(folder) ? Path.GetFullPath(folder) : null;
    }

    /// <summary>Carpetas en las que hay de todo (el escritorio, Program Files…): ahí no se puede saber qué proceso es el juego.</summary>
    static bool IsTooBroad(string folder)
    {
        var full = Path.GetFullPath(folder).TrimEnd('\\');
        if (Path.GetPathRoot(full)?.TrimEnd('\\') == full)
            return true;
        var special = new[]
        {
            Environment.SpecialFolder.Desktop, Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory,
            Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.System, Environment.SpecialFolder.SystemX86, Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolder.MyDocuments,
        };
        return special.Select(Environment.GetFolderPath).Any(path => path.Length > 0 && string.Equals(path.TrimEnd('\\'), full, StringComparison.OrdinalIgnoreCase));
    }

    internal static string? ShortcutTarget(string lnk)
    {
        try
        {
            if (Type.GetTypeFromProgID("WScript.Shell") is not { } type)
                return null;
            dynamic shell = Activator.CreateInstance(type)!;
            var target = (string)shell.CreateShortcut(lnk).TargetPath;
            return target.Length > 0 ? target : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>True si hay algún proceso abierto desde esa carpeta (o el proceso con ese id), sin contar Basilisco.</summary>
    public static bool IsRunning(string? folder, int? processId)
    {
        var self = Environment.ProcessId;
        if (processId is { } id && id != self && IsAlive(id))
            return true;
        if (folder is null)
            return false;

        var prefix = folder.TrimEnd('\\') + "\\";
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id != self && ImagePath(process.Id) is { } path && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    static bool IsAlive(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Ruta del .exe de un proceso. Con el permiso mínimo, que vale también para los juegos con más privilegios.</summary>
    static string? ImagePath(int id)
    {
        var handle = OpenProcess(QueryLimitedInformation, false, id);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            var size = 1024;
            var builder = new StringBuilder(size);
            return QueryFullProcessImageName(handle, 0, builder, ref size) ? builder.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    const uint QueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);
}

/// <summary>
/// La partida en curso, guardada en basilisco-data/session.json para que, si cierras Basilisco mientras juegas,
/// al volver a abrirlo siga contando (o apunte lo jugado hasta la última vez que se vio el juego abierto).
/// </summary>
/// <param name="LastSeen">Última vez que se vio el juego abierto.</param>
public sealed record SavedSession(string GameId, DateTime Started, DateTime LastSeen, string? Folder, int? ProcessId)
{
    static string FilePath => Path.Combine(AppInfo.DataDirectory, "session.json");

    public static SavedSession? Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<SavedSession>(File.ReadAllText(FilePath)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppInfo.DataDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static void Clear()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
