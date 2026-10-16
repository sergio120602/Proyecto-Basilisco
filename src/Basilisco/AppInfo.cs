using System.IO;

namespace Basilisco;

public static class AppInfo
{
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    // Portable: los datos viven junto al .exe, nunca en AppData ni en el registro.
    public static string DataDirectory { get; } =
        Path.Combine(AppContext.BaseDirectory, "basilisco-data");
}
