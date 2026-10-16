using System.IO;
using Basilisco.Models;

namespace Basilisco.Services;

/// <summary>
/// Guarda una copia de cada imagen elegida por el usuario (portadas de juegos e imágenes de filtros)
/// junto al .exe, para que todo siga siendo portable aunque la imagen original se mueva o se borre.
/// </summary>
public static class ImageStore
{
    public static readonly string[] SupportedExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff"];

    public const string FileFilter =
        "Imágenes (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff";

    public static bool IsImage(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static string? GetCoverPath(Game game) => Resolve(AppInfo.CoversDirectory, game.CoverImage);

    public static void SetCover(Game game, string sourcePath)
    {
        var fileName = Import(sourcePath, AppInfo.CoversDirectory, game.Id);
        RemoveCover(game);
        game.CoverImage = fileName;
    }

    public static void RemoveCover(Game game)
    {
        Delete(AppInfo.CoversDirectory, game.CoverImage);
        game.CoverImage = null;
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
