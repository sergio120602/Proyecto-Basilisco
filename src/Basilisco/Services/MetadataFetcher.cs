using System.IO;
using Basilisco.Models;

namespace Basilisco.Services;

/// <summary>Cada parte de un juego que se puede descargar sola.</summary>
public enum MetadataPart { Front, Back, Spine, Inside, Disc, Manual }

/// <summary>Servicios de los que se sacan las imágenes de forma automática.</summary>
public enum MetadataSource { LaunchBox, SteamGridDb, SpineArchive, SteamGameCovers }

/// <summary>
/// Descarga automática de las imágenes de un juego, sin preguntar: para cada parte se prueban los servicios en su orden
/// de prioridad y se queda con la primera imagen que se encuentre.
/// <list type="bullet">
/// <item>Portada: en las cajas rectangulares, LaunchBox, SteamGridDB y Steam Game Covers (en PC, SteamGridDB antes que
/// LaunchBox); en las cuadradas, siempre SteamGridDB primero (sus carátulas de 1024 × 1024 son las que encajan).</item>
/// <item>Contraportada: LaunchBox, SteamGridDB y Steam Game Covers.</item>
/// <item>Lomo: LaunchBox, The Spine Archive y Steam Game Covers (en PC, The Spine Archive, Steam Game Covers y LaunchBox).
/// Los lomos de The Spine Archive son de Switch: en las demás consolas se les quita la franja con el logotipo de Nintendo.</item>
/// <item>Interior de la caja: SteamGridDB. Disco y manual: LaunchBox.</item>
/// </list>
/// </summary>
public sealed class MetadataFetcher(AppSettings settings)
{
    readonly SteamGridDbProvider _grid = new();
    readonly SteamGridDbProvider _gridPortrait = new(CoverFormat.Portrait);
    readonly SteamGridDbProvider _gridInside = new(inside: true);
    readonly LaunchBoxProvider _launchBoxFront = new(ArtKind.Front);
    readonly LaunchBoxProvider _launchBoxBack = new(ArtKind.Back);
    readonly LaunchBoxProvider _launchBoxSpine = new(ArtKind.Spine);
    readonly LaunchBoxProvider _launchBoxDisc = new(ArtKind.Disc);
    readonly LaunchBoxProvider _launchBoxManual = new(ArtKind.Manual);
    readonly SpineArchiveProvider _spineArchive = new();
    bool _spineArchiveReady;

    // Una carátula de Steam Game Covers trae portada, lomo y contraportada a la vez: se guarda la última para no
    // descargarla otra vez al buscar las demás partes del mismo juego.
    (string Name, CoverParts Parts)? _lastCovers;

    /// <summary>Los juegos sin consola se tratan como de PC (son ejecutables o accesos directos del ordenador).</summary>
    public static bool IsPc(string? platform) => platform is null or "pc";

    /// <summary>De dónde se saca cada parte, por orden.</summary>
    public static MetadataSource[] Priority(MetadataPart part, string? platform, CoverFormat format) => part switch
    {
        MetadataPart.Front => IsPc(platform) || format == CoverFormat.Square
            ? [MetadataSource.SteamGridDb, MetadataSource.LaunchBox, MetadataSource.SteamGameCovers]
            : [MetadataSource.LaunchBox, MetadataSource.SteamGridDb, MetadataSource.SteamGameCovers],
        MetadataPart.Back => [MetadataSource.LaunchBox, MetadataSource.SteamGridDb, MetadataSource.SteamGameCovers],
        MetadataPart.Spine => IsPc(platform)
            ? [MetadataSource.SpineArchive, MetadataSource.SteamGameCovers, MetadataSource.LaunchBox]
            : [MetadataSource.LaunchBox, MetadataSource.SpineArchive, MetadataSource.SteamGameCovers],
        MetadataPart.Inside => [MetadataSource.SteamGridDb],
        _ => [MetadataSource.LaunchBox],
    };

    public static MetadataPart PartOf(Face face) => face switch
    {
        Face.Front => MetadataPart.Front,
        Face.Back => MetadataPart.Back,
        _ => MetadataPart.Spine,
    };

    /// <summary>Las partes que le faltan a un juego (en el formato que usa).</summary>
    public static List<MetadataPart> Missing(Game game)
    {
        var missing = new List<MetadataPart>();
        foreach (var face in new[] { Face.Front, Face.Back, Face.Spine })
        {
            if (ImageStore.GetPath(game, game.Format, face) is null)
                missing.Add(PartOf(face));
        }
        if (ImageStore.GetExtraPath(game, GameExtra.Inside) is null)
            missing.Add(MetadataPart.Inside);
        if (ImageStore.GetExtraPath(game, GameExtra.Disc) is null)
            missing.Add(MetadataPart.Disc);
        if (ImageStore.GetExtraPath(game, GameExtra.Manual) is null)
            missing.Add(MetadataPart.Manual);
        return missing;
    }

    /// <summary>
    /// Deja listas las bases de datos que hacen falta: la de LaunchBox (se descarga la primera vez, con los discos y
    /// los manuales) y el catálogo de The Spine Archive. Si alguna falla, se sigue con los demás servicios.
    /// </summary>
    public async Task PrepareAsync(IProgress<string> status, CancellationToken cancellation)
    {
        try
        {
            await LaunchBoxDatabase.EnsureReadyAsync(status, cancellation, upgrade: true);
        }
        catch (Exception ex) when (ex is ArtProviderException or IOException or System.Net.Http.HttpRequestException)
        {
        }
        await PrepareSpineArchiveAsync(status, cancellation);
    }

    async Task PrepareSpineArchiveAsync(IProgress<string> status, CancellationToken cancellation)
    {
        if (_spineArchiveReady)
            return;
        try
        {
            await _spineArchive.PrepareAsync(status, cancellation);
            _spineArchiveReady = true;
        }
        catch (Exception ex) when (ex is ArtProviderException or IOException)
        {
        }
    }

    /// <summary>De qué servicio salió la última imagen encontrada.</summary>
    public MetadataSource? LastSource { get; private set; }

    /// <summary>Busca una parte en sus servicios, por orden. Devuelve la ruta de la imagen (temporal) o null.</summary>
    public async Task<string?> FetchAsync(string name, string? platform, CoverFormat format, MetadataPart part, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        LastSource = null;
        foreach (var source in Priority(part, platform, format))
        {
            cancellation.ThrowIfCancellationRequested();
            if (await FromAsync(source, name, platform, format, part, cancellation) is { } path)
            {
                LastSource = source;
                return path;
            }
        }
        return null;
    }

    async Task<string?> FromAsync(MetadataSource source, string name, string? platform, CoverFormat format, MetadataPart part,
                                  CancellationToken cancellation)
    {
        switch (source)
        {
            case MetadataSource.LaunchBox:
            {
                var provider = part switch
                {
                    MetadataPart.Front => _launchBoxFront,
                    MetadataPart.Back => _launchBoxBack,
                    MetadataPart.Spine => _launchBoxSpine,
                    MetadataPart.Disc => _launchBoxDisc,
                    MetadataPart.Manual => _launchBoxManual,
                    _ => null,
                };
                if (provider is null || !LaunchBoxDatabase.IsDownloaded)
                    return null;
                provider.PlatformHint = platform;
                return await ArtAutoFetch.FirstImageAsync(provider, settings, name, cancellation);
            }

            case MetadataSource.SteamGridDb:
            {
                // SteamGridDB no tiene contraportadas: solo carátulas (portadas) e imágenes grandes para el interior.
                var provider = part switch
                {
                    MetadataPart.Front => format == CoverFormat.Portrait ? _gridPortrait : _grid,
                    MetadataPart.Inside => _gridInside,
                    _ => null,
                };
                return provider is null ? null : await ArtAutoFetch.FirstImageAsync(provider, settings, name, cancellation);
            }

            case MetadataSource.SpineArchive:
            {
                if (part != MetadataPart.Spine)
                    return null;
                await PrepareSpineArchiveAsync(new Progress<string>(), cancellation);
                if (await ArtAutoFetch.FirstImageAsync(_spineArchive, settings, name, cancellation) is not { } spine)
                    return null;
                // Son lomos de cajas de Switch: en los juegos de Switch el logotipo es el suyo y se deja.
                return platform == "switch" ? spine : await Task.Run(() => SpineArchiveProvider.HideConsoleLogo(spine), cancellation);
            }

            default:
            {
                var kind = part switch
                {
                    MetadataPart.Front => ArtKind.Front,
                    MetadataPart.Back => ArtKind.Back,
                    MetadataPart.Spine => ArtKind.Spine,
                    _ => (ArtKind?)null,
                };
                if (kind is not { } wanted)
                    return null;
                if (_lastCovers is { } last && last.Name == name && last.Parts.Get(wanted) is { } cached && File.Exists(cached))
                    return cached;

                // Las partes se dejan tal cual salen de la carátula, sin encuadrarlas: se ajustan a mano si hace falta.
                var covers = new SteamGameCoversProvider(wanted);
                var path = await ArtAutoFetch.FirstImageAsync(covers, settings, name, cancellation);
                if (covers.LastParts is { } parts)
                    _lastCovers = (name, parts);
                return path;
            }
        }
    }
}
