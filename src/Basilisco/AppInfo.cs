using System.IO;

namespace Basilisco;

public static class AppInfo
{
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    // Portable: los datos viven junto al .exe, nunca en AppData ni en el registro.
    public static string DataDirectory { get; } =
        Path.Combine(AppContext.BaseDirectory, "basilisco-data");

    public static string CoversDirectory { get; } = Path.Combine(DataDirectory, "covers");

    public static string FilterImagesDirectory { get; } = Path.Combine(DataDirectory, "filters");

    /// <summary>Franjas de cada consola (ps1.png, ps2.png…). Si falta alguna, Basilisco dibuja la suya.</summary>
    public static string BordersDirectory { get; } = Path.Combine(DataDirectory, "borders");
}
