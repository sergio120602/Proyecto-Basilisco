using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Xml;
using Basilisco.Models;

namespace Basilisco.Services;

/// <summary>Qué es una imagen descargada: una cara de la caja, el disco o algo que sirve de manual.</summary>
public enum ArtKind { Front, Back, Spine, Disc, Manual }

/// <summary>Imagen de un juego en LaunchBox.</summary>
/// <param name="Reconstructed">Rehecha por la comunidad a partir de otras (se ofrece después de las originales).</param>
/// <param name="Detail">Para las del manual, qué imagen es (póster, fondo, pantalla de título).</param>
public sealed record LaunchBoxImage(ArtKind Kind, string Region, string FileName, bool Reconstructed, string? Detail = null)
{
    public static ArtKind KindOf(Face face) => face switch
    {
        Face.Front => ArtKind.Front,
        Face.Back => ArtKind.Back,
        _ => ArtKind.Spine,
    };
}

/// <summary>Ficha de un juego en LaunchBox, para su manual: se lee aparte porque ocupa mucho (sobre todo la descripción).</summary>
public sealed record LaunchBoxDetails(string Overview, string? Developer, string? Publisher, string? Genres, string? MaxPlayers,
                                      string? Esrb, string? Rating, string? Cooperative);

/// <param name="released">Fecha de lanzamiento (aaaa-mm-dd) o solo el año (aaaa), si se sabe.</param>
public sealed class LaunchBoxGame(int id, string name, string platform, string? released)
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    public string Platform { get; } = platform;
    public string? Released { get; } = released;
    public string? Year => Released is { Length: >= 4 } text ? text[..4] : null;

    /// <summary>La fecha como DateTime (el 1 de enero si solo se sabe el año).</summary>
    public DateTime? ReleaseDate =>
        Released is { Length: 10 } full && DateTime.TryParseExact(full, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date
        : Year is { } year && int.TryParse(year, out var y) && y > 1950 ? new DateTime(y, 1, 1)
        : null;
    public List<string> AlternateNames { get; } = [];
    public List<LaunchBoxImage> Images { get; } = [];
}

/// <summary>
/// Base de datos de juegos de LaunchBox (gamesdb.launchbox-app.com). No tiene API con clave: se descarga entera
/// (Metadata.zip, unos 100 MB), se resume en un índice con solo lo que usa Basilisco (nombres, plataforma, año
/// e imágenes de caja) y se guarda junto al .exe. Después las búsquedas son locales e instantáneas.
/// </summary>
public static class LaunchBoxDatabase
{
    public const string MetadataUrl = "https://gamesdb.launchbox-app.com/Metadata.zip";
    public const string ImagesUrl = "https://images.launchbox-app.com/";

    static readonly string Folder = Path.Combine(AppInfo.DataDirectory, "launchbox");
    static readonly string IndexPath = Path.Combine(Folder, "index.tsv.gz");

    // Fichas de los juegos (descripción, desarrolladora…), para los manuales. Solo se lee cuando hace falta.
    static readonly string DetailsPath = Path.Combine(Folder, "details.tsv.gz");

    // Versión del índice: la 2 añade los discos y las imágenes para el manual, y la 3 las fichas y las capturas.
    // Uno más viejo se rehace al buscar.
    const int IndexVersion = 3;
    static int _loadedVersion;
    static readonly SemaphoreSlim Lock = new(1, 1);
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(20) };

    static Dictionary<int, LaunchBoxGame>? _games;
    static List<(string Normalized, LaunchBoxGame Game)> _names = [];

    // La base de datos cargada ocupa cientos de MB: si no se usa en un rato, se suelta (y se vuelve a cargar sola,
    // en unos segundos, la próxima vez que haga falta).
    static readonly TimeSpan UnloadAfter = TimeSpan.FromMinutes(3);
    static DateTime _lastUse = DateTime.Now;
    static readonly Timer UnloadTimer = new(_ => UnloadIfIdle(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

    static void Touch() => _lastUse = DateTime.Now;

    static void UnloadIfIdle()
    {
        if (_games is null || DateTime.Now - _lastUse < UnloadAfter || !Lock.Wait(0))
            return;
        try
        {
            _games = null;
            _names = [];
            _loadedVersion = 0;
        }
        finally
        {
            Lock.Release();
        }
        GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: true);
    }

    /// <summary>True si ya está descargada (aunque aún no se haya cargado en memoria).</summary>
    public static bool IsDownloaded => File.Exists(IndexPath);

    /// <summary>Fecha de la última descarga, para enseñarla en la interfaz.</summary>
    public static DateTime? DownloadedAt => IsDownloaded ? File.GetLastWriteTime(IndexPath) : null;

    /// <summary>Descarga la base de datos si no está y la carga en memoria. Con forceDownload, la vuelve a descargar.</summary>
    /// <param name="upgrade">Si el índice descargado es de una versión anterior (sin discos), se vuelve a descargar.</param>
    public static async Task EnsureReadyAsync(IProgress<string>? status, CancellationToken cancellation, bool forceDownload = false, bool upgrade = false)
    {
        Touch();
        await Lock.WaitAsync(cancellation);
        try
        {
            if (forceDownload || !IsDownloaded)
            {
                await DownloadAndIndexAsync(status, cancellation);
                _games = null;
            }
            if (_games is null)
            {
                status?.Report("Cargando la base de datos de LaunchBox…");
                await Task.Run(Load, cancellation);
            }
            if (upgrade && _loadedVersion < IndexVersion)
            {
                status?.Report("La base de datos de LaunchBox se actualiza para traer también los discos…");
                await DownloadAndIndexAsync(status, cancellation);
                status?.Report("Cargando la base de datos de LaunchBox…");
                await Task.Run(Load, cancellation);
            }
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>Carga el índice si ya está descargado; nunca descarga (para las imágenes por defecto, que no preguntan).</summary>
    public static async Task<bool> TryLoadAsync(CancellationToken cancellation)
    {
        Touch();
        if (_games is not null)
            return true;
        if (!IsDownloaded)
            return false;
        await EnsureReadyAsync(null, cancellation);
        return _games is not null;
    }

    public static LaunchBoxGame? Find(int id)
    {
        Touch();
        return _games?.GetValueOrDefault(id);
    }

    /// <summary>
    /// Fecha de lanzamiento de un juego por su nombre: solo si el nombre coincide de verdad (o un nombre alternativo),
    /// y mejor si es de la misma plataforma. Así no se pone la fecha de otro juego parecido.
    /// </summary>
    public static DateTime? FindReleaseDate(string name, string? platform)
    {
        Touch();
        if (_games is null)
            return null;

        var query = Normalize(name);
        if (query.Length == 0)
            return null;

        LaunchBoxGame? best = null;
        foreach (var (normalized, game) in _names)
        {
            if (normalized != query || game.ReleaseDate is null)
                continue;
            if (platform is not null && game.Platform == platform)
                return game.ReleaseDate;
            // Sin plataforma que coincida, la primera versión publicada.
            if (best is null || game.ReleaseDate < best.ReleaseDate)
                best = game;
        }
        return best?.ReleaseDate;
    }

    /// <param name="platform">Plataforma de LaunchBox del juego (si se sabe), para ponerlo primero.</param>
    /// <param name="kind">Solo juegos que tengan imágenes de esa clase.</param>
    public static List<LaunchBoxGame> Search(string term, string? platform, ArtKind kind, int max = 40)
    {
        Touch();
        if (_games is null)
            return [];

        var query = Normalize(term);
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return [];

        var best = new Dictionary<LaunchBoxGame, int>();
        foreach (var (name, game) in _names)
        {
            if (!tokens.All(name.Contains) || !game.Images.Any(image => image.Kind == kind))
                continue;

            var score = name == query ? 100 : name.StartsWith(query, StringComparison.Ordinal) ? 60 : 30;
            if (platform is not null && game.Platform == platform)
                score += 40;
            // Los nombres más cortos se parecen más a lo buscado («Halo» antes que «Halo: The Master Chief Collection»).
            score -= Math.Min(20, (name.Length - query.Length) / 4);
            if (!best.TryGetValue(game, out var previous) || score > previous)
                best[game] = score;
        }

        return best.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key.Name).Take(max).Select(entry => entry.Key).ToList();
    }

    /// <summary>Imágenes de una cara del juego, primero las de España y Europa, y las originales antes que las rehechas.</summary>
    public static List<LaunchBoxImage> ImagesOf(LaunchBoxGame game, ArtKind kind) =>
        game.Images.Where(image => image.Kind == kind)
            .OrderBy(image => image.Reconstructed)
            .ThenBy(image => RegionRank(image.Region))
            .ToList();

    static int RegionRank(string region) => region switch
    {
        "Spain" => 0,
        "Europe" => 1,
        "United Kingdom" => 2,
        "World" => 3,
        "North America" => 4,
        "" => 5,
        _ => 6,
    };

    /// <summary>Plataforma de LaunchBox que corresponde a cada consola de Basilisco.</summary>
    public static string? PlatformName(string? basiliscoPlatform) => basiliscoPlatform switch
    {
        "ps1" => "Sony Playstation",
        "ps2" => "Sony Playstation 2",
        "ps3" => "Sony Playstation 3",
        "psp" => "Sony PSP",
        "xbox" => "Microsoft Xbox",
        "x360" => "Microsoft Xbox 360",
        "gb" => "Nintendo Game Boy",
        "gba" => "Nintendo Game Boy Advance",
        "n64" => "Nintendo 64",
        "gc" => "Nintendo GameCube",
        "wii" => "Nintendo Wii",
        "wiiu" => "Nintendo Wii U",
        "nds" => "Nintendo DS",
        "nes" => "Nintendo Entertainment System",
        "snes" => "Super Nintendo Entertainment System",
        "switch" => "Nintendo Switch",
        "arcade" => "Arcade",
        "pc" => "Windows",
        _ => null,
    };

    static async Task DownloadAndIndexAsync(IProgress<string>? status, CancellationToken cancellation)
    {
        Directory.CreateDirectory(Folder);
        var zipPath = Path.Combine(Folder, "Metadata.zip.part");
        try
        {
            using (var response = await Http.GetAsync(MetadataUrl, HttpCompletionOption.ResponseHeadersRead, cancellation))
            {
                if (!response.IsSuccessStatusCode)
                    throw new ArtProviderException($"LaunchBox ha respondido con un error ({(int)response.StatusCode}). Prueba más tarde.");

                var total = response.Content.Headers.ContentLength;
                await using var input = await response.Content.ReadAsStreamAsync(cancellation);
                await using var output = File.Create(zipPath);
                var buffer = new byte[1 << 16];
                long done = 0, lastReport = -1;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellation)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
                    done += read;
                    var mb = done / (1 << 20);
                    if (mb != lastReport)
                    {
                        lastReport = mb;
                        status?.Report(total is { } size
                            ? $"Descargando la base de datos de LaunchBox… {mb} de {size / (1 << 20)} MB (solo la primera vez)"
                            : $"Descargando la base de datos de LaunchBox… {mb} MB (solo la primera vez)");
                    }
                }
            }

            status?.Report("Preparando la base de datos de LaunchBox… (puede tardar un minuto)");
            await Task.Run(() => BuildIndex(zipPath, status, cancellation), cancellation);
        }
        catch (HttpRequestException)
        {
            throw new ArtProviderException("No se ha podido descargar la base de datos de LaunchBox. Comprueba tu conexión a internet.");
        }
        finally
        {
            // El zip ya no hace falta: el índice ocupa mucho menos.
            try { File.Delete(zipPath); }
            catch (IOException) { }
        }
    }

    /// <summary>Lee Metadata.xml del zip sin cargarlo entero y escribe el índice (gzip, una línea por dato).</summary>
    static void BuildIndex(string zipPath, IProgress<string>? status, CancellationToken cancellation)
    {
        Directory.CreateDirectory(Folder);
        var tempIndex = IndexPath + ".tmp";
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            var entry = zip.GetEntry("Metadata.xml") ?? throw new ArtProviderException("La descarga de LaunchBox no tiene el formato esperado.");
            using var xml = XmlReader.Create(entry.Open(), new XmlReaderSettings { CheckCharacters = false, DtdProcessing = DtdProcessing.Ignore, IgnoreWhitespace = true });
            using var file = File.Create(tempIndex);
            using var gzip = new GZipStream(file, CompressionLevel.Optimal);
            using var writer = new StreamWriter(gzip, new UTF8Encoding(false));
            writer.WriteLine($"V\t{IndexVersion}\t");
            using var detailsFile = File.Create(DetailsPath + ".tmp");
            using var detailsGzip = new GZipStream(detailsFile, CompressionLevel.Optimal);
            using var details = new StreamWriter(detailsGzip, new UTF8Encoding(false));

            var games = 0;
            xml.MoveToContent();
            xml.Read();
            while (!xml.EOF)
            {
                cancellation.ThrowIfCancellationRequested();
                if (xml.NodeType != XmlNodeType.Element || xml.Depth != 1)
                {
                    xml.Read();
                    continue;
                }

                var kind = xml.Name;
                var fields = ReadFields(xml);
                switch (kind)
                {
                    case "Game" when fields.TryGetValue("DatabaseID", out var id) && fields.TryGetValue("Name", out var name):
                        // Fecha completa si la hay (2013-09-20), o solo el año.
                        var year = fields.GetValueOrDefault("ReleaseDate") is { Length: >= 10 } date ? date[..10]
                                 : fields.GetValueOrDefault("ReleaseYear") ?? "";
                        writer.WriteLine($"G\t{id}\t{Clean(fields.GetValueOrDefault("Platform"))}\t{year}\t{Clean(name)}");
                        if (fields.GetValueOrDefault("Overview") is { Length: > 0 } overview)
                        {
                            // Los saltos de párrafo se guardan como \n para que cada juego ocupe una línea.
                            var text = overview.Replace("\r", "").Replace("\t", " ").Replace("\n", "\\n");
                            details.WriteLine(string.Join('\t', id, Clean(fields.GetValueOrDefault("Developer")), Clean(fields.GetValueOrDefault("Publisher")),
                                Clean(fields.GetValueOrDefault("Genres")), Clean(fields.GetValueOrDefault("MaxPlayers")), Clean(fields.GetValueOrDefault("ESRB")),
                                Clean(fields.GetValueOrDefault("CommunityRating")), Clean(fields.GetValueOrDefault("Cooperative")), text));
                        }
                        if (++games % 20000 == 0)
                            status?.Report($"Preparando la base de datos de LaunchBox… {games:N0} juegos");
                        break;
                    case "GameAlternateName" when fields.TryGetValue("DatabaseID", out var id) && fields.TryGetValue("AlternateName", out var alt):
                        writer.WriteLine($"A\t{id}\t{Clean(alt)}");
                        break;
                    case "GameImage" when fields.TryGetValue("DatabaseID", out var id) && fields.TryGetValue("FileName", out var fileName)
                                          && ImageKind(fields.GetValueOrDefault("Type")) is { } image:
                        writer.WriteLine($"I\t{id}\t{image}\t{Clean(fields.GetValueOrDefault("Region"))}\t{Clean(fileName)}");
                        break;
                }
            }
        }
        File.Move(DetailsPath + ".tmp", DetailsPath, overwrite: true);
        File.Move(tempIndex, IndexPath, overwrite: true);
    }

    /// <summary>
    /// El juego de LaunchBox que corresponde a uno de Basilisco: el que se llama igual (mejor de la misma consola),
    /// o el primero de la búsqueda si su nombre empieza igual. Null si no se encuentra.
    /// </summary>
    public static LaunchBoxGame? FindGame(string name, string? platform)
    {
        Touch();
        if (_games is null)
            return null;
        var query = Normalize(name);
        var exact = _names.Where(entry => entry.Normalized == query).Select(entry => entry.Game).Distinct().ToList();
        if (exact.Count > 0)
            return exact.FirstOrDefault(game => game.Platform == platform) ?? exact[0];
        return Search(name, platform, ArtKind.Front, max: 1).FirstOrDefault(game => Normalize(game.Name).StartsWith(query, StringComparison.Ordinal));
    }

    /// <summary>La ficha de un juego (se busca en el archivo de fichas, que no se tiene en memoria). Null si no tiene.</summary>
    public static LaunchBoxDetails? Details(int id)
    {
        if (!File.Exists(DetailsPath))
            return null;
        var prefix = id.ToString(CultureInfo.InvariantCulture) + "\t";
        using var file = File.OpenRead(DetailsPath);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            var parts = line.Split('\t');
            if (parts.Length < 9)
                return null;
            string? Field(int index) => parts[index].Length > 0 ? parts[index] : null;
            return new LaunchBoxDetails(parts[8].Replace("\\n", "\n"), Field(1), Field(2), Field(3), Field(4), Field(5), Field(6), Field(7));
        }
        return null;
    }

    /// <summary>Lee los hijos de texto de un elemento (Name, Platform…) y deja el lector en el siguiente.</summary>
    static Dictionary<string, string> ReadFields(XmlReader xml)
    {
        var fields = new Dictionary<string, string>();
        if (xml.IsEmptyElement)
        {
            xml.Read();
            return fields;
        }

        var depth = xml.Depth;
        xml.Read();
        while (!xml.EOF && xml.Depth > depth)
        {
            if (xml.NodeType == XmlNodeType.Element && xml.Depth == depth + 1)
            {
                var name = xml.Name;
                if (xml.IsEmptyElement)
                {
                    // Campo vacío (<Genres />): hay que avanzar a mano, ReadElementContentAsString no se usa aquí.
                    fields[name] = "";
                    xml.Read();
                }
                else
                {
                    fields[name] = xml.ReadElementContentAsString();
                }
                continue;
            }
            xml.Read();
        }
        if (xml.NodeType == XmlNodeType.EndElement)
            xml.Read();
        return fields;
    }

    /// <summary>
    /// Solo interesan las imágenes de la caja (F portada, B contraportada, S lomo, con R si son rehechas), el disco (D)
    /// y las que pueden hacer de manual (M): el póster de publicidad (MP), un fondo del juego (MF) y la pantalla de título (MT).
    /// </summary>
    static string? ImageKind(string? type) => type switch
    {
        "Box - Front" => "F",
        "Box - Front - Reconstructed" => "FR",
        "Box - Back" => "B",
        "Box - Back - Reconstructed" => "BR",
        "Box - Spine" => "S",
        "Disc" => "D",
        "Advertisement Flyer - Front" => "MP",
        "Fanart - Background" => "MF",
        "Screenshot - Game Title" => "MT",
        "Screenshot - Gameplay" => "MG",
        _ => null,
    };

    static string Clean(string? text) => (text ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

    static void Load()
    {
        var games = new Dictionary<int, LaunchBoxGame>();
        var version = 1;
        using (var file = File.OpenRead(IndexPath))
        using (var gzip = new GZipStream(file, CompressionMode.Decompress))
        using (var reader = new StreamReader(gzip, Encoding.UTF8))
        {
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var parts = line.Split('\t');
                if (parts[0] == "V")
                {
                    version = int.TryParse(parts.ElementAtOrDefault(1), out var v) ? v : 1;
                    continue;
                }
                if (parts.Length < 3 || !int.TryParse(parts[1], out var id))
                    continue;

                switch (parts[0])
                {
                    case "G" when parts.Length >= 5:
                        games[id] = new LaunchBoxGame(id, parts[4], parts[2], parts[3].Length > 0 ? parts[3] : null);
                        break;
                    case "A" when games.TryGetValue(id, out var game):
                        game.AlternateNames.Add(parts[2]);
                        break;
                    case "I" when parts.Length >= 5 && games.TryGetValue(id, out var game):
                        var (kind, detail) = parts[2] switch
                        {
                            "F" or "FR" => (ArtKind.Front, null),
                            "B" or "BR" => (ArtKind.Back, null),
                            "D" => (ArtKind.Disc, null),
                            "MP" => (ArtKind.Manual, "Póster"),
                            "MF" => (ArtKind.Manual, "Fondo"),
                            "MT" => (ArtKind.Manual, "Pantalla de título"),
                            "MG" => (ArtKind.Manual, "Captura"),
                            _ => (ArtKind.Spine, (string?)null),
                        };
                        game.Images.Add(new LaunchBoxImage(kind, parts[3], parts[4], parts[2] is "FR" or "BR", detail));
                        break;
                }
            }
        }

        // Solo los juegos con imágenes de caja; cada nombre (y nombre alternativo) normalizado una vez.
        var names = new List<(string, LaunchBoxGame)>();
        foreach (var game in games.Values.Where(game => game.Images.Count > 0))
        {
            names.Add((Normalize(game.Name), game));
            foreach (var alt in game.AlternateNames)
                names.Add((Normalize(alt), game));
        }
        _names = names;
        _games = games;
        _loadedVersion = version;
    }

    /// <summary>Minúsculas, sin tildes ni signos: «Pokémon: Edición Roja» → «pokemon edicion roja».</summary>
    public static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var space = true;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                space = false;
            }
            else if (!space)
            {
                builder.Append(' ');
                space = true;
            }
        }
        return builder.ToString().TrimEnd();
    }
}
