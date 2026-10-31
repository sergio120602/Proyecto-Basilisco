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

    public static readonly string[] VideoExtensions = [".mp4", ".wmv", ".avi", ".mov", ".m4v", ".mkv", ".webm"];

    public const string VideoFilter = "Vídeos (*.mp4;*.webm;*.mkv;*.mov;*.m4v;*.wmv;*.avi)|*.mp4;*.webm;*.mkv;*.mov;*.m4v;*.wmv;*.avi";

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

    /// <summary>Ruta de la imagen del disco o del manual del juego, o null si no tiene.</summary>
    public static string? GetExtraPath(Game game, GameExtra extra) => Resolve(AppInfo.CoversDirectory, ExtraFile(game, extra));

    static string? ExtraFile(Game game, GameExtra extra) => extra switch
    {
        GameExtra.Disc => game.Disc,
        GameExtra.Manual => game.Manual,
        _ => game.Inside,
    };

    static void SetExtraFile(Game game, GameExtra extra, string? file)
    {
        switch (extra)
        {
            case GameExtra.Disc: game.Disc = file; break;
            case GameExtra.Manual: game.Manual = file; break;
            default: game.Inside = file; break;
        }
    }

    /// <summary>Copia la imagen como disco o manual del juego y borra la anterior.</summary>
    public static void SetExtra(Game game, GameExtra extra, string sourcePath)
    {
        var fileName = Import(sourcePath, AppInfo.CoversDirectory, $"{game.Id}-{extra.ToString().ToLowerInvariant()}");
        RemoveExtra(game, extra);
        SetExtraFile(game, extra, fileName);
    }

    public static void RemoveExtra(Game game, GameExtra extra)
    {
        Delete(AppInfo.CoversDirectory, ExtraFile(game, extra));
        SetExtraFile(game, extra, null);
    }

    /// <summary>Imagen del disco de la banda sonora, o null si no tiene.</summary>
    public static string? GetSoundtrackDiscPath(Game game) => Resolve(AppInfo.CoversDirectory, game.Soundtrack?.Disc);

    /// <summary>Copia la imagen como el disco de la banda sonora y borra la anterior.</summary>
    public static void SetSoundtrackDisc(Game game, string sourcePath)
    {
        if (game.Soundtrack is not { } soundtrack)
            return;
        var fileName = Import(sourcePath, AppInfo.CoversDirectory, $"{game.Id}-soundtrack");
        Delete(AppInfo.CoversDirectory, soundtrack.Disc);
        soundtrack.Disc = fileName;
        soundtrack.DiscFrame = null;
    }

    public static void RemoveSoundtrackDisc(Game game)
    {
        if (game.Soundtrack is not { } soundtrack)
            return;
        Delete(AppInfo.CoversDirectory, soundtrack.Disc);
        soundtrack.Disc = null;
    }

    /// <summary>Imagen de la consola para el menú de consolas, o null si no tiene.</summary>
    public static string? GetPlatformImagePath(string platformId) => GetMenuImagePath(AppInfo.PlatformImagesDirectory, platformId);

    /// <summary>Copia la imagen como la de esa consola y borra la anterior.</summary>
    public static void SetPlatformImage(string platformId, string sourcePath) => SetMenuImage(AppInfo.PlatformImagesDirectory, platformId, sourcePath);

    public static void RemovePlatformImage(string platformId) => RemoveMenuImage(AppInfo.PlatformImagesDirectory, platformId);

    /// <summary>
    /// Imagen de una casilla de los menús en franja (una consola, un miembro de la familia): el archivo más reciente de
    /// la carpeta que empieza por su nombre, o null si no tiene.
    /// </summary>
    public static string? GetMenuImagePath(string directory, string id) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, id + "-*").Where(IsImage).OrderDescending().FirstOrDefault()
            : null;

    public static void SetMenuImage(string directory, string id, string sourcePath)
    {
        var old = GetMenuImagePath(directory, id);
        Import(sourcePath, directory, id);
        if (old is not null)
            Delete(directory, Path.GetFileName(old));
    }

    public static void RemoveMenuImage(string directory, string id)
    {
        if (GetMenuImagePath(directory, id) is { } path)
            Delete(directory, Path.GetFileName(path));
    }

    public static string? GetManualPdfPath(Game game) => Resolve(AppInfo.ManualsDirectory, game.ManualPdf);

    /// <summary>Nombre del manual en PDF que se lee ahora.</summary>
    public static string ManualPdfName(Game game) => game.ManualPdfName ?? "Manual en PDF";

    /// <summary>
    /// Copia el PDF como un manual más del juego y pasa a ser el que se lee; el que se leía se queda guardado con los
    /// demás, para poder volver a elegirlo.
    /// </summary>
    public static void AddManualPdf(Game game, string sourcePath, string name)
    {
        var fileName = Import(sourcePath, AppInfo.ManualsDirectory, $"{game.Id}-manual");
        if (game.ManualPdf is not null)
            (game.OtherManualPdfs ??= []).Insert(0, new SavedManual(game.ManualPdf, ManualPdfName(game), game.ManualPdfRotations));
        game.ManualPdf = fileName;
        game.ManualPdfName = name;
        game.ManualPdfRotations = null;
    }

    /// <summary>Pasa a leerse ese manual guardado; el que se leía se queda con los demás.</summary>
    public static void SelectManualPdf(Game game, SavedManual manual)
    {
        if (game.OtherManualPdfs is not { } others || !others.Remove(manual))
            return;
        if (game.ManualPdf is not null)
            others.Insert(0, new SavedManual(game.ManualPdf, ManualPdfName(game), game.ManualPdfRotations));
        game.ManualPdf = manual.File;
        game.ManualPdfName = manual.Name;
        game.ManualPdfRotations = manual.Rotations;
        if (others.Count == 0)
            game.OtherManualPdfs = null;
    }

    /// <summary>Borra el manual en PDF que se lee; si hay más guardados, pasa a leerse el siguiente.</summary>
    public static void RemoveManualPdf(Game game)
    {
        Delete(AppInfo.ManualsDirectory, game.ManualPdf);
        game.ManualPdf = null;
        game.ManualPdfName = null;
        game.ManualPdfRotations = null;
        if (game.OtherManualPdfs is [var next, ..])
            SelectManualPdf(game, next);
    }

    /// <summary>Borra todos los manuales en PDF del juego.</summary>
    public static void RemoveAllManualPdfs(Game game)
    {
        foreach (var manual in game.OtherManualPdfs ?? [])
            Delete(AppInfo.ManualsDirectory, manual.File);
        game.OtherManualPdfs = null;
        RemoveManualPdf(game);
    }

    /// <summary>Borra todas las imágenes del juego, de todos los formatos (al quitarlo de la biblioteca).</summary>
    public static void RemoveAll(Game game)
    {
        RemoveAllManualPdfs(game);
        RemoveBackground(game);
        RemoveExtra(game, GameExtra.Disc);
        RemoveExtra(game, GameExtra.Manual);
        RemoveExtra(game, GameExtra.Inside);
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
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        var fileName = $"{ownerId}-{DateTime.Now:yyyyMMddHHmmss}{extension}";
        // Dos en el mismo segundo (dos manuales del mismo juego) no se pisan: el segundo lleva un número.
        for (var i = 2; File.Exists(Path.Combine(directory, fileName)); i++)
            fileName = $"{ownerId}-{DateTime.Now:yyyyMMddHHmmss}-{i}{extension}";
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
