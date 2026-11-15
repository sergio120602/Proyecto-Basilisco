using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Basilisco.Models;
using Basilisco.Services;
using Microsoft.Win32;

namespace Basilisco;

// Instalar y desinstalar los juegos que no son de Steam: al desinstalar, su carpeta se comprime y se guarda en la carpeta
// de juegos desinstalados (Ajustes) para liberar espacio; al instalar, vuelve a su sitio. Si varios ejecutables comparten la
// carpeta (una colección), van todos juntos. El botón va con los controles del carrusel, a la izquierda de la descarga de metadatos.
public partial class MainWindow
{
    /// <summary>Lo que se guardaría al desinstalar cada juego (null: no se sabe su carpeta) y cuánto ocupa (null: no se pudo medir).</summary>
    readonly Dictionary<string, (InstallSource? Source, long? Size)> _installInfo = [];

    /// <summary>El juego cuyo botón se ve ahora.</summary>
    Game? _installGame;
    CancellationTokenSource? _installMeasure;

    /// <summary>La instalación o desinstalación en marcha (solo una a la vez).</summary>
    StorageJob? _storageJob;

    /// <param name="games">El juego pulsado y, si es una colección, los demás que van con él.</param>
    sealed class StorageJob(Game game, IReadOnlyList<Game> games, bool installing)
    {
        public Game Game { get; } = game;
        public IReadOnlyList<Game> Games { get; } = games;
        public bool Installing { get; } = installing;
        public CancellationTokenSource Cancel { get; } = new();
        public double Progress { get; set; }

        public bool Includes(Game other) => Games.Contains(other);

        /// <summary>«Hollow Knight», o «la colección «Mega Man»».</summary>
        public string Title => Games.Count > 1 && (Game.Archive?.Collection ?? CollectionName) is { } name ? $"la colección «{name}»" : $"«{Game.Name}»";

        public string? CollectionName { get; init; }
    }

    static readonly Brush InstallBrush = Frozen(Color.FromRgb(0x2E, 0x9E, 0x5B));

    static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>La colección del juego (la carpeta que comparte con otros juegos), si se sabe; null si va solo.</summary>
    string? CollectionOf(Game game) =>
        game.Archive is { } archive ? archive.Collection
        : _installInfo.TryGetValue(game.Id, out var info) && info.Source is { IsCollection: true } source ? source.CollectionName
        : null;

    /// <summary>Los juegos guardados en el mismo .zip (los de una colección), el propio incluido.</summary>
    List<Game> SameArchive(GameArchive archive) =>
        Games.Where(game => game.Archive is { } other && string.Equals(other.File, archive.File, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>El botón «Instalar» o «Desinstalar» del juego seleccionado, con lo que ocupa (que se mide aparte).</summary>
    async void ShowInstallButton(Game? game)
    {
        _installMeasure?.Cancel();
        _installMeasure = null;
        _installGame = game;

        if (game is null || game.SteamAppId is not null)
        {
            InstallButton.Visibility = Visibility.Collapsed;
            return;
        }
        if (_storageJob is { } job && job.Includes(game))
        {
            ShowJob(job);
            return;
        }
        InstallProgress.Visibility = Visibility.Collapsed;

        if (game.Archive is { } archive)
        {
            SetInstallLook(green: true);
            InstallText.Text = $"{(archive.Collection is null ? "Instalar" : "Instalar colección")}  ·  {GameStorage.FormatSize(archive.Size)}";
            InstallButton.ToolTip = (archive.Collection is { } collection
                    ? $"Colección «{collection}»: {string.Join(", ", SameArchive(archive).Select(other => other.Name))}.\nSe instalan todos juntos.\n\n"
                    : "") +
                $"Está guardado comprimido ({GameStorage.FormatSize(archive.CompressedSize)}) en:\n{archive.File}\n\nAl instalarlo vuelve a:\n{archive.Source}";
            InstallButton.Visibility = Visibility.Visible;
            return;
        }

        // Sin su archivo (y sin carpeta conocida) no hay nada que desinstalar.
        var hasFile = !GameLauncher.IsLink(game.Path) && File.Exists(GameLauncher.FileOf(game));
        if (!hasFile && game.InstallDir is null)
        {
            InstallButton.Visibility = Visibility.Collapsed;
            return;
        }

        SetInstallLook(green: false);
        InstallButton.Visibility = Visibility.Visible;
        if (!_installInfo.TryGetValue(game.Id, out var info))
        {
            InstallText.Text = "Desinstalar  ·  midiendo…";
            InstallButton.ToolTip = null;
            var measure = _installMeasure = new CancellationTokenSource();
            try
            {
                info = await MeasureAsync(game, measure.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (measure.IsCancellationRequested || _installGame != game)
                return;
            _installInfo[game.Id] = info;
            // Si resulta ser de una colección, sus etiquetas lo dicen («Colección: 'Carpeta'»).
            if (info.Source is { IsCollection: true })
                Chips.ItemsSource = DescribeGame(game);
        }
        ShowUninstall(info);
    }

    /// <summary>En verde y con letras blancas para instalar; como los demás botones para desinstalar.</summary>
    void SetInstallLook(bool green)
    {
        if (green)
        {
            InstallButton.Background = InstallBrush;
            InstallButton.Foreground = Brushes.White;
        }
        else
        {
            InstallButton.ClearValue(BackgroundProperty);
            InstallButton.ClearValue(ForegroundProperty);
        }
    }

    void ShowUninstall((InstallSource? Source, long? Size) info)
    {
        var (source, size) = info;
        if (source is null)
        {
            InstallText.Text = "Desinstalar  ·  elige su carpeta";
            InstallButton.ToolTip = "No se sabe con seguridad cuál es la carpeta de este juego: elígela tú para poder desinstalarlo";
            return;
        }
        var label = source.IsCollection ? "Desinstalar colección" : "Desinstalar";
        InstallText.Text = size is { } bytes ? $"{label}  ·  {GameStorage.FormatSize(bytes)}" : label;
        InstallButton.ToolTip = (source.IsCollection
                ? $"Colección «{source.CollectionName}»: {string.Join(", ", source.Games.Select(member => member.Name))}.\n" +
                  "Comparten carpeta, así que se desinstalan todos juntos.\n\n"
                : "") +
            (source.IsFolder ? $"Su carpeta:\n{source.Path}" : $"Sus archivos, en:\n{source.Folder}") +
            "\n\nAl desinstalarlo se comprime y se guarda en la carpeta de juegos desinstalados (Ajustes) para liberar espacio.";
    }

    Task<(InstallSource? Source, long? Size)> MeasureAsync(Game game, CancellationToken token)
    {
        var library = Games.ToList();
        return Task.Run<(InstallSource?, long?)>(() =>
        {
            var source = GameStorage.FindSource(game, library);
            if (source is null)
                return (null, null);
            try
            {
                return (source, GameStorage.SizeOf(source, token));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (source, null);
            }
        }, token);
    }

    void ShowJob(StorageJob job)
    {
        var percent = (int)Math.Floor(job.Progress * 100);
        InstallText.Text = $"{(job.Installing ? "Instalando" : "Desinstalando")}…  {percent} %   ·   Cancelar";
        InstallButton.ToolTip = job.Installing ? $"Descomprimiendo {job.Title} en su carpeta" : $"Comprimiendo {job.Title} en la carpeta de juegos desinstalados";
        SetInstallLook(green: job.Installing);
        InstallProgress.Visibility = Visibility.Visible;
        InstallProgressFill.Width = Math.Max(0, InstallText.ActualWidth * job.Progress);
        InstallButton.Visibility = Visibility.Visible;
    }

    async void OnInstallClick(object sender, RoutedEventArgs e)
    {
        if (_editing || _installGame is not { } game)
            return;
        if (_storageJob is { } job)
        {
            if (!job.Includes(game))
            {
                ShowToast($"Espera a que termine {job.Title}");
                return;
            }
            var cancel = MessageBox.Show(this, job.Installing
                    ? $"¿Cancelar la instalación de {job.Title}?\n\nSeguirá guardado comprimido."
                    : $"¿Cancelar la desinstalación de {job.Title}?\n\nNo se borrará nada: sigue en su sitio.",
                "Basilisco", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (cancel == MessageBoxResult.Yes)
                job.Cancel.Cancel();
            return;
        }
        if (game.Archive is not null)
            await InstallGameAsync(game);
        else
            await UninstallGameAsync(game);
    }

    // ---- Desinstalar ----

    async Task UninstallGameAsync(Game game)
    {
        if (!_installInfo.TryGetValue(game.Id, out var info))
        {
            InstallText.Text = "Desinstalar  ·  midiendo…";
            info = _installInfo[game.Id] = await MeasureAsync(game, CancellationToken.None);
        }
        if (info.Source is null)
        {
            if (ChooseInstallFolder(game) is not { } chosen)
                return;
            InstallText.Text = "Desinstalar  ·  midiendo…";
            info = _installInfo[game.Id] = (chosen, await Task.Run(() => TrySize(chosen)));
            ShowUninstall(info);
        }
        var source = info.Source!;
        var size = info.Size ?? 0;
        var title = source.IsCollection ? $"la colección «{source.CollectionName}»" : $"«{game.Name}»";

        if (_session is { } session && source.Games.Contains(session.Game))
        {
            MessageBox.Show(this, $"«{session.Game.Name}» está abierto. Ciérralo antes de desinstalar {title}.", "Basilisco",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (source.IsFolder && await Task.Run(() => GameProcesses.IsRunning(source.Path, null)))
        {
            MessageBox.Show(this, $"Algo de la carpeta de {title} está abierto. Ciérralo antes de desinstalarlo.", "Basilisco",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (ArchiveFolder() is not { } archiveFolder)
            return;
        if (GameStorage.IsInside(archiveFolder, source.Folder) && source.IsFolder || GameStorage.IsInside(source.Path, archiveFolder))
        {
            MessageBox.Show(this, "La carpeta de juegos desinstalados no puede estar dentro de la del juego (ni al revés). Elige otra en Ajustes.",
                "Basilisco", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var what = source.IsFolder
            ? $"Su carpeta ({GameStorage.FormatSize(size)}):\n{source.Path}"
            : $"{(source.Files.Count == 1 ? "Su archivo" : $"Sus {source.Files.Count} archivos")} ({GameStorage.FormatSize(size)}), en:\n{source.Folder}";
        var members = source.IsCollection
            ? $"Es una colección: estos {source.Games.Count} juegos comparten la carpeta y se desinstalan juntos:\n" +
              string.Concat(source.Games.Select(member => $"   •  {member.Name}\n")) + "\n"
            : "";
        var notes = "";
        if (SameDrive(archiveFolder, source.Folder))
            notes += "\n\nOjo: la carpeta de juegos desinstalados está en el mismo disco, así que solo se libera lo que se gane al comprimir.";
        if (GameStorage.FreeSpace(archiveFolder) is { } free && free < size)
            notes += $"\n\nOjo: en ese disco quedan {GameStorage.FormatSize(free)} libres. Si al comprimir no cabe, se para sin borrar nada.";

        var answer = MessageBox.Show(this,
            $"¿Desinstalar {title}?\n\n{members}{what}\n\nSe comprime y se guarda en:\n{archiveFolder}\n\n" +
            "Después se borra de su sitio para liberar espacio. Para volver a jugar, pulsa «Instalar» y vuelve tal cual." + notes +
            "\n\n«Sí»: desinstalar.   «No»: elegir tú otra carpeta del juego.",
            source.IsCollection ? "Desinstalar colección" : "Desinstalar juego", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.No)
        {
            if (ChooseInstallFolder(game) is not null)
            {
                _installInfo.Remove(game.Id);
                ShowInstallButton(game);
            }
            return;
        }
        if (answer != MessageBoxResult.Yes)
            return;

        var job = StartJob(new StorageJob(game, source.Games, installing: false) { CollectionName = source.IsCollection ? source.CollectionName : null });
        try
        {
            var (archive, files) = await GameStorage.CompressAsync(game, source, archiveFolder, _settings.ArchiveCompression,
                Progress(job), job.Cancel.Token);
            // Primero se apunta en la biblioteca dónde está guardado (en todos los juegos de la colección); solo después se
            // borra de su sitio. Así, pase lo que pase (aunque se cierre Basilisco a medias), ningún juego se pierde.
            foreach (var member in source.Games)
                member.Archive = Copy(archive);
            Save();
            var leftBehind = await GameStorage.DeleteSourceAsync(source, files);
            var done = source.IsCollection ? $"Colección «{source.CollectionName}» desinstalada ({source.Games.Count} juegos)" : $"«{game.Name}» desinstalado";
            ShowToast(leftBehind == 0
                ? $"{done}: {GameStorage.FormatSize(archive.Size)} liberados ({GameStorage.FormatSize(archive.CompressedSize)} comprimido)"
                : $"{done}, pero {leftBehind} archivos estaban en uso y siguen en su sitio");
        }
        catch (OperationCanceledException)
        {
            ShowToast("Desinstalación cancelada: no se ha borrado nada");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Security.SecurityException)
        {
            MessageBox.Show(this, $"No se pudo desinstalar {title}:\n\n{ex.Message}\n\nNo se ha borrado nada.", "Basilisco",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            EndJob(job);
        }
    }

    /// <summary>Cada juego de una colección guarda su propia copia de los datos del .zip (todas iguales).</summary>
    static GameArchive Copy(GameArchive archive) => new()
    {
        File = archive.File,
        Source = archive.Source,
        IsFolder = archive.IsFolder,
        Collection = archive.Collection,
        Size = archive.Size,
        CompressedSize = archive.CompressedSize,
        Date = archive.Date,
    };

    static long? TrySize(InstallSource source)
    {
        try
        {
            return GameStorage.SizeOf(source, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static bool SameDrive(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    /// <summary>Pide la carpeta del juego. Si vale, se guarda como su carpeta de instalación.</summary>
    InstallSource? ChooseInstallFolder(Game game)
    {
        var start = game.InstallDir ?? (GameLauncher.IsLink(game.Path) ? null : Path.GetDirectoryName(GameLauncher.FileOf(game)));
        var dialog = new OpenFolderDialog { Title = $"Carpeta donde está instalado «{game.Name}»" };
        if (start is not null && Directory.Exists(start))
            dialog.InitialDirectory = start;
        if (dialog.ShowDialog(this) != true)
            return null;

        if (GameStorage.SourceFromFolder(dialog.FolderName, game, Games.ToList()) is not { } source)
        {
            MessageBox.Show(this,
                "Esa carpeta no se puede usar. Tiene que tener dentro el juego y no puede ser la raíz de un disco, el escritorio, " +
                "Program Files, Documentos ni la del propio Basilisco, ni tener juegos de tu biblioteca que ya estén desinstalados.",
                "Basilisco", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        game.InstallDir = source.Path;
        Save();
        return source;
    }

    /// <summary>La carpeta de juegos desinstalados (Ajustes). Si aún no hay, o no se encuentra, la pide.</summary>
    string? ArchiveFolder()
    {
        if (_settings.ArchiveFolder is { } folder && Directory.Exists(folder))
            return folder;
        if (_settings.ArchiveFolder is { } missing)
        {
            var again = MessageBox.Show(this, $"No se encuentra la carpeta de juegos desinstalados:\n{missing}\n\n¿Está conectado el disco? ¿Quieres elegir otra?",
                "Basilisco", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (again != MessageBoxResult.Yes)
                return null;
        }
        else
        {
            MessageBox.Show(this,
                "Elige dónde se guardan los juegos desinstalados. Mejor en otro disco: así se libera el espacio de este.\n\n" +
                "Se puede cambiar cuando quieras en Ajustes.",
                "Juegos desinstalados", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        var dialog = new OpenFolderDialog { Title = "Carpeta para los juegos desinstalados" };
        if (dialog.ShowDialog(this) != true)
            return null;
        _settings.ArchiveFolder = dialog.FolderName;
        _settings.Save();
        return dialog.FolderName;
    }

    // ---- Instalar ----

    async Task InstallGameAsync(Game game)
    {
        if (game.Archive is not { } archive)
            return;
        var members = SameArchive(archive);
        var title = archive.Collection is { } collection && members.Count > 1 ? $"la colección «{collection}»" : $"«{game.Name}»";
        if (!File.Exists(archive.File))
        {
            MessageBox.Show(this, $"No se encuentra {title} comprimido:\n{archive.File}\n\n¿Está conectado ese disco?", "Basilisco",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var target = archive.IsFolder ? archive.Source : Path.GetDirectoryName(archive.Source)!;
        var root = Path.GetPathRoot(target);
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            MessageBox.Show(this, $"No se encuentra el disco donde estaba {title} ({root}). Conéctalo para instalarlo.", "Basilisco",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (GameStorage.FreeSpace(target) is { } free && free < archive.Size)
        {
            MessageBox.Show(this, $"No cabe: {title} necesita {GameStorage.FormatSize(archive.Size)} y en {root} quedan {GameStorage.FormatSize(free)} libres.",
                "Basilisco", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var existed = Directory.Exists(target);
        if (archive.IsFolder && existed && Directory.EnumerateFileSystemEntries(target).Any())
        {
            var overwrite = MessageBox.Show(this, $"Ya hay cosas en la carpeta de {title}:\n{target}\n\n¿Instalarlo encima? Los archivos con el mismo nombre se sustituyen.",
                "Basilisco", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (overwrite != MessageBoxResult.Yes)
                return;
        }

        var job = StartJob(new StorageJob(game, members, installing: true) { CollectionName = archive.Collection });
        try
        {
            await GameStorage.InstallAsync(archive, Progress(job), job.Cancel.Token);
            // Todos los juegos de la colección vuelven a la vez (están en el mismo .zip).
            foreach (var member in members)
                member.Archive = null;
            Save();
            // El .zip, cuando la biblioteca ya lo da por instalado.
            await GameStorage.DeleteArchiveAsync(archive);
            ShowToast(members.Count > 1 ? $"Colección «{archive.Collection}» instalada de nuevo ({members.Count} juegos)" : $"«{game.Name}» instalado de nuevo en su carpeta");
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or UnauthorizedAccessException or InvalidDataException or System.Security.SecurityException)
        {
            // Lo que se había copiado a medias sobra: sigue guardado en el .zip.
            if (archive.IsFolder && !existed)
                await Task.Run(() => TryDeleteFolder(target));
            if (ex is OperationCanceledException)
                ShowToast("Instalación cancelada: sigue guardado comprimido");
            else
                MessageBox.Show(this, $"No se pudo instalar {title}:\n\n{ex.Message}\n\nSigue guardado comprimido en:\n{archive.File}",
                    "Basilisco", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            EndJob(job);
        }
    }

    static void TryDeleteFolder(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Al abrir un juego desinstalado: se ofrece instalarlo (con toda su colección, si es de una).</summary>
    async void OfferInstall(Game game)
    {
        if (_storageJob is { } running && running.Includes(game))
        {
            ShowToast($"Espera a que termine de {(running.Installing ? "instalarse" : "desinstalarse")}");
            return;
        }
        var archive = game.Archive!;
        var others = SameArchive(archive).Where(member => member != game).Select(member => member.Name).ToList();
        var collection = archive.Collection is { } name && others.Count > 0
            ? $"\n\nEs de la colección «{name}»: se instalan también {string.Join(", ", others)}."
            : "";
        var answer = MessageBox.Show(this, $"«{game.Name}» está desinstalado (guardado comprimido en {archive.File}).{collection}\n\n¿Instalarlo ahora?",
            "Basilisco", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;
        if (_storageJob is { } other)
        {
            ShowToast($"Espera a que termine {other.Title}");
            return;
        }
        await InstallGameAsync(game);
    }

    // ---- Común ----

    StorageJob StartJob(StorageJob job)
    {
        _storageJob = job;
        if (_installGame is { } shown && job.Includes(shown))
            ShowJob(job);
        return job;
    }

    IProgress<double> Progress(StorageJob job) => new Progress<double>(fraction =>
    {
        job.Progress = fraction;
        if (_storageJob == job && _installGame is { } shown && job.Includes(shown))
            ShowJob(job);
    });

    void EndJob(StorageJob job)
    {
        _storageJob = null;
        job.Cancel.Dispose();
        foreach (var member in job.Games)
            _installInfo.Remove(member.Id);
        // Las etiquetas cambian («Desinstalado»), y el botón pasa a lo contrario.
        if (_installGame is { } shown && job.Includes(shown) && !IsAddCard(_flow.SelectedIndex))
            ShowDetails(_flow.SelectedIndex);
    }

    /// <summary>Al cerrar Basilisco a medias, se avisa (lo que se esté haciendo se cancela sin perder nada).</summary>
    void OnClosingWhileStoring(object? sender, CancelEventArgs e)
    {
        if (_storageJob is not { } job)
            return;
        var answer = MessageBox.Show(this,
            $"Se está {(job.Installing ? "instalando" : "desinstalando")} {job.Title}. Si cierras Basilisco se cancela " +
            (job.Installing ? "(sigue guardado comprimido)." : "(no se borra nada)."),
            "Basilisco", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK)
        {
            e.Cancel = true;
            return;
        }
        job.Cancel.Cancel();
    }

    /// <summary>Cuántos juegos hay desinstalados y cuánto ocupan comprimidos, para Ajustes (cada .zip una vez, aunque sea de una colección).</summary>
    string ArchiveSummary()
    {
        var archived = Games.Where(game => game.Archive is not null).ToList();
        var size = archived.Select(game => game.Archive!).DistinctBy(archive => archive.File.ToLowerInvariant()).Sum(archive => archive.CompressedSize);
        return archived.Count switch
        {
            0 => "Ningún juego desinstalado.",
            1 => $"1 juego desinstalado: ocupa {GameStorage.FormatSize(size)} comprimido.",
            var n => $"{n} juegos desinstalados: ocupan {GameStorage.FormatSize(size)} comprimidos.",
        };
    }
}
