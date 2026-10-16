using System.IO;
using Basilisco.Models;

namespace Basilisco.Services;

/// <summary>
/// Guarda una copia de cada portada personalizada junto al .exe, para que la biblioteca siga siendo
/// portable aunque la imagen original se mueva o se borre.
/// </summary>
public static class CoverStore
{
    public static readonly string[] SupportedExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff"];

    public const string FileFilter =
        "Imágenes (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff";

    public static bool IsImage(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static string? GetPath(Game game) =>
        game.CoverImage is null ? null : Path.Combine(AppInfo.CoversDirectory, game.CoverImage);

    /// <summary>Copia la imagen como nueva portada del juego y borra la anterior.</summary>
    public static void SetCover(Game game, string sourcePath)
    {
        Directory.CreateDirectory(AppInfo.CoversDirectory);

        // Un nombre nuevo en cada cambio evita pisar un archivo que aún esté en uso.
        var fileName = $"{game.Id}-{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(sourcePath).ToLowerInvariant()}";
        File.Copy(sourcePath, Path.Combine(AppInfo.CoversDirectory, fileName), overwrite: true);

        RemoveCover(game);
        game.CoverImage = fileName;
    }

    public static void RemoveCover(Game game)
    {
        if (GetPath(game) is { } path)
        {
            try { File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        game.CoverImage = null;
    }
}
