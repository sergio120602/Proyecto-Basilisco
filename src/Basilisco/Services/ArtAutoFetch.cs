namespace Basilisco.Services;

/// <summary>
/// Pone una imagen por defecto sin preguntar: busca el juego por su nombre, se queda con el primer resultado
/// y descarga su primera imagen (si esa no se deja descargar, la siguiente). Si no hay clave, no hay resultados o el
/// servicio falla, devuelve null sin más.
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
                foreach (var image in images.Take(3))
                {
                    try
                    {
                        // Cada servicio la prepara a su manera (Steam Game Covers, por ejemplo, separa la carátula en sus partes).
                        return await provider.DownloadImageAsync(image, cancellation);
                    }
                    catch (ArtProviderException)
                    {
                    }
                }
                if (images.Count > 0)
                    return null;
            }
        }
        catch (Exception ex) when (ex is ArtProviderException or OperationCanceledException or System.Net.Http.HttpRequestException)
        {
        }
        return null;
    }
}
