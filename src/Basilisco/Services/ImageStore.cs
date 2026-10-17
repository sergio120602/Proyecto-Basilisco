using System.IO;
using Basilisco.Models;

namespace Basilisco.Services;

/// <summary>
/// Guarda una copia de cada imagen elegida por el usuario (portadas, contraportadas e imágenes de filtros)
/// junto al .exe, para que todo siga siendo portable aunque la imagen original se mueva o se borre.
/// </summary>
public static class ImageStore
{
    public static readonly string[] SupportedExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff"];

    public const string FileFilter =
        "Imágenes (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff";

    public static bool IsImage(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Ruta de la imagen de esa cara en ese formato, o null si no tiene.</summary>
    public static string? GetPath(Game game, CoverFormat format, Face face) =>
        Resolve(AppInfo.CoversDirectory, game.Faces(format).Get(face));

    /// <summary>Copia la imagen como nueva cara del juego en ese formato y borra la anterior.</summary>
    public static void Set(Game game, CoverFormat format, Face face, string sourcePath)
    {
        var owner = $"{game.Id}-{format.ToString().ToLowerInvariant()}-{face.ToString().ToLowerInvariant()}";
        var fileName = Import(sourcePath, AppInfo.CoversDirectory, owner);
        Remove(game, format, face);
        game.Faces(format).Set(face, fileName);
    }

    public static void Remove(Game game, CoverFormat format, Face face)
    {
        var faces = game.Faces(format);
        Delete(AppInfo.CoversDirectory, faces.Get(face));
        faces.Set(face, null);
        faces.SetFrame(face, null);
    }

    public static readonly string[] VideoExtensions = [".mp4", ".wmv", ".avi", ".mov", ".m4v", ".mkv"];

    public const string VideoFilter = "Vídeos (*.mp4;*.wmv;*.avi;*.mov;*.m4v;*.mkv)|*.mp4;*.wmv;*.avi;*.mov;*.m4v;*.mkv";

    public static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static string? GetBackgroundPath(Game game) => Resolve(AppInfo.BackgroundsDirectory, game.Background);

    /// <summary>Copia la imagen o el vídeo como fondo del juego y borra el anterior.</summary>
    public static void SetBackground(Game game, string sourcePath)
    {
        var fileName = Import(sourcePath, AppInfo.BackgroundsDirectory, $"{game.Id}-bg");
        RemoveBackground(game);
        game.Background = fileName;
        game.BackgroundIsVideo = IsVideo(sourcePath);
    }

    public static void RemoveBackground(Game game)
    {
        Delete(AppInfo.BackgroundsDirectory, game.Background);
        game.Background = null;
        game.BackgroundIsVideo = false;
    }

    public static string? GetGlobalBackgroundPath(AppSettings settings) => Resolve(AppInfo.BackgroundsDirectory, settings.Background);

    /// <summary>Copia la imagen o el vídeo como fondo general y borra el anterior.</summary>
    public static void SetGlobalBackground(AppSettings settings, string sourcePath)
    {
        var fileName = Import(sourcePath, AppInfo.BackgroundsDirectory, "global-bg");
        RemoveGlobalBackground(settings);
        settings.Background = fileName;
        settings.BackgroundIsVideo = IsVideo(sourcePath);
    }

    public static void RemoveGlobalBackground(AppSettings settings)
    {
        Delete(AppInfo.BackgroundsDirectory, settings.Background);
        settings.Background = null;
        settings.BackgroundIsVideo = false;
    }

    /// <summary>Borra todas las imágenes del juego, de todos los formatos (al quitarlo de la biblioteca).</summary>
    public static void RemoveAll(Game game)
    {
        RemoveBackground(game);
        foreach (var format in Enum.GetValues<CoverFormat>())
        {
            if (format == CoverFormat.Portrait && game.Portrait is null)
                continue;
            foreach (var face in Enum.GetValues<Face>())
                Remove(game, format, face);
        }
    }

    public static string? GetFilterImagePath(GameFilter filter) => Resolve(AppInfo.FilterImagesDirectory, filter.Image);

    public static void SetFilterImage(GameFilter filter, string sourcePath)
    {
        var fileName = Import(sourcePath, AppInfo.FilterImagesDirectory, filter.Id);
        RemoveFilterImage(filter);
        filter.Image = fileName;
    }

    public static void RemoveFilterImage(GameFilter filter)
    {
        Delete(AppInfo.FilterImagesDirectory, filter.Image);
        filter.Image = null;
    }

    static string? Resolve(string directory, string? fileName) =>
        fileName is null ? null : Path.Combine(directory, fileName);

    static string Import(string sourcePath, string directory, string ownerId)
    {
        Directory.CreateDirectory(directory);

        // Un nombre nuevo en cada cambio evita pisar un archivo que aún esté en uso.
        var fileName = $"{ownerId}-{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(sourcePath).ToLowerInvariant()}";
        File.Copy(sourcePath, Path.Combine(directory, fileName), overwrite: true);
        return fileName;
    }

    static void Delete(string directory, string? fileName)
    {
        if (Resolve(directory, fileName) is not { } path)
            return;

        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
