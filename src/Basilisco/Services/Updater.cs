using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Basilisco.Services;

/// <summary>Una versión nueva publicada en GitHub.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string DownloadUrl, long Size, string Notes);

/// <summary>
/// Actualizaciones: mira en las Releases de GitHub si hay una versión más nueva, descarga su .exe junto al actual
/// y lo cambia por él. Al ser portable no hay instalador: el .exe nuevo arranca, borra el viejo y sigue usando
/// la misma carpeta basilisco-data.
/// </summary>
public static partial class Updater
{
    const string Repo = "sergio120602/Proyecto-Basilisco";
    const string UpdatedFromArgument = "--updated-from";

    static readonly HttpClient Http = CreateClient();

    public static Version Current { get; } = Version.TryParse(AppInfo.Version, out var version) ? version : new Version(0, 0, 0);

    /// <summary>La versión más nueva publicada, si es más nueva que esta; null si no hay o no se puede saber.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken cancellation)
    {
        try
        {
            // Las versiones 0.x se publican como «pre-release», así que no vale /releases/latest: se mira la lista.
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases?per_page=15");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Http.SendAsync(request, cancellation);
            if (!response.IsSuccessStatusCode)
                return null;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));

            UpdateInfo? best = null;
            foreach (var release in json.RootElement.EnumerateArray())
            {
                if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean() ||
                    release.GetProperty("tag_name").GetString() is not { } tag ||
                    !Version.TryParse(tag.TrimStart('v', 'V'), out var version) || version <= Current ||
                    best is not null && version <= best.Version)
                    continue;

                foreach (var asset in release.GetProperty("assets").EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        continue;
                    best = new UpdateInfo(version, tag, asset.GetProperty("browser_download_url").GetString() ?? "",
                        asset.GetProperty("size").GetInt64(), release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "");
                    break;
                }
            }
            return best;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Descarga el .exe nuevo junto al actual (con otra extensión hasta que esté entero) y devuelve su ruta.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<double> progress, CancellationToken cancellation)
    {
        var folder = Path.GetDirectoryName(ExePath) ?? AppContext.BaseDirectory;
        var path = Path.Combine(folder, $"Basilisco-{update.Tag}-win-x64.exe.download");
        try
        {
            using var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? update.Size;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellation))
            await using (var output = File.Create(path))
            {
                var buffer = new byte[1 << 16];
                long done = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellation)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
                    done += read;
                    if (total > 0)
                        progress.Report((double)done / total);
                }
            }

            if (update.Size > 0 && new FileInfo(path).Length != update.Size)
                throw new IOException("La descarga no ha llegado entera.");
            return path;
        }
        catch
        {
            try { File.Delete(path); }
            catch (IOException) { }
            throw;
        }
    }

    /// <summary>
    /// Pone el .exe descargado en su sitio y lo arranca. Si este .exe se llama como los de las Releases
    /// (Basilisco-v0.4.0-win-x64.exe), el nuevo se queda con su propio nombre; si lo has renombrado, el nuevo
    /// ocupa su lugar con el mismo nombre (así no se rompen tus accesos directos). El viejo lo borra el nuevo al arrancar.
    /// </summary>
    public static void InstallAndRestart(string downloaded, UpdateInfo update)
    {
        var current = ExePath;
        var folder = Path.GetDirectoryName(current)!;
        string target, old;
        if (ReleaseName().IsMatch(Path.GetFileName(current)))
        {
            target = Path.Combine(folder, $"Basilisco-{update.Tag}-win-x64.exe");
            File.Move(downloaded, target, overwrite: true);
            old = current;
        }
        else
        {
            // Un .exe en marcha no se puede sobrescribir, pero sí renombrar.
            old = current + ".old";
            File.Move(current, old, overwrite: true);
            try
            {
                File.Move(downloaded, current);
            }
            catch
            {
                File.Move(old, current);
                throw;
            }
            target = current;
        }

        Process.Start(new ProcessStartInfo(target)
        {
            UseShellExecute = false,
            WorkingDirectory = folder,
            ArgumentList = { UpdatedFromArgument, old },
        });
    }

    /// <summary>Al arrancar tras actualizar: borra el .exe viejo en cuanto se cierre del todo.</summary>
    public static void CleanUpAfterUpdate(string[] args)
    {
        var index = Array.IndexOf(args, UpdatedFromArgument);
        if (index < 0 || index + 1 >= args.Length)
            return;
        var old = args[index + 1];
        if (string.Equals(Path.GetFullPath(old), Path.GetFullPath(ExePath), StringComparison.OrdinalIgnoreCase))
            return;

        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 60; attempt++)
            {
                try
                {
                    if (File.Exists(old))
                        File.Delete(old);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(500);
                }
            }
        });
    }

    static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Basilisco.exe");

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Basilisco", AppInfo.Version));
        return client;
    }

    [GeneratedRegex(@"^Basilisco-v[\d.]+-win-x64\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex ReleaseName();
}
