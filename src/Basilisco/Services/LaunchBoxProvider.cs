using System.Globalization;
using Basilisco.Models;

namespace Basilisco.Services;

/// <summary>
/// LaunchBox Games Database: fotos reales de las cajas (portada, contraportada y lomo) de casi 200 000 juegos,
/// de varias regiones. No necesita clave; la primera vez descarga su base de datos (ver <see cref="LaunchBoxDatabase"/>).
/// </summary>
public sealed class LaunchBoxProvider(Face face) : ArtProvider
{
    const string NoKey = "-";

    /// <summary>Consola del juego que se está buscando (de Basilisco), para poner primero los de esa plataforma.</summary>
    public string? PlatformHint { get; set; }

    public override string Name => "LaunchBox";

    public override string ImageKind => face switch
    {
        Face.Front => "portada",
        Face.Back => "contraportada",
        _ => "lomo",
    };

    public override bool RequiresKey => false;
    public override string KeyHelp => "";
    public override string KeyUrl => "https://gamesdb.launchbox-app.com/";
    public override string? GetKey(AppSettings settings) => NoKey;
    public override void SetKey(AppSettings settings, string? key) { }

    public override Task PrepareAsync(IProgress<string> status, CancellationToken cancellation) =>
        LaunchBoxDatabase.EnsureReadyAsync(status, cancellation);

    public override string? MaintenanceLabel => "Actualizar base de datos";

    public override Task RunMaintenanceAsync(IProgress<string> status, CancellationToken cancellation) =>
        LaunchBoxDatabase.EnsureReadyAsync(status, cancellation, forceDownload: true);

    public override async Task<List<ArtGame>> SearchAsync(string term, string key, CancellationToken cancellation)
    {
        // Para las imágenes por defecto no se descarga nada sin preguntar: solo se usa si ya estaba descargada.
        if (!await LaunchBoxDatabase.TryLoadAsync(cancellation))
            throw new ArtProviderException("La base de datos de LaunchBox aún no está descargada.");

        var platform = LaunchBoxDatabase.PlatformName(PlatformHint);
        return await Task.Run(() => LaunchBoxDatabase.Search(term, platform, face)
            .Select(game => new ArtGame(
                game.Id.ToString(CultureInfo.InvariantCulture), game.Name,
                string.Join(" · ", new[] { game.Platform, game.Year }.Where(text => !string.IsNullOrEmpty(text)))))
            .ToList(), cancellation);
    }

    public override async Task<List<ArtImage>> GetImagesAsync(ArtGame game, string key, CancellationToken cancellation)
    {
        if (!await LaunchBoxDatabase.TryLoadAsync(cancellation) || !int.TryParse(game.Id, out var id))
            return [];

        if (LaunchBoxDatabase.Find(id) is not { } match)
            return [];

        return LaunchBoxDatabase.ImagesOf(match, face)
            .Select(image =>
            {
                var url = LaunchBoxDatabase.ImagesUrl + image.FileName;
                var region = image.Region.Length > 0 ? RegionName(image.Region) : "Sin región";
                return new ArtImage(url, url, 0, 0, image.Reconstructed ? $"{region} · rehecha" : region);
            })
            .ToList();
    }

    static string RegionName(string region) => region switch
    {
        "Spain" => "España",
        "Europe" => "Europa",
        "North America" => "Norteamérica",
        "United Kingdom" => "Reino Unido",
        "World" => "Mundial",
        "Japan" => "Japón",
        "Germany" => "Alemania",
        "France" => "Francia",
        "Italy" => "Italia",
        "Australia" => "Australia",
        "Brazil" => "Brasil",
        "Korea" => "Corea",
        "China" => "China",
        _ => region,
    };
}
