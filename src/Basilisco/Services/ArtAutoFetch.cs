namespace Basilisco.Services;

/// <summary>
/// Pone una imagen por defecto sin preguntar: busca el juego por su nombre, se queda con el primer resultado
/// y descarga su primera imagen. Si no hay clave, no hay resultados o el servicio falla, devuelve null sin más.
/// </summary>
public static class ArtAutoFetch
{
    public static async Task<string?> FirstImageAsync(ArtProvider provider, AppSettings settings, string name, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(name) || provider.GetKey(settings) is not { Length: > 0 } key)
            return null;

        try
        {
            var games = await provider.SearchAsync(name, key, cancellation);
            foreach (var game in games.Take(2))
            {
                var images = await provider.GetImagesAsync(game, key, cancellation);
                if (images.Count > 0)
                    return await ArtProvider.DownloadAsync(images[0], cancellation);
            }
        }
        catch (Exception ex) when (ex is ArtProviderException or OperationCanceledException or System.Net.Http.HttpRequestException)
        {
        }
        return null;
    }
}
