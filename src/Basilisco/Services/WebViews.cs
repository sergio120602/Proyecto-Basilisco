using System.IO;
using Microsoft.Web.WebView2.Core;

namespace Basilisco.Services;

/// <summary>
/// El navegador de dentro de Basilisco (Edge, el de Windows): uno para todo (Pinterest, el reproductor de YouTube), con la
/// sesión en basilisco-data/webview. Se crea una sola vez: dos con el mismo perfil y distintas opciones no pueden convivir.
/// </summary>
public static class WebViews
{
    public static string ProfileFolder { get; } = Path.Combine(AppInfo.DataDirectory, "webview");

    static Task<CoreWebView2Environment>? _environment;

    public static Task<CoreWebView2Environment> EnvironmentAsync() => _environment ??= CreateAsync();

    static async Task<CoreWebView2Environment> CreateAsync()
    {
        Directory.CreateDirectory(ProfileFolder);
        // La música de la banda sonora tiene que poder empezar al pulsar el disco (sin otro clic dentro del reproductor).
        var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
        return await CoreWebView2Environment.CreateAsync(null, ProfileFolder, options);
    }
}
