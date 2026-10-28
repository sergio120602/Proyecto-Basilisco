using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using Basilisco.Carousel;
using Basilisco.Customization;
using Basilisco.Models;
using Basilisco.Services;
using Microsoft.Win32;

namespace Basilisco;

public partial class MainWindow : Window
{
    // El logo se pinta hasta 180 px de ancho; con margen para pantallas con escalado.
    const int FilterIconDecodeWidth = 480;

    readonly LibraryStore _store = new();
    readonly AppSettings _settings = AppSettings.Load();
    readonly SteamGridDbProvider _steamGridDb = new();
    readonly SteamGridDbProvider _steamGridDbPortrait = new(CoverFormat.Portrait);
    readonly SteamGridDbProvider _steamGridDbHeroes = new(heroes: true);
    readonly SteamGridDbProvider _steamGridDbInside = new(inside: true);
    readonly SteamGridDbProvider _steamGridDbLogos = new(logos: true);
    readonly SteamGridDbProvider _steamGridDbManual = new(Models.CoverFormat.Portrait, manual: true);
    readonly SteamGridDbProvider _steamGridDbCartridge = new(cartridge: true);
    readonly LaunchBoxPlatformProvider _launchBoxPlatforms = new();
    readonly GamesDatabaseManualsProvider _manuals = new();
    readonly TheGamesDbProvider _theGamesDb = new();
    readonly TheGamesDbProvider _theGamesDbBanners = new(banners: true);
    readonly LaunchBoxProvider _launchBoxFront = new(Face.Front);
    readonly LaunchBoxProvider _launchBoxBack = new(Face.Back);
    readonly LaunchBoxProvider _launchBoxSpine = new(Face.Spine);
    readonly SpineArchiveProvider _spineArchive = new();
    readonly LaunchBoxProvider _launchBoxDisc = new(ArtKind.Disc);
    readonly LaunchBoxProvider _launchBoxManual = new(ArtKind.Manual);
    readonly Library _library;
    readonly CoverFlow _flow;

    // Lo seleccionado arriba: una consola, un filtro o ninguno de los dos («Todos los juegos»).
    string? _activePlatform;
    GameFilter? _activeFilter;
    // …o un miembro de la familia de Steam (sus juegos, en el menú «Steam familiar»).
    FamilyMember? _activeMember;

    /// <summary>Nombre de lo que se está viendo, para los textos («PS2», «RPG»…), o null en «Todos los juegos».</summary>
    string? ActiveViewName => _activeFilter?.Name ?? Platforms.Find(_activePlatform)?.Name ?? _activeMember?.Name ??
                              (_activePlayers is { } players ? PlayersName(players) : null);

    /// <summary>Juegos que se ven en el carrusel con el filtro actual, en el mismo orden.</summary>
    List<Game> _visibleGames = [];

    // Arrastrar la caja seleccionada para girarla.
    const double DragThreshold = 6;
    const double DragDegreesPerPixel = 0.45;
    Point? _dragStart;
    (double Yaw, double Pitch) _dragFrom;
    bool _dragged;

    int _wheelDelta;
    bool _fullScreen;

    // Mandos de Xbox, PS5 y Switch Pro.
    readonly GamepadService _gamepad = new();
    bool _padMode;
    bool _injectingKey;
    const double StickYawSpeed = 220;
    const double StickPitchSpeed = 110;

    public MainWindow()
    {
        ThemeManager.Apply(_settings.Theme);
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.SetTitleBar(this, ThemeManager.IsDark);
        ThemeManager.Changed += OnThemeChanged;
        // «Como Windows»: si cambias el modo de Windows con Basilisco abierto, cambia también.
        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        Closed += (_, _) => SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
        UpdateNightButton();
        VersionText.Text = $"v{AppInfo.Version}";

        _library = _store.Load();
        SteamAchievements.MySteamId = MySteamId();
        RemoveOldFamilyFilter();
        _flow = new CoverFlow(Viewport);
        _flow.SelectedIndexChanged += ShowDetails;
        RefreshFilterTabs();
        RefreshCarousel(0);

        Customizer.Saved += ApplyCustomization;
        Customizer.ArtSearchRequested += OpenArtSearch;
        Customizer.AutoFetch = AutoFetchAsync;
        FormatEditor.Applied += ApplyFormat;
        FilterEditor.Saved += ApplyFilter;
        FilterEditor.DeleteRequested += DeleteFilter;
        ConsoleEditor.LaunchBoxRequested += platform => ArtSearch.Open(_launchBoxPlatforms, _settings, platform.Name, ConsoleEditor.SetImage);
        ConsoleEditor.Closed += () => Focus();
        FilterEditor.LogoSearchRequested += name => ArtSearch.Open(_steamGridDbLogos, _settings, name, FilterEditor.SetImage);
        FolderImporter.ImportRequested += ApplyFolderImport;
        SteamImporter.ImportRequested += ApplySteamImport;
        LauncherImporter.ImportRequested += ApplyLauncherImport;
        LauncherImporter.Closed += () => { if (!IsPanelOpen) Focus(); };
        var warmSettings = _settings;
        _ = Task.Run(() => Emulators.Warm(warmSettings));
        GameLauncher.EmulatorOf = id => Emulators.Find(id) is { } emulator
            ? (Emulators.FindExe(emulator, _settings), _settings.EmulatorArguments.GetValueOrDefault(id) ?? emulator.DefaultArguments)
            : (null, "\"{game}\"");
        SteamImporter.FamilyLoaded += ApplyFamily;
        ConsoleEditor.Changed += RefreshFilterTabs;
        ThemeEditor.Saved += ApplyTheme;
        ThemeEditor.ModeChanged += SetTheme;
        ThemeEditor.PerformanceChanged += ApplyPerformance;
        ThemeEditor.OptimizeRequested += () => _ = OptimizeDataAsync();
        CaseView.PlayRequested += Launch;
        CaseView.Closed += () => Focus();
        FilterStrip.Closed += () =>
        {
            RefreshFilterTabs();
            if (!IsPanelOpen)
                Focus();
        };
        CaseView.ManualRequested += game => _ = OpenManualAsync(game);
        Customizer.ManualReadRequested += (path, game, title) => ManualReader.Open(path, game, title, _padMode);
        Customizer.ImageManualReadRequested += OpenImageManual;
        Customizer.SoundtrackChanged += _ => Save();
        CaseView.Volume = _settings.SoundtrackVolume;
        CaseView.YouTubeKey = _settings.YouTubeApiKey;
        CaseView.YouTubeKeyChanged += key =>
        {
            _settings.YouTubeApiKey = key;
            _settings.Save();
        };
        CaseView.SoundtrackEdited += _ => Save();
        CaseView.VolumeChanged += volume => _settings.SoundtrackVolume = volume;
        CaseView.Closed += () => _settings.Save();
        Customizer.ImageManualRemoveRequested += RemoveImageManual;
        Customizer.PinterestRequested += (game, name) => OpenPinterest(game, name.Length > 0 ? name : game.Name);
        ManualReader.PinterestRequested += game => OpenPinterest(game, game.Name);
        ManualReader.Edited += Save;
        PinterestPicker.Saved += pages => { if (_pinterestGame is { } game) _ = SaveImageManualAsync(game, pages); };
        PinterestPicker.Closed += () =>
        {
            if (ManualReader.IsOpen) ManualReader.Focus();
            else if (Customizer.IsOpen) Customizer.Focus();
            else Focus();
        };
        ManualReader.Closed += () =>
        {
            if (Customizer.IsOpen) Customizer.Focus();
            else if (CaseView.IsOpen) CaseView.Focus();
            else Focus();
        };
        PlatformPicker.Saved += ApplyPlatforms;

        Viewport.SizeChanged += (_, _) => UpdateOverlayLayout();
        Root.SizeChanged += (_, _) => UpdateUiScale();
        HeaderBar.SizeChanged += (_, _) => UpdateOverlayLayout();
        Activated += async (_, _) =>
        {
            CaseView.SetPaused(false);
            if (BackgroundVideo.Source is not null && _settings.AnimatedBackgrounds) BackgroundVideo.Play();
            EndPlaySession(returned: true);
            if (DateTime.Now - _lastSteamSync > TimeSpan.FromSeconds(30))
                await SyncSteamPlaytimeAsync(online: false);
        };
        Deactivated += (_, _) =>
        {
            BackgroundVideo.Pause();
            // Mientras juegas, el disco que gira no se ve: no se gasta gráfica en él.
            if (CaseView.IsPlaying)
                CaseView.SetPaused(true);
        };
        ZoomSlider.Value = Math.Clamp(_settings.Zoom, CoverFlow.MinZoom, CoverFlow.MaxZoom);
        _flow.Shift = _settings.CarouselShift;
        ShiftSlider.Value = _flow.Shift;
        AutoShiftToggle.IsChecked = _settings.CarouselAutoShift;
        _flow.Aligned = _settings.CarouselAligned;
        AlignToggle.IsChecked = _settings.CarouselAligned;
        _flow.Quality = _settings.QualityScale;
        BoxModel.Reflections = _settings.Reflections;
        ShiftSlider.IsEnabled = !_settings.CarouselAutoShift;
        ApplyZoom(ZoomSlider.Value, save: false);

        _gamepad.IsActive = () => IsActive && WindowState != WindowState.Minimized;
        // El mando solo cuenta con Basilisco delante: mientras juegas, lo que pulses es para el juego (antes, un B en el
        // juego cerraba la caja abierta, y las flechas movían el carrusel por detrás).
        _gamepad.Pressed += action => { if (IsActive) OnPadPressed(action); };
        _gamepad.Navigate += direction => { if (IsActive) OnPadNavigate(direction); };
        _gamepad.RightStick += (x, y) => { if (IsActive) OnPadRightStick(x, y); };
        _gamepad.NavigateVertical += direction => { if (IsActive) OnPadVertical(direction); };
        _gamepad.Connected += (name, style) =>
        {
            SetPadMode(true);
            ShowToast($"Mando conectado: {name}");
        };
        _gamepad.Disconnected += () => ShowToast("Mando desconectado");
        Closed += (_, _) =>
        {
            _gamepad.Dispose();
            // Si cierras Basilisco jugando, la partida queda guardada: al volver a abrirlo se sigue contando.
            _session?.ToSaved().Save();
        };
        _playTimer.Tick += async (_, _) => await PollPlaySessionAsync();
        Loaded += async (_, _) =>
        {
            // Las teclas (o botones del mando) en la esquina de los botones de arriba, y lo que se movió de sitio.
            RefreshButtonBadges();
            InitEditMode();
            await RepairMovedGamesAsync();
            await RestorePlaySessionAsync();
            await SyncSteamPlaytimeAsync(online: true);
            await CheckForUpdateAsync();
            // Y de vez en cuando, por si Basilisco se queda abierto días.
            var updateTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(6) };
            updateTimer.Tick += async (_, _) => await CheckForUpdateAsync();
            updateTimer.Start();
        };
    }

    // ---- Tamaño de la interfaz ----

    // La interfaz está pensada para 1920 × 1080: en pantallas más pequeñas se reduce y en las grandes se amplía,
    // igual en todo (cabecera, barra de abajo, títulos y menús). Las cajas en 3D ya se ajustan solas.
    const double DesignWidth = 1920, DesignHeight = 1080;
    ScaleTransform _uiScale = new(1, 1);

    void UpdateUiScale()
    {
        if (Root.ActualWidth <= 0 || Root.ActualHeight <= 0)
            return;
        var scale = Math.Clamp(Math.Min(Root.ActualWidth / DesignWidth, Root.ActualHeight / DesignHeight), 0.5, 2.5);
        if (Math.Abs(scale - _uiScale.ScaleX) < 0.005 && HeaderBar.LayoutTransform == _uiScale)
            return;

        _uiScale = new ScaleTransform(scale, scale);
        _uiScale.Freeze();
        FrameworkElement[] scaled =
        [
            HeaderBar, FooterBar, TitlePanel, CarouselControlsLeft, CarouselControlsRight, Toast, AchievementsPageContent,
            EditBanner, FilterStrip, ConsoleEditor, CaseView, ManualReader, Customizer, ArtSearch, FilterEditor, FolderImporter, PlatformPicker, FormatEditor, SteamImporter, ThemeEditor,
            LauncherImporter,
        ];
        foreach (var element in scaled)
            element.LayoutTransform = _uiScale;
        UpdateOverlayLayout();
    }

    // ---- Modo nocturno ----

    void OnNightClick(object sender, RoutedEventArgs e) => SetTheme(ThemeManager.IsDark ? ColorMode.Light : ColorMode.Dark);

    /// <summary>Cambia el modo y lo recuerda.</summary>
    void SetTheme(ColorMode mode)
    {
        _settings.Theme = mode;
        _settings.Save();
        ThemeManager.Apply(mode);
        UpdateNightButton();
    }

    void OnThemeChanged()
    {
        // El color «automático» de las letras cambia con el modo.
        ApplyLook(SelectedGame);
        UpdateNightButton();
    }

    void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_settings.Theme == ColorMode.System && e.Category == UserPreferenceCategory.General)
            Dispatcher.BeginInvoke(() => ThemeManager.Apply(ColorMode.System));
    }

    void UpdateNightButton()
    {
        // Iconos de Windows: el sol para volver al modo claro y la luna para el nocturno.
        NightButton.Content = ThemeManager.IsDark ? "" : "";
        NightButton.ToolTip = ThemeManager.IsDark ? "Pasar al modo claro (N)" : "Pasar al modo nocturno (N)";
    }

    // ---- Manuales de Pinterest ----

    Game? _pinterestGame;

    long ManualSpaceLimit => Math.Max(100, _settings.ManualSpaceLimitMb) * 1024L * 1024L;

    /// <summary>Pinterest para el manual del juego (desde Personalizar o desde el lector), con sus páginas ya elegidas.</summary>
    void OpenPinterest(Game game, string name)
    {
        _pinterestGame = game;
        PinterestPicker.UiScale = _uiScale;
        PinterestPicker.Open(name, game.ManualPages);
    }

    /// <summary>
    /// Descarga las imágenes elegidas en Pinterest y las guarda como el manual del juego (en su orden). Las que ya tenía
    /// no se vuelven a descargar (y conservan su giro). La primera pasa a ser el librito de la caja abierta si no tenía
    /// imagen de manual. Después se respeta el tope de espacio. Si el lector tenía abierto este manual, se vuelve a abrir.
    /// </summary>
    async Task SaveImageManualAsync(Game game, List<PinterestPanel.SavedPage> saved)
    {
        var previous = game.ManualPages ?? [];
        var pages = new List<ManualPage>();
        for (var i = 0; i < saved.Count; i++)
        {
            ShowToast($"Descargando las páginas del manual… {i} de {saved.Count}");
            var item = saved[i];
            var page = previous.FirstOrDefault(old => old.Source == item.Url && ManualPages.IsStored(old)) is { } known
                ? new ManualPage { Hash = known.Hash, Source = known.Source, Width = known.Width, Height = known.Height, Rotation = known.Rotation }
                : (await ManualPages.ImportAsync([item.Url], null, CancellationToken.None)).FirstOrDefault();
            if (page is null)
                continue;
            page.Spread = item.Spread;
            pages.Add(page);
        }
        if (pages.Count == 0)
        {
            ShowToast("No se ha podido descargar ninguna de las imágenes");
            return;
        }
        game.ManualPages = pages;
        game.ManualOpened = DateTime.Now;
        if (ImageStore.GetExtraPath(game, GameExtra.Manual) is null)
        {
            try
            {
                ImageStore.SetExtra(game, GameExtra.Manual, ManualPages.PathOf(pages[0]));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        Save();
        ManualPages.RemoveUnused(Games);
        ManualPages.EnforceLimit(Games, ManualSpaceLimit, keep: game);
        if (Customizer.IsOpen)
            Customizer.RefreshManualInfo();
        if (ManualReader.IsOpen && ManualReader.Game == game)
            OpenImageManual(game);
        var size = pages.Sum(page => File.Exists(ManualPages.PathOf(page)) ? new FileInfo(ManualPages.PathOf(page)).Length : 0);
        ShowToast($"Manual guardado: {pages.Count} páginas · {size / 1048576.0:0.#} MB" +
                  (pages.Count < saved.Count ? $" ({saved.Count - pages.Count} no se pudieron descargar)" : ""));
    }

    void OpenImageManual(Game game)
    {
        if (game.ManualPages is not { Count: > 0 } pages)
            return;
        game.ManualOpened = DateTime.Now;
        Save();
        ManualReader.Open(new ImageManual(pages), game, game.Name, _padMode);
        ManualPages.EnforceLimit(Games, ManualSpaceLimit, keep: game);
    }

    void RemoveImageManual(Game game)
    {
        game.ManualPages = null;
        Save();
        ManualPages.RemoveUnused(Games);
        if (Customizer.IsOpen)
            Customizer.RefreshManualInfo();
        ShowToast("Manual de Pinterest quitado");
    }

    // ---- Rendimiento ----

    /// <summary>Calidad de las cajas, reflejos y fondos en vídeo: se aplican al momento y se recuerdan.</summary>
    void ApplyPerformance(BoxQuality quality, bool reflections, bool animated)
    {
        var repaint = quality != _settings.Quality || reflections != _settings.Reflections;
        _settings.Quality = quality;
        _settings.Reflections = reflections;
        _settings.AnimatedBackgrounds = animated;
        _settings.Save();

        if (repaint)
        {
            _flow.Quality = _settings.QualityScale;
            BoxModel.Reflections = reflections;
            _flow.RepaintAll();
        }
        if (BackgroundVideo.Source is not null)
        {
            if (animated)
                BackgroundVideo.Play();
            else
                BackgroundVideo.Pause();
        }
    }

    /// <summary>
    /// Optimizar la carpeta de datos: primero se mira cuánto se puede ahorrar, se pregunta, y si se acepta se reducen
    /// las imágenes demasiado grandes y se borran los archivos que ya no usa nada.
    /// </summary>
    async Task OptimizeDataAsync()
    {
        ShowToast("Mirando la carpeta de datos…");
        var games = Games.ToList();
        var filters = Filters.ToList();
        var report = await Task.Run(() => DataOptimizer.Analyze(games, filters, _settings));
        if (report.IsEmpty)
        {
            MessageBox.Show(this, "La carpeta de datos ya está optimizada: no hay imágenes de más ni archivos sin usar.",
                "Optimizar datos", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        static string Mb(long bytes) => $"{bytes / 1048576.0:0.#} MB";
        var lines = new List<string>();
        if (report.Large.Count > 0)
            lines.Add($"• Reducir {report.Large.Count} imágenes más grandes de lo que se llega a ver ({Mb(report.LargeBytes)} ahora). Se quedan con el mismo nombre y formato; no se nota en pantalla.");
        if (report.Orphans.Count > 0)
            lines.Add($"• Borrar {report.Orphans.Count} archivos que ya no usa ningún juego, saga ni consola ({Mb(report.OrphanBytes)}).");
        var answer = MessageBox.Show(this, "Se puede aligerar basilisco-data:\n\n" + string.Join("\n", lines) + "\n\n¿Optimizar ahora?",
            "Optimizar datos", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        ShowToast("Optimizando la carpeta de datos…");
        var done = await Task.Run(() => DataOptimizer.Apply(report));
        _flow.RepaintAll();
        ShowToast($"Datos optimizados: {Mb(done.SavedBytes)} menos");
    }

    // ---- Manuales ----

    /// <summary>
    /// Abre el manual del juego. Si aún no tiene, lo crea en PDF con la información de Steam y LaunchBox; su portada
    /// pasa a ser el librito que se ve dentro de la caja (si no tenía ya una imagen de manual).
    /// </summary>
    async Task OpenManualAsync(Game game)
    {
        // El de Pinterest (imágenes) va antes que el PDF.
        if (game.ManualPages is { Count: > 0 })
        {
            OpenImageManual(game);
            return;
        }
        if (ImageStore.GetManualPdfPath(game) is { } existing && File.Exists(existing))
        {
            ManualReader.Open(existing, game, game.Name, _padMode);
            return;
        }

        CaseView.SetManualBusy("Creando el manual…");
        try
        {
            var pdf = await ManualBuilder.CreateAsync(game, FaceImage(game, Face.Front), FaceImage(game, Face.Back),
                new Progress<string>(text => { if (CaseView.Game == game) CaseView.SetManualBusy(text); }), CancellationToken.None);
            await StoreManualAsync(game, pdf);
            if (CaseView.Game == game)
                CaseView.RefreshManual();
            if (ImageStore.GetManualPdfPath(game) is { } created && CaseView.IsOpen && CaseView.Game == game)
                ManualReader.Open(created, game, game.Name, _padMode);
        }
        catch (Exception ex) when (ex is ArtProviderException or IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException
                                   or System.IO.InvalidDataException or System.Xml.XmlException)
        {
            ShowToast(ex is ArtProviderException ? ex.Message : $"No se ha podido crear el manual: {ex.Message}");
            if (CaseView.Game == game)
                CaseView.RefreshManual();
        }
    }

    /// <summary>Guarda el PDF como manual del juego y, si no tenía imagen de manual, pone su portada.</summary>
    async Task StoreManualAsync(Game game, string pdf, bool coverAsImage = true)
    {
        ImageStore.SetManualPdf(game, pdf);
        if (coverAsImage && game.Manual is null && ImageStore.GetManualPdfPath(game) is { } stored && await PdfRenderer.SaveCoverAsync(stored) is { } cover)
        {
            ImageStore.SetExtra(game, GameExtra.Manual, cover);
            game.ManualFrame = null;
        }
        Save();
    }

    /// <summary>La imagen de esa cara en el formato del juego o, si no tiene, en el otro.</summary>
    static string? FaceImage(Game game, Face face) =>
        ImageStore.GetPath(game, game.Format, face) ??
        ImageStore.GetPath(game, game.Format == CoverFormat.Square ? CoverFormat.Portrait : CoverFormat.Square, face);

    // ---- Actualizaciones ----

    UpdateInfo? _update;
    bool _updating;

    async Task CheckForUpdateAsync()
    {
        if (_updating || await Updater.CheckAsync(CancellationToken.None) is not { } update)
            return;
        var isNew = _update?.Version != update.Version;
        _update = update;
        UpdateButton.Content = $"Actualizar a {update.Tag}";
        UpdateButton.ToolTip = "Hay una versión nueva de Basilisco en GitHub: se descarga y se pone sola, sin perder nada (U)";
        UpdateButton.Visibility = Visibility.Visible;
        if (isNew)
            ShowToast($"Hay una versión nueva de Basilisco: {update.Tag}");
    }

    void OnUpdateClick(object sender, RoutedEventArgs e) => _ = UpdateAsync();

    /// <summary>Descarga la versión nueva, la pone en lugar de esta y la abre. La biblioteca y los ajustes no se tocan.</summary>
    async Task UpdateAsync()
    {
        if (_updating || _update is not { } update)
            return;

        var notes = update.Notes.Trim();
        if (notes.Length > 700)
            notes = notes[..700].TrimEnd() + "…";
        var answer = MessageBox.Show(this,
            $"Hay una versión nueva de Basilisco: {update.Tag} (tienes la v{AppInfo.Version}).\n\n{notes}\n\n" +
            "Se descargará, se pondrá en lugar de esta y Basilisco se volverá a abrir. Tu biblioteca, portadas y ajustes no cambian.\n\n¿Actualizar ahora?",
            "Actualizar Basilisco", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        _updating = true;
        UpdateButton.IsEnabled = false;
        try
        {
            var path = await Updater.DownloadAsync(update, new Progress<double>(done =>
                UpdateButton.Content = $"Descargando… {done:P0}"), CancellationToken.None);
            UpdateButton.Content = "Reiniciando…";
            Updater.InstallAndRestart(path, update);
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException
                                   or TaskCanceledException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, $"No se ha podido actualizar Basilisco.\n\n{ex.Message}\n\nPuedes descargar la versión nueva a mano desde GitHub.",
                "Actualizar Basilisco", MessageBoxButton.OK, MessageBoxImage.Warning);
            UpdateButton.Content = $"Actualizar a {update.Tag}";
            UpdateButton.IsEnabled = true;
            _updating = false;
        }
    }

    DateTime _lastSteamSync;

    /// <summary>
    /// Pone a los juegos de Steam las horas que dice Steam (y su última partida). Se leen del Steam de este PC;
    /// con <paramref name="online"/> y la clave de la Web API, también de los servidores (cuenta lo jugado en otros PC).
    /// </summary>
    async Task SyncSteamPlaytimeAsync(bool online)
    {
        if (!Games.Any(game => game.SteamAppId is not null))
            return;
        _lastSteamSync = DateTime.Now;

        var local = await Task.Run(SteamLocal.Playtimes);
        var minutes = local.ToDictionary(entry => entry.Key, entry => entry.Value.Minutes);
        if (online && _settings.SteamApiKey is { Length: > 0 } key && SteamLocal.Accounts().FirstOrDefault() is { } account)
        {
            try
            {
                foreach (var app in await SteamWeb.OwnedGamesAsync(account.SteamId, key, null, CancellationToken.None))
                    minutes[app.AppId] = Math.Max(minutes.GetValueOrDefault(app.AppId), app.PlaytimeMinutes);
            }
            catch (Exception)
            {
                // Sin internet o con la clave mal: se quedan las horas de este PC.
            }
        }

        var changed = false;
        foreach (var game in Games)
        {
            if (game.SteamAppId is not { } appId)
                continue;
            if (minutes.TryGetValue(appId, out var played) && played != game.PlayMinutes)
            {
                game.PlayMinutes = played;
                changed = true;
            }
            if (local.TryGetValue(appId, out var time) && time.LastPlayed > (game.LastPlayed ?? DateTime.MinValue))
            {
                game.LastPlayed = time.LastPlayed;
                changed = true;
            }
        }
        if (!changed)
            return;
        Save();
        if (SelectedGame is not null)
            ShowDetails(_flow.SelectedIndex);
    }

    /// <summary>
    /// Busca los juegos cuyo archivo ya no está donde estaba (porque has movido su carpeta)
    /// y actualiza su ubicación sin perder portadas, filtros ni partidas.
    /// </summary>
    async Task RepairMovedGamesAsync()
    {
        var missing = Games.Where(game => !GameLauncher.IsLink(game.Path) && !File.Exists(GameLauncher.FileOf(game))).Select(game => (Game: game, game.Path)).ToList();
        if (missing.Count == 0)
            return;

        var found = await Task.Run(() => missing
            .Select(entry => (entry.Game, entry.Path, NewPath: GameRelocator.FindMoved(entry.Path)))
            .Where(entry => entry.NewPath is not null)
            .ToList());

        // Mientras buscábamos, el usuario ha podido cambiar o quitar alguno: solo tocamos los que siguen igual.
        var repaired = found.Where(entry => Games.Contains(entry.Game) && entry.Game.Path == entry.Path).ToList();
        if (repaired.Count == 0)
            return;

        var selected = SelectedGame;
        foreach (var (game, _, newPath) in repaired)
            Relink(game, newPath!);

        Save();
        RefreshKeeping(selected);
        ShowToast(repaired.Count == 1
            ? $"«{repaired[0].Game.Name}» había cambiado de carpeta: ubicación actualizada"
            : $"{repaired.Count} juegos habían cambiado de carpeta: ubicaciones actualizadas");
    }

    Game? SelectedGame => IsAddCard(_flow.SelectedIndex) ? null : _visibleGames[_flow.SelectedIndex];

    /// <summary>Recarga la barra de filtros y el carrusel dejando seleccionado el mismo juego, si sigue visible.</summary>
    void RefreshKeeping(Game? game)
    {
        RefreshFilterTabs();
        var index = game is null ? -1 : SortModes.Apply(Games.Where(BelongsToActiveFilter), _settings.SortMode).ToList().IndexOf(game);
        RefreshCarousel(index < 0 ? Math.Min(_flow.SelectedIndex, Games.Count(BelongsToActiveFilter)) : index);
    }

    /// <summary>
    /// Cambia la ubicación de un juego. Si ya había otro juego con esa ubicación (lo habías vuelto a importar
    /// después de moverlo), los junta en uno solo.
    /// </summary>
    void Relink(Game game, string newPath)
    {
        if (Games.FirstOrDefault(other => other != game && SamePath(other.Path, newPath)) is { } duplicate)
            MergeInto(game, duplicate);
        game.Path = newPath;
    }

    void MergeInto(Game keep, Game duplicate)
    {
        // El nombre puesto a mano manda; si era el del archivo, mejor el del duplicado (suele venir de su carpeta).
        if (keep.Name == Path.GetFileNameWithoutExtension(keep.Path))
            keep.Name = duplicate.Name;

        keep.Platform ??= duplicate.Platform;
        // Cada formato conserva sus imágenes y completa las que le falten con las del duplicado.
        foreach (var format in Enum.GetValues<CoverFormat>())
        {
            if (format == CoverFormat.Portrait && duplicate.Portrait is null)
                continue;
            var mine = keep.Faces(format);
            var theirs = duplicate.Faces(format);
            foreach (var face in Enum.GetValues<Face>())
            {
                if (mine.Get(face) is not null || theirs.Get(face) is not { } file)
                    continue;
                mine.Set(face, file);
                mine.SetFrame(face, theirs.GetFrame(face));
                theirs.Set(face, null);
            }
        }
        ImageStore.RemoveAll(duplicate);

        keep.FilterIds = keep.FilterIds.Union(duplicate.FilterIds).ToList();
        keep.PlayCount += duplicate.PlayCount;
        if (duplicate.LastPlayed > (keep.LastPlayed ?? DateTime.MinValue))
            keep.LastPlayed = duplicate.LastPlayed;
        if (duplicate.AddedAt < keep.AddedAt)
            keep.AddedAt = duplicate.AddedAt;

        Games.Remove(duplicate);
    }

    static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Juego de la biblioteca cuyo archivo ya no existe y que tenía este mismo ejecutable.</summary>
    Game? FindMissingWithSameFile(string path) =>
        Games.FirstOrDefault(game =>
            string.Equals(Path.GetFileName(game.Path), Path.GetFileName(path), StringComparison.OrdinalIgnoreCase) &&
            !GameLauncher.IsLink(game.Path) && !GameLauncher.IsLink(path) && !File.Exists(game.Path));

    FoundStatus StatusOf(string path) =>
        IsInLibrary(path) ? FoundStatus.InLibrary
        : FindMissingWithSameFile(path) is not null ? FoundStatus.Moved
        : FoundStatus.New;

    /// <summary>Pide al usuario que señale dónde está ahora el juego.</summary>
    void LocateManually(Game game)
    {
        var folder = Path.GetDirectoryName(game.Path);
        while (folder is not null && !Directory.Exists(folder))
            folder = Path.GetDirectoryName(folder);

        var dialog = new OpenFileDialog
        {
            Title = $"¿Dónde está ahora «{game.Name}»?",
            Filter = GameLauncher.FileFilter,
            FileName = Path.GetFileName(game.Path),
            InitialDirectory = folder ?? "",
            DereferenceLinks = false,
        };
        if (dialog.ShowDialog(this) != true)
            return;

        Relink(game, dialog.FileName);
        Save();
        RefreshKeeping(game);
        ShowToast($"Ubicación de «{game.Name}» actualizada");
    }

    List<Game> Games => _library.Games;
    List<GameFilter> Filters => _library.Filters;

    bool IsPanelOpen => AchievementsPage.IsVisible || PinterestPicker.IsOpen || ConsoleEditor.IsOpen || FilterStrip.IsOpen || ManualReader.IsOpen || CaseView.IsOpen || Customizer.IsOpen || FilterEditor.IsOpen || FolderImporter.IsOpen || ArtSearch.IsOpen || PlatformPicker.IsOpen ||
                        FormatEditor.IsOpen || SteamImporter.IsOpen || ThemeEditor.IsOpen || LauncherImporter.IsOpen;

    bool IsAddCard(int index) => index >= _visibleGames.Count;

    bool BelongsToActiveFilter(Game game) =>
        (_activePlatform is null || game.Platform == _activePlatform) &&
        (_activeFilter is null || game.FilterIds.Contains(_activeFilter.Id)) &&
        (_activeMember is null || _activeMember.Owns(game)) &&
        (_activePlayers is null || PlayersGroup(game) == _activePlayers);

    int CountGames(GameFilter filter) => Games.Count(game => game.FilterIds.Contains(filter.Id));

    int CountGames(Platform platform) => Games.Count(game => game.Platform == platform.Id);

    void RefreshCarousel(int selectedIndex, bool jump = false)
    {
        _visibleGames = SortModes.Apply(Games.Where(BelongsToActiveFilter), _settings.SortMode).ToList();
        var items = _visibleGames
            .Select(ToCarouselItem)
            .Append(new CarouselItem("Importar juego", IsAddCard: true))
            .ToList();

        _flow.SetItems(items, selectedIndex, jump);
    }

    static CarouselItem ToCarouselItem(Game game) =>
        new(game.Name, IsAddCard: false,
            ImageStore.GetPath(game, game.Format, Face.Front), ImageStore.GetPath(game, game.Format, Face.Back), game.Platform,
            ImageStore.GetPath(game, game.Format, Face.Spine), game.CurrentFaces.BackFrame, game.CurrentFaces.SpineFrame,
            game.Format, game.CurrentFaces.FrontFrame, Steam: game.SteamAppId is not null);

    /// <summary>
    /// Barra de arriba: «Todos los juegos», el grupo de consolas, el de filtros y el orden. Cada botón dice lo que
    /// está elegido (por ejemplo «PS2 (3)») y se ve marcado si es lo que se está viendo.
    /// </summary>
    void RefreshFilterTabs()
    {
        AllTab.Content = $"Todos los juegos ({Games.Count})";
        AllTab.IsChecked = _activeFilter is null && _activePlatform is null && _activeMember is null && _activePlayers is null;

        ConsolesTab.Content = Platforms.Find(_activePlatform) is { } platform ? $"{platform.Name} ({CountGames(platform)})  ▾" : "Consolas  ▾";
        ConsolesTab.IsChecked = _activePlatform is not null;

        // Con un filtro activo se ve su logo (si tiene) o su nombre.
        if (_activeFilter is { } filter)
        {
            var image = ImageStore.GetFilterImagePath(filter);
            var logo = image is null ? null : CoverArt.LoadImage(image, FilterIconDecodeWidth);
            FiltersTab.Content = logo is null
                ? $"{filter.Name} ({CountGames(filter)})  ▾"
                : new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new Image { Source = logo, Height = 30, MaxWidth = 150, Stretch = Stretch.Uniform, Margin = new Thickness(-6, -4, 8, -4) },
                        new TextBlock { Text = $"({CountGames(filter)})  ▾", VerticalAlignment = VerticalAlignment.Center },
                    },
                };
            FiltersTab.ToolTip = $"{filter.Name} · clic derecho para editarlo";
        }
        else
        {
            FiltersTab.Content = "Sagas  ▾";
            FiltersTab.ToolTip = "Ver solo los juegos de una saga, crear sagas nuevas o editarlas";
        }
        FiltersTab.IsChecked = _activeFilter is not null;

        FamilyTab.Content = _activeMember is { } member ? $"{member.Name} ({CountGames(member)})  ▾" : "Steam familiar  ▾";
        FamilyTab.ToolTip = _activeMember is { } shown
            ? $"Los juegos de {shown.Name} · clic derecho para personalizarlo"
            : "Ver los juegos de cada miembro de tu familia de Steam";
        FamilyTab.IsChecked = _activeMember is not null;

        PlayersTab.Content = _activePlayers is { } players ? $"{PlayersName(players)} ({Games.Count(game => PlayersGroup(game) == players)})  ▾" : "Nº Jugadores  ▾";
        PlayersTab.IsChecked = _activePlayers is not null;

        // Solo «Orden» (cuál está elegido se ve en el menú y en su ayuda), para que el buscador quepa entero.
        SortTab.Content = "Orden  ▾";
        SortTab.ToolTip = $"Cambiar el orden de los juegos · ahora: {SortModes.Short(_settings.SortMode)}";
        SortTab.IsChecked = false;
    }

    /// <summary>Abre un menú desplegable debajo de un botón de la barra de arriba.</summary>
    void ShowMenu(FrameworkElement owner, ContextMenu menu)
    {
        menu.PlacementTarget = owner;
        menu.Placement = PlacementMode.Bottom;
        menu.VerticalOffset = 6;
        menu.FontSize = 15;
        menu.Closed += (_, _) => RefreshFilterTabs();
        menu.IsOpen = true;
    }

    static MenuItem Item(object header, Action? onClick, bool isChecked = false, bool enabled = true, object? icon = null)
    {
        var item = new MenuItem { Header = header, IsChecked = isChecked, IsEnabled = enabled, Icon = icon, Padding = new Thickness(6, 6, 18, 6) };
        if (onClick is not null)
            item.Click += (_, _) => onClick();
        return item;
    }

    void OnAllTabClick(object sender, RoutedEventArgs e)
    {
        if (_activeFilter is not null || _activePlatform is not null || _activeMember is not null || _activePlayers is not null)
            SelectView(null, null);
        else
            RefreshFilterTabs();
    }

    /// <summary>Grupo «Consolas»: las que tienen juegos (y la que estás viendo).</summary>
    /// <remarks>
    /// Se abre la misma franja negra que la de los filtros: cada consola con su imagen (de LaunchBox o propia) o su
    /// nombre, y «Personalizar consola» al final para ponerle imagen.
    /// </remarks>
    void OnConsolesTabClick(object sender, RoutedEventArgs e)
    {
        RefreshFilterTabs();
        static string Games(int count) => count == 1 ? "1 juego" : $"{count} juegos";

        var tiles = new List<FilterTile>
        {
            new("Todas las consolas", null, Games(this.Games.Count), _activePlatform is null, () => SelectView(null, _activeFilter)),
        };
        foreach (var platform in Platforms.All)
        {
            var count = CountGames(platform);
            if (count == 0 && platform.Id != _activePlatform)
                continue;
            var chosen = platform;
            tiles.Add(new FilterTile(platform.Name, ImageStore.GetPlatformImagePath(platform.Id), $"{platform.Name} · {Games(count)}",
                platform.Id == _activePlatform, () => SelectView(chosen.Id, null), Edit: () => ConsoleEditor.Open(chosen)));
        }
        if (tiles.Count > 1)
            tiles.Add(new FilterTile("Personalizar consola", null, "Ponerle una imagen (de LaunchBox o tuya)", false,
                () => FilterStrip.StartEditing(), IsAction: true, KeepOpen: true));

        var scale = _uiScale.ScaleX;
        var top = (HeaderBar.Margin.Top + HeaderBar.ActualHeight * scale) / scale + 6;
        FilterStrip.Open(tiles, top, _padMode, "CONSOLAS", "ELIGE LA CONSOLA QUE QUIERES PERSONALIZAR");
    }

    /// <summary>
    /// Grupo «Filtros»: se abre la franja negra con tus filtros (sus logotipos en grande, o el nombre si no tienen),
    /// y al final «Nuevo filtro» y «Editar filtro».
    /// </summary>
    void OnFiltersTabClick(object sender, RoutedEventArgs e)
    {
        RefreshFilterTabs();
        OpenFilterStrip();
    }

    void OpenFilterStrip()
    {
        static string Games(int count) => count == 1 ? "1 juego" : $"{count} juegos";

        var tiles = new List<FilterTile>
        {
            new("Todas las sagas", null, Games(this.Games.Count), _activeFilter is null, () => SelectView(_activePlatform, null)),
        };
        foreach (var filter in Filters)
        {
            var chosen = filter;
            tiles.Add(new FilterTile(filter.Name, ImageStore.GetFilterImagePath(filter), $"{filter.Name} · {Games(CountGames(filter))}",
                filter == _activeFilter, () => SelectView(null, chosen), Edit: () => OpenFilterEditor(chosen)));
        }
        tiles.Add(new FilterTile("+  Nueva saga", null, "Crear una saga", false, () => OpenFilterEditor(null), IsAction: true));
        if (Filters.Count > 0)
            tiles.Add(new FilterTile("Editar saga", null, "Cambiar el nombre, el logotipo o los juegos", false, () => FilterStrip.StartEditing(),
                IsAction: true, KeepOpen: true));

        // Justo debajo de la barra de arriba (en las unidades del panel, que va escalado como el resto).
        var scale = _uiScale.ScaleX;
        var top = (HeaderBar.Margin.Top + HeaderBar.ActualHeight * scale) / scale + 6;
        FilterStrip.Open(tiles, top, _padMode, "SAGAS", "ELIGE LA SAGA QUE QUIERES EDITAR");
    }

    // ---- Steam familiar ----

    int CountGames(FamilyMember member) => Games.Count(member.Owns);

    /// <summary>
    /// Los miembros de la familia de Steam, tú el primero. Si aún no se ha leído la familia de Steam (bibliotecas de antes
    /// de la 0.6.7), se sacan de los juegos: tú con los tuyos y cada uno con los que comparte.
    /// </summary>
    List<FamilyMember> FamilyMembers()
    {
        if (_library.Family.Count > 0)
            return _library.Family.OrderByDescending(member => member.IsMe).ToList();

        var members = new List<FamilyMember>();
        if (Games.Any(game => game.SteamAppId is not null && game.SteamOwner is null))
            members.Add(FamilyMember.FromName("Tú", isMe: true));
        members.AddRange(Games.Select(game => game.SteamOwner).OfType<string>().Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase).Select(name => FamilyMember.FromName(name)));
        return members;
    }

    static string? MemberImage(FamilyMember member) => ImageStore.GetMenuImagePath(AppInfo.FamilyImagesDirectory, member.Key);

    void OnFamilyTabClick(object sender, RoutedEventArgs e)
    {
        RefreshFilterTabs();
        OpenFamilyStrip();
    }

    /// <summary>
    /// Grupo «Steam familiar»: la misma franja negra que la de consolas y filtros, con cada miembro de la familia (tú
    /// incluido) con su imagen (su foto de perfil o la que le pongas) o su nombre. Al elegir uno se ven todos sus
    /// juegos: los que tienen varios están en la ficha de cada uno.
    /// </summary>
    void OpenFamilyStrip()
    {
        static string Games(int count) => count == 1 ? "1 juego" : $"{count} juegos";

        var members = FamilyMembers();
        var tiles = new List<FilterTile>
        {
            new("Todos los juegos", null, Games(this.Games.Count), _activeMember is null, () => SelectView(null, null)),
        };
        foreach (var member in members)
        {
            var chosen = member;
            var who = member.IsMe ? $"{member.Name} (tú)" : member.Name;
            tiles.Add(new FilterTile(member.Name, MemberImage(member), $"{who} · {Games(CountGames(member))}",
                member.SteamId == _activeMember?.SteamId, () => SelectView(null, null, chosen), Edit: () => OpenMemberEditor(chosen), FillImage: true));
        }
        if (members.Count > 0)
            tiles.Add(new FilterTile("Personalizar miembro", null, "Ponerle una imagen (su foto de Steam o una tuya)", false,
                () => FilterStrip.StartEditing(), IsAction: true, KeepOpen: true));
        if (_library.Family.Count == 0)
            tiles.Add(new FilterTile("Leer mi familia de Steam", null, "Con el token de sesión: todos los miembros y de quién es cada juego", false,
                OpenSteamImport, IsAction: true));

        var scale = _uiScale.ScaleX;
        var top = (HeaderBar.Margin.Top + HeaderBar.ActualHeight * scale) / scale + 6;
        FilterStrip.Open(tiles, top, _padMode, "STEAM FAMILIAR", "ELIGE EL MIEMBRO QUE QUIERES PERSONALIZAR");
    }

    // Clic derecho con un miembro elegido: personalizarlo directamente.
    void OnFamilyTabRightClick(object sender, MouseButtonEventArgs e)
    {
        if (_activeMember is { } member)
            OpenMemberEditor(member);
        e.Handled = true;
    }

    void OpenMemberEditor(FamilyMember member) =>
        ConsoleEditor.OpenMember(member, member.AvatarUrl is { } url ? () => DownloadAvatarAsync(url) : null,
            () => _ = OpenMemberAchievementsAsync(member));

    static async Task<string?> DownloadAvatarAsync(string url)
    {
        try
        {
            return await ArtProvider.DownloadAsync(new ArtImage(url, url, 0, 0), CancellationToken.None);
        }
        catch (ArtProviderException)
        {
            return null;
        }
    }

    /// <summary>
    /// Se ha leído Steam: se guardan los miembros de la familia y quién tiene cada juego (también los que ya estaban en la
    /// biblioteca). A los miembros sin imagen se les pone su foto de perfil.
    /// </summary>
    void ApplyFamily(SteamFamilyInfo info)
    {
        _lastOwners = info.Owners;
        var familyRead = info.Members.Count > 0;
        if (familyRead)
            _library.Family = info.Members;
        SteamAchievements.MySteamId = MySteamId();
        ApplyOwners(replace: familyRead);
        Save();
        if (_activeMember is { } member)
            _activeMember = FamilyMembers().FirstOrDefault(m => m.SteamId == member.SteamId);
        RefreshFilterTabs();
        if (_activeMember is not null)
            RefreshKeeping(SelectedGame);
        _ = DownloadMissingAvatarsAsync();
    }

    // Quién tiene cada juego, de la última vez que se leyó Steam (para los que se importen después).
    Dictionary<int, List<string>> _lastOwners = [];

    /// <param name="replace">True si se ha leído la familia entera (los dueños son todos); si no, solo se añaden.</param>
    void ApplyOwners(bool replace)
    {
        foreach (var game in Games)
        {
            if (game.SteamAppId is { } appId && _lastOwners.TryGetValue(appId, out var owners))
                game.SteamOwnerIds = replace ? [.. owners] : game.SteamOwnerIds.Union(owners).ToList();
        }
    }

    async Task DownloadMissingAvatarsAsync()
    {
        var changed = false;
        foreach (var member in _library.Family.Where(member => member.AvatarUrl is not null && MemberImage(member) is null).ToList())
        {
            if (await DownloadAvatarAsync(member.AvatarUrl!) is not { } path)
                continue;
            try
            {
                ImageStore.SetMenuImage(AppInfo.FamilyImagesDirectory, member.Key, path);
                changed = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        if (changed)
            RefreshFilterTabs();
    }

    /// <summary>
    /// Hasta la 0.6.6 los juegos de la familia se metían en un filtro «Steam Familiar». Ahora tienen su propio menú, así
    /// que ese filtro se quita (los juegos siguen en la biblioteca).
    /// </summary>
    void RemoveOldFamilyFilter()
    {
        var old = Filters.FirstOrDefault(filter => string.Equals(filter.Name, "Steam Familiar", StringComparison.CurrentCultureIgnoreCase));
        if (old is null)
            return;
        foreach (var game in Games)
            game.FilterIds.Remove(old.Id);
        ImageStore.RemoveFilterImage(old);
        Filters.Remove(old);
        Save();
    }

    // Clic derecho en el grupo de filtros con uno activo: editarlo directamente.
    void OnFiltersTabRightClick(object sender, MouseButtonEventArgs e)
    {
        if (_activeFilter is { } filter)
            OpenFilterEditor(filter);
        e.Handled = true;
    }

    /// <summary>Menú de orden: personalizado, alfabético, fecha de lanzamiento y fecha de adición.</summary>
    void OnSortTabClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { LayoutTransform = _uiScale };
        menu.SetResourceReference(BackgroundProperty, "CardBrush");
        menu.SetResourceReference(ForegroundProperty, "TextBrush");
        foreach (var mode in Enum.GetValues<SortMode>())
        {
            var chosen = mode;
            menu.Items.Add(Item(SortModes.Label(mode), () => _ = SetSortModeAsync(chosen), isChecked: mode == _settings.SortMode));
            if (mode is SortMode.Custom or SortMode.NameDescending or SortMode.ReleaseOldest)
                menu.Items.Add(new Separator());
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("«Personalizado» abre la estantería: ↑ coge un juego, ← → lo mueve y ↓ lo suelta", null, enabled: false));

        ShowMenu(SortTab, menu);
    }

    async Task SetSortModeAsync(SortMode mode)
    {
        var selected = SelectedGame;
        _settings.SortMode = mode;
        _settings.Save();

        if (SortModes.UsesReleaseDate(mode))
            await FillReleaseDatesAsync();

        RefreshKeeping(selected);
        if (mode == SortMode.Custom)
        {
            EnterShelf();
            return;
        }
        ExitShelf();
        ShowToast($"Orden: {SortModes.Label(mode)}");
    }

    /// <summary>
    /// Pone la fecha de lanzamiento a los juegos que no la tienen, buscándolos en LaunchBox por su nombre exacto
    /// (solo si su base de datos ya está descargada). Los que no se encuentran se quedan al final.
    /// </summary>
    async Task FillReleaseDatesAsync()
    {
        var missing = Games.Where(game => game.ReleaseDate is null).ToList();
        if (missing.Count == 0)
            return;

        if (!LaunchBoxDatabase.IsDownloaded)
        {
            ShowToast("Para saber las fechas de lanzamiento, descarga LaunchBox (Personalizar → Buscar en LaunchBox) o escríbelas en Personalizar");
            return;
        }

        ShowToast("Buscando fechas de lanzamiento en LaunchBox…");
        if (!await LaunchBoxDatabase.TryLoadAsync(CancellationToken.None))
            return;

        var found = await Task.Run(() => missing
            .Select(game => (Game: game, Date: LaunchBoxDatabase.FindReleaseDate(game.Name, LaunchBoxDatabase.PlatformName(game.Platform))))
            .Where(entry => entry.Date is not null)
            .ToList());
        foreach (var (game, date) in found)
            game.ReleaseDate ??= date;

        if (found.Count > 0)
            Save();
        var left = missing.Count - found.Count;
        if (left > 0)
            ShowToast($"{left} juegos sin fecha de lanzamiento (van al final). Puedes ponerla en Personalizar (E)");
    }

    /// <summary>
    /// Estantería del orden personalizado: las cajas se giran para enseñar el lomo. ↑ coge el juego del centro,
    /// ← → lo lleva entre los demás y ↓ lo suelta ahí. Esc (o B en el mando) termina y guarda el orden.
    /// </summary>
    void EnterShelf()
    {
        if (_flow.ShelfMode)
            return;
        _flow.ShelfMode = true;
        UpdateShelfHints();
        ShowToast("Estantería: ↑ coge un juego, ← → muévelo y ↓ suéltalo donde quieras");
    }

    void ExitShelf()
    {
        if (!_flow.ShelfMode)
            return;
        _flow.ShelfMode = false;
        Save();
        UpdateShelfHints();
        ShowToast("Orden guardado");
    }

    void ShelfLift()
    {
        if (SelectedGame is null)
            return;
        _flow.SetHolding(true);
        UpdateShelfHints();
    }

    void ShelfDrop()
    {
        if (!_flow.Holding)
            return;
        _flow.SetHolding(false);
        Save();
        UpdateShelfHints();
    }

    /// <summary>← → en la estantería: recorrerla, o llevar el juego cogido entre los demás.</summary>
    void ShelfMove(int direction)
    {
        if (!_flow.Holding)
        {
            _flow.MoveBy(direction);
            return;
        }

        var index = _flow.SelectedIndex;
        var target = index + direction;
        if (index < 0 || index >= _visibleGames.Count || target < 0 || target >= _visibleGames.Count)
            return;

        // En la biblioteca completa el juego pasa a ocupar el sitio de su vecino visible (aunque haya un filtro activo).
        var game = _visibleGames[index];
        var neighbor = _visibleGames[target];
        Games.Remove(game);
        var at = Games.IndexOf(neighbor);
        Games.Insert(direction > 0 ? at + 1 : at, game);
        (_visibleGames[index], _visibleGames[target]) = (_visibleGames[target], _visibleGames[index]);
        _flow.MoveHeld(direction);
    }

    void UpdateShelfHints()
    {
        var shelf = _flow.ShelfMode;
        ShelfHints.Visibility = shelf ? Visibility.Visible : Visibility.Collapsed;
        // En la estantería, sus teclas ocupan el sitio de la barra de logros.
        ShowAchievements(shelf || _flow.SelectedIndex < 0 || IsAddCard(_flow.SelectedIndex) ? null : _visibleGames[_flow.SelectedIndex]);
        ShelfMoveHint.Text = _flow.Holding ? "Mover" : "Recorrer";
        ShelfDoneKey.Text = _padMode ? GamepadService.Label(PadAction.Back, _gamepad.Style) : "Esc";
    }

    bool HandleShelfKey(Key key)
    {
        switch (key)
        {
            case Key.Up: ShelfLift(); break;
            case Key.Down: ShelfDrop(); break;
            case Key.Left: ShelfMove(-1); break;
            case Key.Right: ShelfMove(1); break;
            case Key.Home when !_flow.Holding: _flow.Select(0); break;
            case Key.End when !_flow.Holding: _flow.Select(_flow.Count - 1); break;
            case Key.Escape or Key.Enter when _flow.Holding: ShelfDrop(); break;
            case Key.Escape or Key.Enter: ExitShelf(); break;
            case Key.F11: ToggleFullScreen(); break;
            default: return false;
        }
        return true;
    }

    void SelectFilter(GameFilter? filter) => SelectView(null, filter);

    void SelectView(string? platform, GameFilter? filter, FamilyMember? member = null, int? players = null)
    {
        _activePlatform = platform;
        _activeFilter = filter;
        _activeMember = member;
        _activePlayers = players;
        RefreshFilterTabs();
        RefreshCarousel(0, jump: true);

        Viewport.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });
    }

    void CycleFilter(int direction)
    {
        // El mismo orden que la barra de arriba: todos, consolas con juegos y filtros.
        var options = new List<(string? Platform, GameFilter? Filter)> { (null, null) };
        options.AddRange(Platforms.All.Where(platform => CountGames(platform) > 0).Select(platform => ((string?)platform.Id, (GameFilter?)null)));
        options.AddRange(Filters.Select(filter => ((string?)null, (GameFilter?)filter)));
        var current = Math.Max(0, options.IndexOf((_activePlatform, _activeFilter)));
        var next = options[(current + direction + options.Count) % options.Count];
        SelectView(next.Platform, next.Filter);
    }

    void ShowDetails(int index)
    {
        if (IsAddCard(index))
        {
            TitleText.Text = "Importar juego";
            Chips.ItemsSource = ActiveViewName is { } view && _visibleGames.Count == 0
                ? new[] { $"«{view}» está vacío", "Asigna juegos con E desde «Todos los juegos»", "o importa juegos nuevos aquí" }
                : new[] { "Ejecutables, accesos directos y scripts", "C para importar una carpeta entera", "También puedes arrastrar archivos o carpetas aquí" };
            ActivateHint.Text = "Importar";
            GameHints.Visibility = Visibility.Collapsed;
        }
        else
        {
            var game = _visibleGames[index];
            TitleText.Text = game.Name;
            Chips.ItemsSource = DescribeGame(game);
            ActivateHint.Text = "Abrir";
            GameHints.Visibility = Visibility.Visible;
        }

        var selectedGame = IsAddCard(index) ? null : _visibleGames[index];
        // (con la tarjeta de importar se ven la apariencia general)
        ApplyLook(selectedGame);
        ShowAchievements(selectedGame);

        UpdateOverlayLayout();
        TitlePanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });
    }

    static string PlayTimeLabel(int minutes) => minutes switch
    {
        < 60 => $"⏱  {minutes} min jugados",
        _ when minutes % 60 == 0 => $"⏱  {minutes / 60} h jugadas",
        _ => $"⏱  {minutes / 60} h {minutes % 60} min jugadas",
    };

    // ---- Logros de Steam ----

    readonly System.Windows.Threading.DispatcherTimer _achievementsDelay = new() { Interval = TimeSpan.FromMilliseconds(400) };
    CancellationTokenSource? _achievementsFetch;
    Game? _achievementsGame;

    /// <summary>
    /// Barra de logros de Steam del juego seleccionado (abajo, en el centro). Se enseña al momento lo último que se supo y,
    /// si es viejo, se pregunta a Steam cuando dejas el juego seleccionado un momento (así no se pregunta por cada caja al
    /// deslizar). Solo para los juegos de Steam; con la estantería abierta se ven sus teclas en su lugar.
    /// </summary>
    void ShowAchievements(Game? game)
    {
        _achievementsDelay.Stop();
        _achievementsFetch?.Cancel();
        _achievementsGame = game;
        if (game is null || _flow.ShelfMode)
        {
            AchievementsPanel.Visibility = Visibility.Collapsed;
            return;
        }
        // Sin logros de Steam: el perfil de trofeos del juego.
        if (game.SteamAppId is not { } appId)
        {
            AchievementsPanel.Visibility = Visibility.Visible;
            AchievementsPanel.ToolTip = "Ver y crear los trofeos del juego";
            ShowOwnTrophiesBar(game);
            return;
        }
        AchievementsPanel.ToolTip = "Ver todos los logros del juego";

        AchievementsPanel.Visibility = Visibility.Visible;
        if (SteamAchievements.Cached(appId) is { } known)
            SetAchievements(known);
        else
            SetAchievementsMessage("Logros de Steam · cargando…");
        if (!SteamAchievements.IsStale(appId))
            return;

        if (_settings.SteamApiKey is not { Length: > 0 })
        {
            if (SteamAchievements.Cached(appId) is null)
                SetAchievementsMessage("Para ver los logros, guarda tu clave de la Web API de Steam (botón Steam)");
            return;
        }
        if (_achievementsDelay.Tag is null)
        {
            _achievementsDelay.Tag = true;
            _achievementsDelay.Tick += (_, _) =>
            {
                _achievementsDelay.Stop();
                _ = FetchAchievementsAsync();
            };
        }
        _achievementsDelay.Start();
    }

    async Task FetchAchievementsAsync()
    {
        if (_achievementsGame is not { SteamAppId: { } appId } game || _settings.SteamApiKey is not { Length: > 0 } key)
            return;
        // Tu cuenta: la de la familia leída de Steam o, si no, la última con la que se entró en Steam en este PC.
        var steamId = _library.Family.FirstOrDefault(member => member.IsMe && member.IsKnownAccount)?.SteamId
                      ?? SteamLocal.Accounts().FirstOrDefault()?.SteamId;
        if (steamId is null)
        {
            SetAchievementsMessage("Inicia sesión en Steam en este PC para ver tus logros");
            return;
        }

        var cancellation = _achievementsFetch = new CancellationTokenSource();
        try
        {
            var progress = await SteamAchievements.FetchAsync(appId, steamId, key, cancellation.Token);
            if (_achievementsGame == game)
                SetAchievements(progress);
        }
        catch (AchievementsException ex)
        {
            if (_achievementsGame == game && SteamAchievements.Cached(appId) is null)
                SetAchievementsMessage(ex.Message);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            // Se suelta al acabar (si mientras tanto se ha elegido otro juego, ya hay otra petición en su lugar).
            if (_achievementsFetch == cancellation)
                _achievementsFetch = null;
            cancellation.Dispose();
        }
    }

    void SetAchievementsMessage(string message)
    {
        AchievementsText.Text = message;
        AchievementsPercent.Text = "";
        SetAchievementsFill(0, complete: false);
    }

    void SetAchievements(AchievementProgress progress, string label = "Logros de Steam")
    {
        if (progress.Total == 0)
        {
            SetAchievementsMessage($"{label} · este juego no tiene logros (o aún no lo has abierto)");
            return;
        }
        AchievementsText.Text = progress.Complete
            ? $"{label} · ¡todos! {progress.Total} de {progress.Total}"
            : $"{label} · {progress.Unlocked} de {progress.Total}";
        var percent = progress.Fraction * 100;
        AchievementsPercent.Text = progress.Complete ? "100 %" : $"{(percent < 1 && progress.Unlocked > 0 ? 1 : Math.Floor(percent)):0} %";
        SetAchievementsFill(progress.Fraction, progress.Complete);
    }

    static readonly LinearGradientBrush SteamBlue = Frozen(new LinearGradientBrush(
        [new GradientStop(Color.FromRgb(0x1A, 0x44, 0xC2), 0), new GradientStop(Color.FromRgb(0x47, 0xBF, 0xFF), 1)], 0));

    static readonly LinearGradientBrush Gold = Frozen(new LinearGradientBrush(
        [new GradientStop(Color.FromRgb(0xB8, 0x86, 0x0B), 0), new GradientStop(Color.FromRgb(0xFF, 0xD8, 0x6B), 0.55),
         new GradientStop(Color.FromRgb(0xF5, 0xB3, 0x00), 1)], 0));

    static T Frozen<T>(T brush) where T : Freezable
    {
        brush.Freeze();
        return brush;
    }

    static readonly ImageSource TrophyColor = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Basilisco;component/Assets/trophy.png"));
    static readonly ImageSource TrophyGray = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Basilisco;component/Assets/trophy-gray.png"));

    /// <summary>Llena la barra; completa, se pone dorada y el trofeo brilla (y late despacio). Si no, el trofeo va en gris.</summary>
    void SetAchievementsFill(double fraction, bool complete)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        AchievementsFill.Width = new GridLength(fraction, GridUnitType.Star);
        AchievementsRest.Width = new GridLength(1 - fraction, GridUnitType.Star);
        AchievementsBar.Background = complete ? Gold : SteamBlue;
        AchievementsPercent.Foreground = complete ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x6B)) : (Brush)FindResource("UiText");

        TrophyImage.Source = complete ? TrophyColor : TrophyGray;
        TrophyImage.Opacity = complete ? 1 : 0.75;
        TrophyImage.Effect = complete
            ? new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0xFF, 0xD8, 0x6B), BlurRadius = 26, ShadowDepth = 0, Opacity = 1 }
            : null;
        AchievementsBar.Effect = complete
            ? new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0xFF, 0xD0, 0x50), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.9 }
            : null;

        var pulse = complete
            ? new DoubleAnimation(1, 1.08, TimeSpan.FromSeconds(1.1)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() }
            : null;
        TrophyScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        TrophyScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }

    // ---- Página de logros ----

    void OnAchievementsPanelClick(object sender, MouseButtonEventArgs e)
    {
        if (_achievementsGame is not { } game)
            return;
        if (game.SteamAppId is null)
            _ = OpenTrophyProfileAsync(game);
        else
            _ = OpenAchievementsPageAsync(game);
    }

    void OnAchievementsBackClick(object sender, RoutedEventArgs e) => CloseAchievementsPage();

    /// <summary>Tu cuenta de Steam: la de la familia leída de Steam o, si no, la última con la que se entró en Steam en este PC.</summary>
    string? MySteamId() =>
        _library.Family.FirstOrDefault(member => member.IsMe && member.IsKnownAccount)?.SteamId ?? SteamLocal.Accounts().FirstOrDefault()?.SteamId;

    readonly TranslateTransform _sceneShift = new();
    CancellationTokenSource? _achievementsPageLoad;

    /// <summary>Lo que se mueve hacia arriba al bajar a la página de logros: todo lo de la pantalla principal.</summary>
    IEnumerable<UIElement> SceneElements() =>
        Root.Children.OfType<UIElement>().TakeWhile(element => element != AchievementsPage);

    /// <summary>
    /// Baja a la página de logros: la escena sube y desaparece por arriba y la página entra desde abajo. Ahí están todos
    /// los logros del juego como trofeos: bronce los fáciles, plata los de dificultad media, oro los difíciles (según
    /// cuántos jugadores los tienen) y el platino, el de conseguirlos todos. Los tuyos brillan; los que faltan, en gris.
    /// </summary>
    async Task OpenAchievementsPageAsync(Game game)
    {
        if (game.SteamAppId is not { } appId || AchievementsPage.IsVisible)
            return;

        AchievementsPageKind.Text = "LOGROS DE STEAM";
        TrophyTools.Visibility = Visibility.Collapsed;
        CloseTrophyEditor();
        AchievementsPageTitle.Text = game.Name;
        AchievementsPageSummary.Text = "Cargando los logros…";
        AchievementsList.Children.Clear();
        AchievementsScroll.ScrollToTop();
        AnimatePage(open: true);

        if (_settings.SteamApiKey is not { Length: > 0 } key || MySteamId() is not { } steamId)
        {
            AchievementsPageSummary.Text = _settings.SteamApiKey is { Length: > 0 }
                ? "Inicia sesión en Steam en este PC para ver tus logros."
                : "Para ver los logros, guarda tu clave de la Web API de Steam (botón Steam).";
            return;
        }

        _achievementsPageLoad?.Cancel();
        var cancellation = _achievementsPageLoad = new CancellationTokenSource();
        try
        {
            var list = await SteamAchievements.DetailsAsync(appId, steamId, key, cancellation.Token);
            if (cancellation.IsCancellationRequested || !AchievementsPage.IsVisible)
                return;
            FillAchievementsPage(game, list);
            if (_achievementsGame == game && SteamAchievements.Cached(appId) is { } progress)
                SetAchievements(progress);
            // Los que solo están en inglés, en español (en cuanto lleguen las traducciones).
            var translated = await TranslateSteamAsync(list);
            if (translated != list && AchievementsPage.IsVisible && !cancellation.IsCancellationRequested && !TrophyTools.IsVisible)
            {
                var offset = AchievementsScroll.VerticalOffset;
                FillAchievementsPage(game, translated);
                AchievementsScroll.ScrollToVerticalOffset(offset);
            }
        }
        catch (AchievementsException ex)
        {
            AchievementsPageSummary.Text = ex.Message;
        }
        catch (OperationCanceledException)
        {
        }
    }

    void CloseAchievementsPage()
    {
        if (!AchievementsPage.IsVisible)
            return;
        // Dentro de un juego de los logros de un miembro, se vuelve primero a la lista de sus juegos.
        if (_memberView is not null && _memberGameOpen)
        {
            ShowMemberGames();
            return;
        }
        _achievementsPageLoad?.Cancel();
        AnimatePage(open: false);
    }

    void AnimatePage(bool open)
    {
        var height = Math.Max(1, Root.ActualHeight);
        foreach (var element in SceneElements())
            element.RenderTransform = _sceneShift;
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var duration = TimeSpan.FromMilliseconds(520);

        AchievementsPage.Visibility = Visibility.Visible;
        var page = new DoubleAnimation(open ? height : 0, open ? 0 : height, duration) { EasingFunction = ease };
        if (!open)
        {
            page.Completed += (_, _) =>
            {
                AchievementsPage.Visibility = Visibility.Collapsed;
                _memberView = null;
                _memberWork?.Cancel();
                MemberGamesButton.Visibility = Visibility.Collapsed;
                CloseTrophyEditor();
                TrophyTools.Visibility = Visibility.Collapsed;
                _trophyGame = null;
                foreach (var element in SceneElements())
                    element.ClearValue(RenderTransformProperty);
                AchievementsList.Children.Clear();
                Focus();
            };
        }
        AchievementsPageShift.BeginAnimation(TranslateTransform.YProperty, page);
        _sceneShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(open ? 0 : -height, open ? -height : 0, duration) { EasingFunction = ease });
        if (open)
            Focus();
    }

    // Colores de cada trofeo (de oscuro a claro, para el degradado).
    static (Color Dark, Color Light) TierColors(TrophyTier tier) => tier switch
    {
        TrophyTier.Platinum => (Color.FromRgb(0x5D, 0x8F, 0xB8), Color.FromRgb(0xE4, 0xF3, 0xFF)),
        TrophyTier.Gold => (Color.FromRgb(0xB8, 0x86, 0x0B), Color.FromRgb(0xFF, 0xD8, 0x6B)),
        TrophyTier.Silver => (Color.FromRgb(0x8A, 0x92, 0x9C), Color.FromRgb(0xEE, 0xF1, 0xF4)),
        _ => (Color.FromRgb(0x8C, 0x4F, 0x22), Color.FromRgb(0xE3, 0xA0, 0x6A)),
    };

    static string TierName(TrophyTier tier) => tier switch
    {
        TrophyTier.Platinum => "Platino",
        TrophyTier.Gold => "Oro",
        TrophyTier.Silver => "Plata",
        _ => "Bronce",
    };

    /// <summary>El trofeo del color de su nivel (el dibujo en gris, teñido); el platino es el de Steam, en color.</summary>
    static FrameworkElement TierTrophy(TrophyTier tier, double height, bool lit)
    {
        if (tier == TrophyTier.Platinum && lit)
            return new Image { Source = TrophyColor, Height = height, Stretch = Stretch.Uniform };
        var (dark, light) = TierColors(tier);
        var aspect = TrophyGray.Width / TrophyGray.Height;
        var paint = new Grid { Width = height * aspect, Height = height };
        // El dibujo en gris da el relieve; encima, el color del metal con la forma del trofeo.
        paint.Children.Add(new Image { Source = TrophyGray, Stretch = Stretch.Fill });
        paint.Children.Add(new Border
        {
            Background = new LinearGradientBrush(light, dark, 70) { Opacity = lit ? 0.85 : 0.35 },
            OpacityMask = new ImageBrush(TrophyGray) { Stretch = Stretch.Fill },
        });
        if (!lit)
            paint.Opacity = 0.6;
        return paint;
    }

    void FillAchievementsPage(Game game, List<AchievementInfo> list)
    {
        if (list.Count == 0)
        {
            AchievementsList.Children.Clear();
            AchievementsPageSummary.Text = "Este juego no tiene logros en Steam.";
            return;
        }
        FillTrophySections(game.Name, list.Select(a => (a, a.Tier, (OwnTrophy?)null)).ToList(), "logros", byPercent: true);
    }

    static FrameworkElement Section(string title, string? detail)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 22, 0, 8) };
        header.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
        if (detail is not null)
            header.Children.Add(new TextBlock { Text = "   " + detail, FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA3, 0xBF)), VerticalAlignment = VerticalAlignment.Bottom });
        return header;
    }

    /// <summary>Un logro: su icono, nombre, descripción y trofeo. Conseguido, brilla del color de su trofeo; si no, en gris.</summary>
    /// <param name="compare">Quién de la familia lo tiene (al comparar un juego que tenéis varios), o null.</param>
    static FrameworkElement Card(AchievementInfo achievement, TrophyTier tier, bool wide, string? compare = null)
    {
        var (dark, light) = TierColors(tier);
        var lit = achievement.Unlocked;
        var card = new Border
        {
            Width = wide ? 872 : 430,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(6),
            Padding = new Thickness(14, 12, 14, 12),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(2),
            BorderBrush = lit ? new LinearGradientBrush(light, dark, 45) : new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            Background = lit
                ? new LinearGradientBrush(Color.FromArgb(0x55, dark.R, dark.G, dark.B), Color.FromArgb(0x18, light.R, light.G, light.B), 0)
                : new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            Effect = lit ? new System.Windows.Media.Effects.DropShadowEffect { Color = light, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.55 } : null,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Icono del logro (el de Steam, en color o en gris); el platino lleva el trofeo grande.
        FrameworkElement icon;
        var url = lit ? achievement.Icon : achievement.IconGray ?? achievement.Icon;
        if (url is not null)
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(url);
            image.DecodePixelWidth = 128;
            image.EndInit();
            icon = new Border
            {
                Width = 64, Height = 64, CornerRadius = new CornerRadius(6), ClipToBounds = true,
                Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill },
                Opacity = lit ? 1 : 0.55,
            };
        }
        else
        {
            icon = TierTrophy(tier, 64, lit);
        }
        icon.Margin = new Thickness(0, 0, 14, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 1);
        text.Children.Add(new TextBlock
        {
            Text = achievement.Name, FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = lit ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xA8, 0xB0, 0xBA)),
        });
        var description = achievement.Description.Length > 0 ? achievement.Description : achievement.Hidden ? "Logro oculto" : "";
        if (description.Length > 0)
        {
            text.Children.Add(new TextBlock
            {
                Text = description, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), MaxHeight = 36,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(lit ? Color.FromRgb(0xE6, 0xEE, 0xF5) : Color.FromRgb(0x9A, 0xA6, 0xB4)),
            });
        }
        var details = new List<string> { TierName(tier) };
        if (achievement.GlobalPercent is { } global)
            details.Add($"{global.ToString(global < 1 ? "0.0" : "0", System.Globalization.CultureInfo.CurrentCulture)} % de los jugadores");
        if (achievement.UnlockedAt is { } when && lit)
            details.Add($"conseguido el {when:dd/MM/yyyy}");
        else if (!lit)
            details.Add("sin conseguir");
        text.Children.Add(new TextBlock
        {
            Text = string.Join("  ·  ", details), FontSize = 12, Margin = new Thickness(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap,
            Foreground = lit ? new SolidColorBrush(light) : new SolidColorBrush(Color.FromRgb(0x7D, 0x87, 0x93)),
        });
        if (compare is not null)
        {
            text.Children.Add(new TextBlock
            {
                Text = compare, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xC8, 0xFF)),
            });
        }
        grid.Children.Add(text);

        var badge = TierTrophy(tier, 40, lit);
        badge.Margin = new Thickness(12, 0, 0, 0);
        badge.VerticalAlignment = VerticalAlignment.Center;
        if (lit)
            badge.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = light, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.9 };
        Grid.SetColumn(badge, 2);
        grid.Children.Add(badge);

        card.Child = grid;
        return card;
    }

    // ---- Atajos de teclado ----

    void OnShortcutsClick(object sender, RoutedEventArgs e)
    {
        // El menú va escalado como el resto de la interfaz (un Popup no hereda la escala de la ventana).
        ShortcutsCard.LayoutTransform = _uiScale;
        ShortcutsPopup.IsOpen = ShortcutsButton.IsChecked == true;
    }

    void OnShortcutsClosed(object? sender, EventArgs e) => ShortcutsButton.IsChecked = false;

    string? _lookBackground;

    /// <summary>
    /// Fondo y color de letras del juego seleccionado (o los de siempre). El fondo cambia con un fundido y solo
    /// se vuelve a cargar si es otro archivo.
    /// </summary>
    void ApplyLook(Game? game)
    {
        var hexColor = game?.TextColor ?? _settings.TextColor;
        var color = hexColor is { } hex && ColorConverter.ConvertFromString(hex) is Color custom ? custom : ThemeManager.DefaultText;
        Resources["UiText"] = new SolidColorBrush(color);
        // Con letras claras, el halo alrededor del título pasa a ser oscuro para que se lean.
        var light = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255 > 0.6;
        var glow = light ? Colors.Black : Colors.White;
        if (TitleText.Effect is System.Windows.Media.Effects.DropShadowEffect titleGlow)
            titleGlow.Color = glow;

        // El fondo propio del juego manda; si no tiene, el general.
        var own = game is null ? null : ImageStore.GetBackgroundPath(game);
        if (own is not null && !File.Exists(own))
            own = null;
        var path = own ?? ImageStore.GetGlobalBackgroundPath(_settings);
        if (path is not null && !File.Exists(path))
            path = null;
        var isVideo = own is not null ? game!.BackgroundIsVideo : _settings.BackgroundIsVideo;
        if (path == _lookBackground)
            return;
        _lookBackground = path;

        // Vídeo en un códec que el reproductor no abre (VP9, AV1, HEVC…): se convierte una vez y se usa el convertido.
        if (path is not null && isVideo && !VideoConverter.IsPlayable(path))
        {
            _ = ConvertBackgroundAsync(path, own is not null ? game : null);
            return;
        }

        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(path is null ? 300 : 140));
        fadeOut.Completed += (_, _) =>
        {
            if (_lookBackground != path)
                return;
            BackgroundVideo.Stop();
            BackgroundVideo.Source = null;
            BackgroundImage.Source = null;
            Floor.Opacity = path is null ? 1 : 0.35;
            if (path is null)
                return;

            if (isVideo)
            {
                BackgroundVideo.Source = new Uri(path);
                BackgroundVideo.Play();
                // Fondos quietos: se enseña el primer fotograma y se para (gasta mucho menos procesador).
                if (!_settings.AnimatedBackgrounds)
                    PauseVideoWhenReady();
            }
            else
            {
                BackgroundImage.Source = CoverArt.LoadImage(path, 2560);
            }
            BackgroundLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(320)));
        };
        BackgroundLayer.BeginAnimation(OpacityProperty, fadeOut);
    }

    void PauseVideoWhenReady()
    {
        void Opened(object? sender, RoutedEventArgs e)
        {
            BackgroundVideo.MediaOpened -= Opened;
            if (!_settings.AnimatedBackgrounds)
                BackgroundVideo.Pause();
        }
        BackgroundVideo.MediaOpened += Opened;
    }

    readonly HashSet<string> _converting = [];

    /// <summary>
    /// Convierte a H.264 un vídeo de fondo que no se puede reproducir, sustituye el archivo guardado por el
    /// convertido (del juego o el general) y lo enseña. Solo pasa la primera vez.
    /// </summary>
    async Task ConvertBackgroundAsync(string path, Game? owner)
    {
        if (!_converting.Add(path))
            return;
        ShowToast("Preparando el vídeo de fondo (solo la primera vez)…");
        var converted = await VideoConverter.ConvertAsync(path);
        _converting.Remove(path);

        if (converted is null)
        {
            ShowToast("Este vídeo no se puede reproducir: Windows no tiene su códec. Prueba con otro (por ejemplo MP4 en H.264)");
            return;
        }

        var fileName = Path.GetFileName(converted);
        if (owner is not null)
        {
            owner.Background = fileName;
            Save();
        }
        else
        {
            _settings.Background = fileName;
            _settings.Save();
        }
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        _lookBackground = null;
        ShowDetails(_flow.SelectedIndex);
    }

    void OnBackgroundVideoFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        BackgroundVideo.Source = null;
        ShowToast("No se ha podido reproducir el vídeo de fondo. Prueba con otro (por ejemplo MP4 en H.264)");
    }

    void OnBackgroundVideoEnded(object sender, RoutedEventArgs e)
    {
        BackgroundVideo.Position = TimeSpan.Zero;
        BackgroundVideo.Play();
    }

    // ---- Tiempo de juego ----

    /// <summary>
    /// Partida en curso. Se cuenta mientras el juego siga abierto (algún proceso desde su carpeta, o el que se lanzó),
    /// aunque vuelvas a Basilisco o lo cierres y lo abras otra vez. Si no se puede saber (un enlace sin carpeta, o un
    /// lanzador que se cierra al momento), se acaba al volver a Basilisco, como antes.
    /// </summary>
    sealed class PlaySession(Game game, DateTime started, string? folder, int? processId)
    {
        public Game Game { get; } = game;
        public DateTime Started { get; } = started;
        public string? Folder { get; } = folder;
        public int? ProcessId { get; } = processId;
        public DateTime LastSeen { get; set; } = started;
        public bool SeenRunning { get; set; }

        /// <summary>True mientras se sigue al juego por sus procesos; false: se acaba al volver a Basilisco.</summary>
        public bool Tracking { get; set; } = folder is not null || processId is not null;

        public SavedSession ToSaved() => new(Game.Id, Started, LastSeen, Folder, ProcessId);
    }

    // Al lanzar un juego, lo que puede tardar en aparecer su proceso (lanzadores, Steam…).
    static readonly TimeSpan LaunchGrace = TimeSpan.FromMinutes(2);

    PlaySession? _session;
    readonly System.Windows.Threading.DispatcherTimer _playTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    bool _polling;
    DateTime _sessionSaved;

    void StartPlaySession(Game game, System.Diagnostics.Process? process)
    {
        EndPlaySession(returned: false);
        int? processId = null;
        try
        {
            processId = process?.Id;
        }
        catch (InvalidOperationException)
        {
        }
        process?.Dispose();
        BeginTracking(new PlaySession(game, DateTime.Now, GameProcesses.FolderOf(game), processId));
        // Si el juego trae sus propios logros, los que desbloquees pasan a su perfil al momento.
        WatchGameAchievements(game);
    }

    void BeginTracking(PlaySession session)
    {
        _session = session;
        session.ToSaved().Save();
        _sessionSaved = DateTime.Now;
        _playTimer.Start();
    }

    /// <summary>Cada pocos segundos: ¿sigue abierto el juego? Cuando se cierra, se apunta lo jugado hasta la última vez que se vio.</summary>
    async Task PollPlaySessionAsync()
    {
        if (_polling || _session is not { } session)
            return;
        _polling = true;
        try
        {
            var running = session.Tracking && await Task.Run(() => GameProcesses.IsRunning(session.Folder, session.ProcessId));
            if (_session != session)
                return;

            var now = DateTime.Now;
            if (!session.Tracking || running)
            {
                // Sin seguimiento, «visto» es que Basilisco sigue abierto: si se cierra, se cuenta hasta ese momento.
                session.LastSeen = now;
                session.SeenRunning |= running;
            }
            else if (session.SeenRunning)
            {
                EndPlaySession(returned: false, end: session.LastSeen);
                return;
            }
            else if (session.Folder is null && now - session.Started < TimeSpan.FromSeconds(20) || now - session.Started > LaunchGrace)
            {
                // Un lanzador que se cierra al momento, o un juego que no aparece: se acaba al volver a Basilisco.
                session.Tracking = false;
                session.LastSeen = now;
            }

            if (now - _sessionSaved > TimeSpan.FromMinutes(1))
            {
                session.ToSaved().Save();
                _sessionSaved = now;
            }
        }
        finally
        {
            _polling = false;
        }
    }

    /// <summary>
    /// El juego se ha cerrado: si se lanzó desde la caja abierta, Basilisco vuelve al frente y el disco frena poco a poco
    /// hasta pararse, y la tapa se cierra.
    /// </summary>
    void GameClosed(Game game)
    {
        if (!CaseView.IsPlaying || CaseView.Game != game)
            return;
        if (WindowState == WindowState.Minimized)
            WindowState = _fullScreen ? WindowState.Maximized : WindowState.Normal;
        Activate();
        CaseView.GameEnded();
    }

    /// <summary>Al abrir Basilisco: si quedó una partida a medias, se sigue contando (si el juego sigue abierto) o se apunta.</summary>
    async Task RestorePlaySessionAsync()
    {
        if (SavedSession.Load() is not { } saved)
            return;
        if (Games.FirstOrDefault(game => game.Id == saved.GameId) is not { } game)
        {
            SavedSession.Clear();
            return;
        }

        var session = new PlaySession(game, saved.Started, saved.Folder, null) { LastSeen = saved.LastSeen, SeenRunning = true };
        if (saved.Folder is not null && await Task.Run(() => GameProcesses.IsRunning(saved.Folder, null)))
        {
            session.LastSeen = DateTime.Now;
            BeginTracking(session);
            ShowToast($"«{game.Name}» sigue abierto: se sigue contando el tiempo");
            return;
        }

        _session = session;
        EndPlaySession(returned: false, end: saved.LastSeen);
    }

    /// <param name="returned">True si se acaba porque has vuelto a Basilisco (los primeros segundos no cuentan: aún carga el juego).</param>
    /// <param name="end">Cuándo se cerró el juego; si no, ahora.</param>
    void EndPlaySession(bool returned, DateTime? end = null)
    {
        if (_session is not { } session)
            return;
        // Si se le está siguiendo, volver a Basilisco no lo acaba: se espera a que se cierre el juego.
        if (returned && session.Tracking)
            return;
        var elapsed = (end ?? DateTime.Now) - session.Started;
        if (returned && elapsed < TimeSpan.FromSeconds(20))
            return;

        _session = null;
        _playTimer.Stop();
        SavedSession.Clear();
        GameClosed(session.Game);
        WatchGameAchievements(null);
        if (session.Game.Trophies?.Source == TrophySource.GameFiles)
            SyncGameAchievements(session.Game);
        var minutes = (int)Math.Max(0, elapsed.TotalMinutes);
        if (minutes < 1 || !Games.Contains(session.Game))
            return;

        // Los de Steam no se cuentan aquí: llevan las horas de Steam, que las apunta al cerrar el juego.
        if (session.Game.SteamAppId is not null)
        {
            _ = SyncAfterSteamSessionAsync(session.Game);
            return;
        }

        session.Game.PlayMinutes += minutes;
        Save();
        if (SelectedGame == session.Game)
            ShowDetails(_flow.SelectedIndex);
        ShowToast($"{session.Game.Name}: +{(minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h {minutes % 60} min")} de juego");
    }

    /// <summary>Steam tarda unos segundos en apuntar las horas al cerrar el juego: se miran ahora y otra vez al rato.</summary>
    async Task SyncAfterSteamSessionAsync(Game game)
    {
        var before = game.PlayMinutes;
        foreach (var delay in new[] { 5, 60 })
        {
            await Task.Delay(TimeSpan.FromSeconds(delay));
            await SyncSteamPlaytimeAsync(online: true);
            if (game.PlayMinutes > before)
            {
                var added = game.PlayMinutes - before;
                ShowToast($"{game.Name}: +{(added < 60 ? $"{added} min" : $"{added / 60} h {added % 60} min")} de juego (Steam)");
                return;
            }
        }
    }

    List<string> DescribeGame(Game game)
    {
        var chips = new List<string>
        {
            game.SteamAppId is { } appId
                ? (SteamLocal.IsInstalled(appId) ? "Steam · instalado" : "Steam · no instalado")
                : Path.GetExtension(game.Path).ToLowerInvariant() switch
            {
                ".exe" => "Ejecutable",
                ".lnk" => "Acceso directo",
                ".url" => "Enlace",
                ".bat" or ".cmd" => "Script",
                _ => "Archivo",
            },
            // El tiempo de juego (también el de Steam); sin tiempo, cuántas veces se ha abierto.
            game.PlayMinutes > 0 ? PlayTimeLabel(game.PlayMinutes) : game.PlayCount switch
            {
                0 => "Nunca jugado",
                1 => "Jugado 1 vez",
                var n => $"Jugado {n} veces",
            },
        };

        if (game.LastPlayed is { } last)
        {
            chips.Add((DateTime.Today - last.Date).Days switch
            {
                0 => "Hoy",
                1 => "Ayer",
                _ => last.ToString("dd/MM/yyyy"),
            });
        }

        if (_activePlatform is null)
            chips.Add(Platforms.Find(game.Platform)?.Name ?? "Sin consola");

        // En «Todos los juegos» enseñamos a qué filtros pertenece; dentro de un filtro ya se sabe.
        if (_activeFilter is null)
        {
            chips.AddRange(Filters.Where(filter => game.FilterIds.Contains(filter.Id)).Select(filter => filter.Name));
        }

        // Steam familiar: quién lo tiene (si son varios, todos).
        var owners = _library.Family.Count > 0 ? _library.Family.Where(member => member.Owns(game)).ToList() : [];
        if (owners.Count > 1 || owners.Count == 1 && !owners[0].IsMe)
            chips.Add("Steam Familiar · de " + string.Join(", ", owners.Select(member => member.IsMe ? "ti" : member.Name)));
        else if (owners.Count == 0 && game.SteamOwner is { } owner)
            chips.Add($"Steam Familiar · de {owner}");

        var folder = GameLauncher.IsLink(game.Path) ? null : Path.GetFileName(Path.GetDirectoryName(game.Path));
        if (!string.IsNullOrEmpty(folder))
            chips.Add($"Carpeta: '{folder}'");

        if (Emulators.Find(game.Emulator) is { } emulator)
            chips.Add($"Emulador: {emulator.Name}");
        else if (StoreLibraries.Find(game.Store) is { } store)
            chips.Add(store.Name);

        if (!GameLauncher.IsLink(game.Path) && !File.Exists(GameLauncher.FileOf(game)))
            chips.Add("Archivo no encontrado");

        return chips;
    }

    void UpdateOverlayLayout()
    {
        if (Viewport.ActualWidth <= 0)
            return;

        // El título va siempre en el mismo sitio, centrado justo debajo de la cabecera: no depende del zoom
        // ni del tamaño de la caja seleccionada.
        var scale = _uiScale.ScaleX;
        TitlePanel.Margin = new Thickness(0, HeaderBar.Margin.Top + HeaderBar.ActualHeight * scale + 14 * scale, 0, 0);
        if (_settings.CarouselAutoShift)
        {
            // Ni el título ni los controles de los lados (alineación, altura, zoom) pisan las cajas.
            var controlsBottom = HeaderBar.Margin.Top + (HeaderBar.ActualHeight + 12 +
                                                         Math.Max(CarouselControlsLeft.ActualHeight, CarouselControlsRight.ActualHeight) + 16) * scale;
            FitBelowTitle(Math.Max(TitlePanel.Margin.Top + TitlePanel.ActualHeight * scale + 22 * scale, controlsBottom));
        }

        // El suelo empieza por detrás de las cajas laterales para que todas apoyen sobre él.
        var horizon = _flow.Project(new Point3D(0, 0, -2.5));
        Floor.Margin = new Thickness(0, Math.Max(0, horizon.Y - 40), 0, 0);

        // Los controles del carrusel, debajo de la cabecera: la alineación y la altura a la izquierda, la descarga y el zoom a la derecha.
        var controlsTop = HeaderBar.Margin.Top + HeaderBar.ActualHeight * scale + 12 * scale;
        CarouselControlsLeft.Margin = new Thickness(HeaderBar.Margin.Left, controlsTop, 0, 0);
        CarouselControlsRight.Margin = new Thickness(0, controlsTop, HeaderBar.Margin.Right, 0);
    }

    void Activate(int index)
    {
        if (IsAddCard(index))
            Import();
        else
        {
            // Primero se abre la caja (manual y disco); desde ahí, Jugar lanza el juego.
            var game = _visibleGames[index];
            CaseView.Open(game, ToCarouselItem(game), _padMode);
        }
    }

    void Import()
    {
        var dialog = new OpenFileDialog
        {
            Title = ActiveViewName is { } view ? $"Importar juego a «{view}»" : "Importar juego",
            Filter = GameLauncher.FileFilter,
            Multiselect = true,
            // Queremos el acceso directo tal cual, no el archivo al que apunta, para conservar sus argumentos.
            DereferenceLinks = false,
        };

        if (dialog.ShowDialog(this) == true)
            AddFiles(dialog.FileNames);
    }

    void ImportFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Importar carpeta de juegos" };
        if (dialog.ShowDialog(this) == true)
            OpenFolderImport(dialog.FolderName);
    }

    void OpenFolderImport(string folder) =>
        FolderImporter.Open(folder, StatusOf, ActiveViewName);

    bool IsInLibrary(string path) =>
        Games.Any(game => string.Equals(game.Path, path, StringComparison.OrdinalIgnoreCase));

    void ApplyFolderImport(FolderImportResult result)
    {
        GameFilter? groupFilter = null;
        if (result.NewFilterName is { } name)
        {
            // Si ya hay un filtro con ese nombre, se reutiliza en lugar de duplicarlo.
            groupFilter = Filters.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.CurrentCultureIgnoreCase));
            if (groupFilter is null)
            {
                groupFilter = new GameFilter { Name = name };
                Filters.Add(groupFilter);
            }
        }

        AddGames(result.Games.Select(game => (game.Path, (string?)game.Name)), groupFilter);
    }

    void AddFiles(IEnumerable<string> paths)
    {
        var files = paths.Where(File.Exists).Select(path => (path, (string?)null)).ToList();
        if (files.Count > 0)
            AddGames(files);
    }

    /// <param name="games">Ruta de cada juego y, si se conoce, su nombre (si no, el del archivo).</param>
    /// <param name="groupFilter">Filtro extra al que añadir los juegos, además del seleccionado.</param>
    /// <param name="configure">Ajustes extra para cada juego nuevo (por ejemplo, sus datos de Steam).</param>
    /// <param name="askPlatform">Preguntar después la consola de cada juego (no hace falta con los de Steam, que son de PC).</param>
    void AddGames(IEnumerable<(string Path, string? Name)> games, GameFilter? groupFilter = null,
                  Action<Game>? configure = null, bool askPlatform = true)
    {
        Game? first = null;
        var added = 0;
        var relinked = 0;
        var imported = new List<Game>();

        foreach (var (path, name) in games)
        {
            var game = Games.FirstOrDefault(g => SamePath(g.Path, path));
            if (game is null && FindMissingWithSameFile(path) is { } moved)
            {
                // Ya lo tenías, pero en otra carpeta: se actualiza su ubicación en lugar de duplicarlo.
                Relink(moved, path);
                game = moved;
                relinked++;
            }
            else if (game is null)
            {
                game = new Game
                {
                    Name = name ?? Path.GetFileNameWithoutExtension(path),
                    Path = path,
                    AddedAt = DateTime.Now,
                };
                configure?.Invoke(game);
                Games.Add(game);
            }
            else if (BelongsToActiveFilter(game) && (groupFilter is null || game.FilterIds.Contains(groupFilter.Id)))
            {
                continue;
            }

            // Lo que se importa dentro de un filtro o de una consola entra directamente en él.
            foreach (var filter in new[] { _activeFilter, groupFilter })
            {
                if (filter is not null && !game.FilterIds.Contains(filter.Id))
                    game.FilterIds.Add(filter.Id);
            }
            if (_activePlatform is not null)
                game.Platform = _activePlatform;

            imported.Add(game);
            first ??= game;
            added++;
        }

        if (first is null)
        {
            ShowToast("Ya estaba en tu biblioteca");
            return;
        }

        Save();
        RefreshFilterTabs();
        RefreshCarousel(0);
        _flow.Select(_visibleGames.IndexOf(first));

        var target = (ActiveViewName ?? groupFilter?.Name) is { } shown ? $"«{shown}»" : "la biblioteca";
        var message = added == 1 ? $"«{first.Name}» añadido a {target}" : $"{added} juegos añadidos a {target}";
        if (relinked > 0)
            message += relinked == 1 ? " (1 ya lo tenías en otra carpeta)" : $" ({relinked} ya los tenías en otra carpeta)";
        ShowToast(message);

        // Y ahora, de qué consola es cada uno (decide el borde de su caja).
        if (askPlatform)
            PlatformPicker.Open(imported, _activePlatform);
    }

    void OpenSteamImport() => SteamImporter.Open(_settings, IsSteamAppInLibrary);

    /// <summary>
    /// «Importación Juegos NO PC»: la misma franja negra que la de los filtros, con Steam, las tiendas de PC y los
    /// emuladores (diciendo cuáles están en este PC). Al elegir uno se abre su importación.
    /// </summary>
    void OpenImportMenu()
    {
        string Count(Func<Game, bool> belongs) => Games.Count(belongs) switch { 0 => "", 1 => " · 1 juego", var n => $" · {n} juegos" };
        var tiles = new List<FilterTile>
        {
            new("Steam", null, "Instalados, biblioteca y Steam Familiar" + Count(game => game.SteamAppId is not null), false, OpenSteamImport),
        };
        foreach (var store in StoreLibraries.All)
        {
            var chosen = store;
            tiles.Add(new FilterTile(store.Name, null, "PC" + Count(game => game.Store == chosen.Id), false,
                () => LauncherImporter.OpenStore(chosen, _settings, IsImportedInLibrary)));
        }
        foreach (var emulator in Emulators.All)
        {
            var chosen = emulator;
            var installed = Emulators.FindExe(emulator, _settings) is not null;
            tiles.Add(new FilterTile(emulator.Name, null, $"{emulator.Console} · {(installed ? "en este PC" : "no encontrado")}" + Count(game => game.Emulator == chosen.Id),
                false, () => LauncherImporter.OpenEmulator(chosen, _settings, IsImportedInLibrary)));
        }
        var scale = _uiScale.ScaleX;
        var top = (HeaderBar.Margin.Top + HeaderBar.ActualHeight * scale) / scale + 6;
        FilterStrip.Open(tiles, top, _padMode, "IMPORTACIÓN JUEGOS NO PC");
    }

    bool IsImportedInLibrary(ImportedGame found) => Games.Any(game => SamePath(game.Path, found.Path));

    /// <summary>Añade los juegos de un emulador o una tienda, con su consola, su emulador (y núcleo) o su tienda.</summary>
    void ApplyLauncherImport(LauncherImportResult result)
    {
        var byPath = result.Games.GroupBy(game => game.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        AddGames(result.Games.Select(game => (game.Path, (string?)game.Name)), configure: game =>
        {
            if (!byPath.TryGetValue(game.Path, out var found))
                return;
            game.Emulator = result.Emulator?.Id;
            game.EmulatorCore = found.Core;
            game.Store = result.Store?.Id;
            game.InstallDir = found.InstallDir;
            game.Platform ??= found.Platform ?? result.Emulator?.Platform;
            // Los juegos de consola se ven mejor en su caja de siempre (rectangular, con la franja de la consola).
            if (game.Platform is { } platform && platform != "pc")
                game.Format = CoverFormat.Portrait;
        }, askPlatform: result.Games.Any(game => game.Platform is null) && result.Emulator?.Platform is null && result.Store is null);
    }

    /// <summary>
    /// Un juego de Steam ya está en la biblioteca si se importó desde Steam o si ya tenías su ejecutable
    /// (importado de la carpeta del juego dentro de steamapps).
    /// </summary>
    bool IsSteamAppInLibrary(SteamApp app) =>
        Games.Any(game => game.SteamAppId == app.AppId || SamePath(game.Path, app.LaunchUri) ||
                          app.InstallDir is { } dir && game.Path.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));

    void ApplySteamImport(SteamImportResult result)
    {
        var byLink = result.Apps.ToDictionary(app => app.LaunchUri, StringComparer.OrdinalIgnoreCase);

        AddGames(result.Apps.Select(app => (app.LaunchUri, (string?)app.Name)), configure: game =>
        {
            if (!byLink.TryGetValue(game.Path, out var app))
                return;
            game.SteamAppId = app.AppId;
            game.SteamOwner = app.Source == SteamSource.Family ? app.Owner ?? "otro miembro" : null;
            game.PlayMinutes = Math.Max(game.PlayMinutes, app.PlaytimeMinutes);
            game.Platform ??= "pc";
            if (app.OwnerIds is { Count: > 0 } owners)
                game.SteamOwnerIds = [.. owners];
        }, askPlatform: false);

        // Los instalados y los de la familia no traen las horas: se leen del Steam de este PC.
        _ = SyncSteamPlaytimeAsync(online: false);
    }

    /// <summary>El filtro con ese nombre; si no existe, se crea.</summary>
    GameFilter FilterNamed(string name)
    {
        var filter = Filters.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (filter is null)
        {
            filter = new GameFilter { Name = name };
            Filters.Add(filter);
        }
        return filter;
    }

    void ApplyPlatforms(List<(Game Game, string? Platform)> choices)
    {
        var selected = SelectedGame;
        foreach (var (game, platform) in choices)
            game.Platform = platform;

        Save();
        RefreshKeeping(selected);

        var assigned = choices.Where(choice => choice.Platform is not null).Select(choice => choice.Platform).Distinct().ToList();
        ShowToast(assigned.Count == 1 && Platforms.Find(assigned[0]) is { } only
            ? $"Consola guardada: {only.Name}"
            : "Consolas guardadas");
    }

    void Launch(Game game)
    {
        if (!GameLauncher.IsLink(game.Path) && !File.Exists(GameLauncher.FileOf(game)))
        {
            // Lo más probable es que hayas movido su carpeta: primero lo buscamos nosotros.
            if (GameRelocator.FindMoved(game.Path) is { } moved)
            {
                Relink(game, moved);
                Save();
                RefreshKeeping(game);
            }
            else
            {
                var answer = MessageBox.Show(this,
                    $"No se encuentra «{game.Name}» en:\n\n{game.Path}\n\n¿Se ha movido? ¿Quieres buscarlo tú?",
                    "Basilisco", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer == MessageBoxResult.Yes)
                    LocateManually(game);
                return;
            }
        }

        try
        {
            StartPlaySession(game, GameLauncher.Launch(game));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"No se pudo iniciar «{game.Name}».\n\n{ex.Message}",
                "Basilisco", MessageBoxButton.OK, MessageBoxImage.Error);
            CaseView.GameEnded();
            return;
        }

        game.LastPlayed = DateTime.Now;
        game.PlayCount++;
        Save();
        ShowDetails(_flow.SelectedIndex);
        ShowToast($"Iniciando {game.Name}…");
    }

    void RemoveSelected()
    {
        var index = _flow.SelectedIndex;
        if (IsAddCard(index))
            return;

        var game = _visibleGames[index];
        var answer = MessageBox.Show(this, $"¿Quitar «{game.Name}» de la biblioteca?\n\nEl juego no se borrará de tu disco.",
            "Quitar juego", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        ImageStore.RemoveAll(game);
        Games.Remove(game);
        Save();
        RefreshFilterTabs();
        RefreshCarousel(index);
    }

    void OpenCustomizer(int index)
    {
        if (!IsAddCard(index))
            Customizer.Open(_visibleGames[index], Filters);
    }

    void ApplyCustomization(CustomizeResult result)
    {
        var game = result.Game;
        var index = _visibleGames.IndexOf(game);
        if (index < 0)
            return;

        game.Name = result.Name;
        game.ReleaseDate = result.ReleaseDate;
        // A mano manda; en blanco se vuelve a buscar en LaunchBox la próxima vez.
        if (result.Players != game.Players)
        {
            game.Players = result.Players;
            game.PlayersLooked = result.Players is not null;
        }
        game.TextColor = result.TextColor;
        game.PlayMinutes = result.PlayMinutes;
        game.ManualFrame = result.ManualFrame;
        game.InsideFrame = result.InsideFrame;
        game.DiscFrame = result.DiscFrame;
        try
        {
            if (result.Background.NewPath is { } background)
                ImageStore.SetBackground(game, background);
            else if (result.Background.Remove)
                ImageStore.RemoveBackground(game);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        foreach (var (extra, change) in new[] { (GameExtra.Disc, result.Disc), (GameExtra.Manual, result.Manual), (GameExtra.Inside, result.Inside) })
        {
            try
            {
                if (change.NewPath is { } extraPath)
                    ImageStore.SetExtra(game, extra, extraPath);
                else if (change.Remove)
                    ImageStore.RemoveExtra(game, extra);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        try
        {
            if (result.ManualPdf.NewPath is { } manualPdf)
                _ = StoreManualAsync(game, manualPdf, coverAsImage: result.Manual.NewPath is null);
            else if (result.ManualPdf.Remove)
                ImageStore.RemoveManualPdf(game);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        _lookBackground = "\0"; // Fuerza a recargar el fondo aunque el archivo se llame igual.
        game.Platform = result.Platform;
        game.Format = result.Format;
        game.FilterIds = result.FilterIds;
        var message = "Cambios guardados";
        try
        {
            // Las imágenes de cada formato van por separado: se guardan las de los dos.
            foreach (var (format, pending) in result.Faces)
            {
                foreach (var face in Enum.GetValues<Face>())
                {
                    var change = pending.Change(face);
                    if (change.NewPath is { } path)
                        ImageStore.Set(game, format, face, path);
                    else if (change.Remove)
                        ImageStore.Remove(game, format, face);

                    if (format == CoverFormat.Square || game.Portrait is not null)
                        game.Faces(format).SetFrame(face, pending.Frame(face));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = "No se pudo copiar la imagen; el resto de cambios sí se han guardado";
        }

        var relinked = result.NewPath is { } newPath && !SamePath(newPath, game.Path);
        if (relinked)
            Relink(game, result.NewPath!);

        Save();
        RefreshFilterTabs();

        if (relinked)
        {
            // Al cambiar de ubicación puede haberse juntado con un duplicado: hay que rehacer el carrusel.
            RefreshKeeping(game);
        }
        else if (BelongsToActiveFilter(game))
        {
            _flow.UpdateItem(index, ToCarouselItem(game));
            ShowDetails(index);
        }
        else
        {
            // Lo has sacado del filtro o de la consola que estás viendo: desaparece del carrusel.
            RefreshCarousel(index);
            message = $"«{game.Name}» ya no está en «{ActiveViewName}»";
        }
        ShowToast(message);
    }

    /// <summary>
    /// Busca en internet una imagen para el juego que se está personalizando: la portada en SteamGridDB,
    /// y la contraportada y el lomo (a partir de sus banners) en TheGamesDB. O en LaunchBox, o el lomo en The Spine Archive.
    /// </summary>
    void OpenArtSearch(string gameName, ArtTarget target, CoverFormat format, string? platform, ArtSource source)
    {
        if (source == ArtSource.SpineArchive)
        {
            ArtSearch.Open(_spineArchive, _settings, gameName, Customizer.SetSpineImage);
            return;
        }
        // El librito del manual desde las carátulas (grids) de SteamGridDB.
        if (source == ArtSource.SteamGridDb && target == ArtTarget.Manual)
        {
            ArtSearch.Open(_steamGridDbManual, _settings, gameName, Customizer.SetManualImage);
            return;
        }
        // La pegatina de un cartucho desde los grids apaisados 92:43 de SteamGridDB.
        if (source == ArtSource.SteamGridDb && target == ArtTarget.Disc)
        {
            ArtSearch.Open(_steamGridDbCartridge, _settings, gameName, Customizer.SetDiscImage);
            return;
        }

        Action<string> onChosen = target switch
        {
            ArtTarget.Front => Customizer.SetFrontImage,
            ArtTarget.Back => Customizer.SetBackImage,
            ArtTarget.Spine => Customizer.SetSpineImage,
            ArtTarget.Disc => Customizer.SetDiscImage,
            ArtTarget.Background => Customizer.SetBackgroundImage,
            ArtTarget.Inside => Customizer.SetInsideImage,
            ArtTarget.ManualPdf => Customizer.SetManualPdfFile,
            _ => Customizer.SetManualImage,
        };

        if (target == ArtTarget.ManualPdf)
        {
            _manuals.PlatformHint = platform;
            ArtSearch.Open(_manuals, _settings, gameName, onChosen);
            return;
        }

        if (target is ArtTarget.Background or ArtTarget.Inside)
        {
            ArtSearch.Open(target == ArtTarget.Inside ? _steamGridDbInside : _steamGridDbHeroes, _settings, gameName, onChosen);
            return;
        }

        if (source == ArtSource.SteamGameCovers)
        {
            // Una carátula completa trae portada, lomo y contraportada: las caras vacías se rellenan con el resto.
            var kind = target switch
            {
                ArtTarget.Front => ArtKind.Front,
                ArtTarget.Back => ArtKind.Back,
                ArtTarget.Spine => ArtKind.Spine,
                _ => ArtKind.Disc,
            };
            var covers = new SteamGameCoversProvider(kind);
            ArtSearch.Open(covers, _settings, gameName, path =>
            {
                onChosen(path);
                if (covers.LastParts is { } parts)
                    Customizer.FillEmptyFaces(parts);
            });
            return;
        }

        if (target is ArtTarget.Disc or ArtTarget.Manual)
        {
            var provider = target == ArtTarget.Disc ? _launchBoxDisc : _launchBoxManual;
            provider.PlatformHint = platform;
            ArtSearch.Open(provider, _settings, gameName, onChosen);
            return;
        }

        if (source == ArtSource.LaunchBox)
        {
            // LaunchBox: la segunda opción, con fotos reales de las tres caras de la caja.
            var provider = target switch
            {
                ArtTarget.Front => _launchBoxFront,
                ArtTarget.Back => _launchBoxBack,
                _ => _launchBoxSpine,
            };
            provider.PlatformHint = platform;
            ArtSearch.Open(provider, _settings, gameName, onChosen);
            return;
        }

        switch (target)
        {
            case ArtTarget.Front:
                ArtSearch.Open(format == CoverFormat.Portrait ? _steamGridDbPortrait : _steamGridDb, _settings, gameName, Customizer.SetFrontImage);
                break;
            case ArtTarget.Back: ArtSearch.Open(_theGamesDb, _settings, gameName, Customizer.SetBackImage); break;
            case ArtTarget.Spine: ArtSearch.Open(_theGamesDbBanners, _settings, gameName, Customizer.SetSpineImage); break;
        }
    }

    MetadataFetcher? _metadata;
    MetadataFetcher Metadata => _metadata ??= new MetadataFetcher(_settings);

    /// <summary>
    /// Imagen por defecto para una cara de un formato, con el orden de servicios de cada parte (ver <see cref="MetadataFetcher"/>).
    /// Null si no se encuentra. LaunchBox solo se usa si su base de datos ya está descargada.
    /// </summary>
    Task<string?> AutoFetchAsync(string name, string? platform, CoverFormat format, Face face, CancellationToken cancellation) =>
        Metadata.FetchAsync(name, platform, format, MetadataFetcher.PartOf(face), cancellation);

    CancellationTokenSource? _metadataRun;

    /// <summary>
    /// «Descarga Metadatos Actualización»: a todos los juegos a los que les falta alguna parte (portada, contraportada,
    /// lomo, interior, disco o manual) se les busca sola, con el orden de servicios de cada parte. Lo que ya tienen no se
    /// toca. Mientras se descarga, el mismo botón la cancela.
    /// </summary>
    async Task DownloadMissingMetadataAsync()
    {
        if (_metadataRun is { } running)
        {
            running.Cancel();
            return;
        }

        var games = Games.Where(game => MetadataFetcher.Missing(game).Count > 0).ToList();
        if (games.Count == 0)
        {
            ShowToast("Todos los juegos tienen ya todas sus imágenes");
            return;
        }

        var answer = MessageBox.Show(this,
            $"A {games.Count} {(games.Count == 1 ? "juego le falta" : "juegos les falta")} algo (portada, contraportada, lomo, interior de la caja, disco, manual o la lista de canciones de su banda sonora).\n\n" +
            "Se buscarán en LaunchBox, SteamGridDB, The Spine Archive, Steam Game Covers y MusicBrainz y se pondrán solo donde falten; lo que ya tienen no se toca. " +
            (LaunchBoxDatabase.IsDownloaded ? "" : "La primera vez se descarga la base de datos de LaunchBox (unos minutos). ") +
            "Puede tardar un rato.\n\n¿Empezar?",
            "Descarga Metadatos Actualización", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        using var run = _metadataRun = new CancellationTokenSource();
        var cancellation = run.Token;
        MetadataButton.Content = "Cancelar descarga";
        var parts = 0;
        var changedGames = 0;
        var done = 0;
        try
        {
            await Metadata.PrepareAsync(new Progress<string>(text => ShowToast(text)), cancellation);

            foreach (var game in games)
            {
                ShowToast($"Descargando metadatos… {++done} de {games.Count}: {game.Name}");
                var changed = false;
                foreach (var part in MetadataFetcher.Missing(game))
                {
                    // La banda sonora no es una imagen: su lista de canciones y la carátula para su disco.
                    if (part == MetadataPart.Soundtrack)
                    {
                        if (await Soundtracks.FindAsync(game.Name, cancellation) is { } found && Games.Contains(game) && game.Soundtrack is null)
                        {
                            game.Soundtrack = found.Soundtrack;
                            if (found.CoverPath is { } cover)
                            {
                                try
                                {
                                    ImageStore.SetSoundtrackDisc(game, cover);
                                }
                                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                                {
                                }
                            }
                            changed = true;
                            parts++;
                        }
                        continue;
                    }
                    var path = await Metadata.FetchAsync(game.Name, game.Platform, game.Format, part, cancellation);
                    // Mientras se buscaba ha podido quitarse el juego o recibir otra imagen.
                    if (path is null || !Games.Contains(game) || !MetadataFetcher.Missing(game).Contains(part))
                        continue;
                    try
                    {
                        switch (part)
                        {
                            case MetadataPart.Front: ImageStore.Set(game, game.Format, Face.Front, path); break;
                            case MetadataPart.Back: ImageStore.Set(game, game.Format, Face.Back, path); break;
                            case MetadataPart.Spine: ImageStore.Set(game, game.Format, Face.Spine, path); break;
                            case MetadataPart.Inside: ImageStore.SetExtra(game, GameExtra.Inside, path); break;
                            case MetadataPart.Disc: ImageStore.SetExtra(game, GameExtra.Disc, path); break;
                            default: ImageStore.SetExtra(game, GameExtra.Manual, path); break;
                        }
                        changed = true;
                        parts++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                    }
                }

                if (!changed)
                    continue;
                changedGames++;
                Save();
                var index = _visibleGames.IndexOf(game);
                if (index >= 0)
                {
                    _flow.UpdateItem(index, ToCarouselItem(game));
                    if (index == _flow.SelectedIndex)
                        ShowDetails(index);
                }
            }
            ShowToast(parts == 0
                ? "No se ha encontrado ninguna imagen nueva"
                : $"Metadatos descargados: {parts} {(parts == 1 ? "imagen" : "imágenes")} en {changedGames} {(changedGames == 1 ? "juego" : "juegos")}");
        }
        catch (OperationCanceledException)
        {
            ShowToast($"Descarga cancelada: {parts} {(parts == 1 ? "imagen puesta" : "imágenes puestas")} en {changedGames} {(changedGames == 1 ? "juego" : "juegos")}");
        }
        finally
        {
            _metadataRun = null;
            MetadataButton.Content = "Descarga Metadatos Actualización";
        }
    }

    void OnMetadataClick(object sender, RoutedEventArgs e) => _ = DownloadMissingMetadataAsync();

    void OpenFormatEditor() => FormatEditor.Open(_visibleGames, SelectedGame);

    /// <summary>Cambia el formato de los juegos y, si se ha pedido, pone imágenes por defecto donde falten.</summary>
    async void ApplyFormat(FormatResult result)
    {
        var selected = SelectedGame;
        foreach (var game in result.Games)
            game.Format = result.Format;
        Save();
        RefreshKeeping(selected);

        var name = result.Format == CoverFormat.Portrait ? "rectangular" : "cuadrado";
        ShowToast(result.Games.Count == 1 ? $"«{result.Games[0].Name}» pasa a formato {name}" : $"{result.Games.Count} juegos pasan a formato {name}");

        if (!result.AutoFetch)
            return;

        var empty = result.Games.Where(game => game.Faces(result.Format).IsEmpty).ToList();
        if (empty.Count == 0)
            return;

        // Una a una, para no saturar las APIs; cada caja se actualiza en cuanto llega su imagen.
        var done = 0;
        var withArt = 0;
        foreach (var game in empty)
        {
            ShowToast($"Buscando carátulas por defecto… {++done} de {empty.Count}");
            var changed = false;
            foreach (var face in new[] { Face.Front, Face.Back, Face.Spine })
            {
                var path = await AutoFetchAsync(game.Name, game.Platform, result.Format, face, CancellationToken.None);
                // Mientras buscábamos ha podido quitarse el juego, cambiar de formato o recibir otra imagen.
                if (path is null || !Games.Contains(game) || game.Format != result.Format || game.Faces(result.Format).Get(face) is not null)
                    continue;
                try
                {
                    ImageStore.Set(game, result.Format, face, path);
                    changed = true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }

            if (!changed)
                continue;
            withArt++;
            Save();
            var index = _visibleGames.IndexOf(game);
            if (index >= 0)
            {
                _flow.UpdateItem(index, ToCarouselItem(game));
                if (index == _flow.SelectedIndex)
                    ShowDetails(index);
            }
        }

        ShowToast(withArt == empty.Count
            ? "Carátulas por defecto puestas"
            : $"Carátulas por defecto puestas en {withArt} de {empty.Count} juegos (el resto no se encontró)");
    }

    void OpenFilterEditor(GameFilter? filter)
    {
        if (filter is null)
            FilterEditor.OpenNew();
        else
            FilterEditor.OpenEdit(filter, CountGames(filter));
    }

    void ApplyFilter(FilterResult result)
    {
        var isNew = result.Filter is null;
        var filter = result.Filter ?? new GameFilter();
        filter.Name = result.Name;

        var message = isNew ? $"Saga «{filter.Name}» creada" : "Saga guardada";
        try
        {
            if (result.NewImagePath is { } image)
                ImageStore.SetFilterImage(filter, image);
            else if (result.RemoveImage)
                ImageStore.RemoveFilterImage(filter);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = "No se pudo copiar la imagen; el resto de cambios sí se han guardado";
        }

        if (isNew)
            Filters.Add(filter);
        Save();

        if (isNew)
            SelectFilter(filter);
        else
            RefreshFilterTabs();
        ShowToast(message);
    }

    void DeleteFilter(GameFilter filter)
    {
        var answer = MessageBox.Show(this, $"¿Eliminar la saga «{filter.Name}»?\n\nLos juegos que tenga no se borran: siguen en «Todos los juegos».",
            "Eliminar saga", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        foreach (var game in Games)
            game.FilterIds.Remove(filter.Id);
        ImageStore.RemoveFilterImage(filter);
        Filters.Remove(filter);
        Save();

        if (filter == _activeFilter)
            SelectView(null, null);
        else
            RefreshFilterTabs();
        ShowToast($"Saga «{filter.Name}» eliminada");
    }

    void Save()
    {
        if (!_store.Save(_library))
            ShowToast("No se pudo guardar la biblioteca");
    }

    void ShowToast(string message)
    {
        ToastText.Text = message;
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2300))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2700))));
        Toast.BeginAnimation(OpacityProperty, animation);
    }

    void ToggleFullScreen()
    {
        _fullScreen = !_fullScreen;
        WindowStyle = _fullScreen ? WindowStyle.None : WindowStyle.SingleBorderWindow;
        ResizeMode = _fullScreen ? ResizeMode.NoResize : ResizeMode.CanResize;
        // Pasar por Normal obliga a Windows a recalcular el tamaño y cubrir también la barra de tareas.
        WindowState = WindowState.Normal;
        WindowState = WindowState.Maximized;
    }

    /// <summary>
    /// Botón del mando. Con un menú abierto solo se acepta (como Enter) o se cancela (como Esc);
    /// en el carrusel cada botón hace su acción directamente.
    /// </summary>
    // ---- Evitar dobles pulsaciones ----
    // Con Steam abierto, su «configuración de escritorio» convierte el mando en teclas (cruceta → flechas,
    // A → Enter, B → Esc…). Basilisco lee el mando con SDL, así que la misma pulsación llegaría dos veces
    // y el carrusel avanzaría dos puestos. Si mando y teclado dicen lo mismo casi a la vez, solo cuenta uno.
    static readonly TimeSpan DuplicateWindow = TimeSpan.FromMilliseconds(250);
    readonly Dictionary<Key, DateTime> _padKeys = [];
    readonly Dictionary<Key, DateTime> _keyboardKeys = [];

    /// <summary>Tecla que equivale a lo que hace el mando (para reconocer el duplicado de Steam).</summary>
    static Key? KeyFor(PadAction action) => action switch
    {
        PadAction.Confirm => Key.Enter,
        PadAction.Back => Key.Escape,
        _ => null,
    };

    static Key KeyForDirection(int direction, bool vertical) =>
        vertical ? (direction < 0 ? Key.Up : Key.Down) : (direction < 0 ? Key.Left : Key.Right);

    // Si la tecla de Steam llega antes que el mando (pasa a veces por unos milisegundos), el mando no la repite.
    static readonly TimeSpan KeyFirstWindow = TimeSpan.FromMilliseconds(90);

    /// <summary>
    /// True si el teclado acaba de hacer lo mismo (entonces el mando no lo repite). La pulsación del mando se
    /// apunta siempre, para que las teclas repetidas que mande Steam después se ignoren.
    /// </summary>
    bool PadIsDuplicate(Key key)
    {
        var now = DateTime.Now;
        var duplicate = _keyboardKeys.TryGetValue(key, out var at) && now - at < KeyFirstWindow;
        _padKeys[key] = now;
        return duplicate;
    }

    /// <summary>True si el mando acaba de hacer lo mismo: es la tecla que manda Steam por el mando y se ignora.</summary>
    bool KeyIsDuplicate(Key key)
    {
        if (key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Enter or Key.Escape or Key.Space))
            return false;

        var padKey = key == Key.Space ? Key.Enter : key;
        if (_padKeys.TryGetValue(padKey, out var at) && DateTime.Now - at < DuplicateWindow)
            return true;

        // Solo se apuntan las teclas que sí cuentan.
        _keyboardKeys[padKey] = DateTime.Now;
        return false;
    }

    void OnPadPressed(PadAction action)
    {
        if (KeyFor(action) is { } key && PadIsDuplicate(key))
            return;
        SetPadMode(true);
        if (AchievementsPage.IsVisible)
        {
            if (action == PadAction.Back)
            {
                if (TrophyEditor.IsVisible)
                    CloseTrophyEditor();
                else
                    CloseAchievementsPage();
            }
            return;
        }
        if (_flow.ShelfMode && !IsPanelOpen)
        {
            // En la estantería: aceptar coge o suelta, volver suelta o termina.
            if (action == PadAction.Confirm)
            {
                if (_flow.Holding) ShelfDrop(); else ShelfLift();
            }
            else if (action == PadAction.Back)
            {
                if (_flow.Holding) ShelfDrop(); else ExitShelf();
            }
            return;
        }
        if (FilterStrip.IsOpen)
        {
            switch (action)
            {
                case PadAction.Confirm: FilterStrip.Choose(); break;
                case PadAction.Back: InjectKey(Key.Escape); break;
                case PadAction.PageLeft or PadAction.PreviousFilter: FilterStrip.Move(-1); break;
                case PadAction.PageRight or PadAction.NextFilter: FilterStrip.Move(1); break;
            }
            return;
        }
        if (ManualReader.IsOpen)
        {
            switch (action)
            {
                case PadAction.Confirm or PadAction.PageRight or PadAction.NextFilter: ManualReader.Flip(1); break;
                case PadAction.PageLeft or PadAction.PreviousFilter: ManualReader.Flip(-1); break;
                case PadAction.Back: ManualReader.Close(); break;
            }
            return;
        }
        // En la caja abierta, X (□ en PlayStation) abre el manual.
        if (CaseView.IsOpen && !Customizer.IsOpen && action == PadAction.Rotate)
        {
            CaseView.RequestManual();
            return;
        }
        if (IsPanelOpen)
        {
            if (action == PadAction.Confirm)
                InjectKey(Key.Enter);
            else if (action == PadAction.Back)
                InjectKey(Key.Escape);
            return;
        }

        // En la barra de arriba: Ⓐ pulsa el botón elegido y Ⓑ vuelve al carrusel; cualquier otro botón vuelve y hace lo suyo.
        if (InBar)
        {
            if (action == PadAction.Confirm)
            {
                ActivateBar();
                return;
            }
            ExitBar();
            if (action == PadAction.Back)
                return;
        }

        switch (action)
        {
            case PadAction.Search: FocusSearch(); break;
            case PadAction.Sort: Press(SortTab); break;
            case PadAction.Confirm: Activate(_flow.SelectedIndex); break;
            case PadAction.Back when _fullScreen: ToggleFullScreen(); break;
            case PadAction.Rotate: _flow.FlipSelected(); break;
            case PadAction.Customize: OpenCustomizer(_flow.SelectedIndex); break;
            case PadAction.PreviousFilter: CycleFilter(-1); break;
            case PadAction.NextFilter: CycleFilter(1); break;
            case PadAction.Format: OpenFormatEditor(); break;
            case PadAction.FullScreen: ToggleFullScreen(); break;
            case PadAction.PageLeft: _flow.MoveBy(-5); break;
            case PadAction.PageRight: _flow.MoveBy(5); break;
        }
    }

    void OnPadNavigate(int direction)
    {
        if (PadIsDuplicate(KeyForDirection(direction, vertical: false)))
            return;
        SetPadMode(true);
        if (ManualReader.IsOpen)
        {
            ManualReader.Flip(direction);
            return;
        }
        if (FilterStrip.IsOpen)
        {
            FilterStrip.Move(direction);
            return;
        }
        // En la caja abierta, izquierda y derecha: pasar a la banda sonora y, en ella, avanzar o retroceder en la canción.
        if (CaseView.IsOpen && !Customizer.IsOpen && !ManualReader.IsOpen)
        {
            InjectKey(KeyForDirection(direction, vertical: false));
            return;
        }
        if (IsPanelOpen)
            return;
        if (InBar)
            MoveBar(direction);
        else if (_flow.ShelfMode)
            ShelfMove(direction);
        else
            _flow.MoveBy(direction);
    }

    /// <summary>Arriba y abajo del mando: en la estantería, coger y soltar.</summary>
    void OnPadVertical(int direction)
    {
        if (PadIsDuplicate(KeyForDirection(direction, vertical: true)))
            return;
        SetPadMode(true);
        if (AchievementsPage.IsVisible)
        {
            AchievementsScroll.ScrollToVerticalOffset(AchievementsScroll.VerticalOffset + direction * 160);
            return;
        }
        // En la banda sonora de la caja abierta: elegir canción.
        if (CaseView.IsOpen && !Customizer.IsOpen && !ManualReader.IsOpen)
        {
            InjectKey(KeyForDirection(direction, vertical: true));
            return;
        }
        if (!IsPanelOpen && !_flow.ShelfMode)
        {
            // Arriba sube a la barra de arriba (y de fila en fila); abajo baja hasta volver al carrusel.
            if (InBar)
                MoveBarRow(direction);
            else if (direction < 0)
                EnterBar();
            return;
        }
        if (IsPanelOpen || !_flow.ShelfMode)
            return;
        if (direction < 0)
            ShelfLift();
        else
            ShelfDrop();
    }

    /// <summary>El stick derecho gira la caja seleccionada, como arrastrarla con el ratón.</summary>
    void OnPadRightStick(double x, double y)
    {
        if (IsPanelOpen || _flow.ShelfMode || !_flow.CanInspectSelected)
            return;
        SetPadMode(true);
        var (yaw, pitch) = _flow.InspectTarget;
        _flow.SetInspect(yaw + x * StickYawSpeed / 60, pitch + y * StickPitchSpeed / 60);
    }

    /// <summary>Envía una tecla al elemento con el foco, para que los menús reaccionen al mando igual que al teclado.</summary>
    void InjectKey(Key key)
    {
        if (PresentationSource.FromVisual(this) is not { } source)
            return;

        var target = Keyboard.FocusedElement ?? this;
        _injectingKey = true;
        try
        {
            var preview = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            target.RaiseEvent(preview);
            if (!preview.Handled)
                target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
        }
        finally
        {
            _injectingKey = false;
        }
    }

    /// <summary>La barra de abajo enseña los botones del mando (con los símbolos de su familia) o las teclas.</summary>
    void SetPadMode(bool pad)
    {
        var style = _gamepad.Style;
        if (_padMode == pad && (!pad || ConfirmKey.Text == GamepadService.Label(PadAction.Confirm, style)))
            return;

        _padMode = pad;
        NavKey.Text = pad ? "◀ ▶" : "← →";
        ConfirmKey.Text = pad ? GamepadService.Label(PadAction.Confirm, style) : "Enter";
        RotateKey.Text = pad ? GamepadService.Label(PadAction.Rotate, style) : "R";
        CustomizeKey.Text = pad ? GamepadService.Label(PadAction.Customize, style) : "E";
        FormatKey.Text = pad ? GamepadService.Label(PadAction.Format, style) : "F";
        FilterKey.Text = pad ? GamepadService.Label(PadAction.NextFilter, style) : "Tab";
        FullScreenKey.Text = pad ? GamepadService.Label(PadAction.FullScreen, style) : "F11";
        BarKey.Text = pad ? "▲" : "↑";
        RemoveHint.Visibility = ImportHints.Visibility = KeyboardHints.Visibility = pad ? Visibility.Collapsed : Visibility.Visible;
        UpdateShelfHints();
        RefreshButtonBadges();
    }

    /// <summary>Acerca o aleja las cajas; el título y el suelo se recolocan solos.</summary>
    void ApplyZoom(double zoom, bool save = true)
    {
        _flow.Zoom = zoom;
        ZoomText.Text = $"{_flow.Zoom * 100:0} %";
        UpdateOverlayLayout();
        if (!save)
            return;
        _settings.Zoom = _flow.Zoom;
        _settings.Save();
    }

    void OnZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_flow is not null && IsLoaded)
            ApplyZoom(e.NewValue);
    }

    /// <summary>
    /// Altura automática: busca cuánto hay que bajar las cajas para que la más alta (una de DVD o cuadrada) empiece
    /// por debajo de <paramref name="limit"/> (el pie del nombre y sus etiquetas). Si ya caben, se quedan donde siempre.
    /// </summary>
    void FitBelowTitle(double limit)
    {
        double Top(double shift)
        {
            _flow.Shift = shift;
            return _flow.Project(new System.Windows.Media.Media3D.Point3D(0, BoxModel.Height, 0)).Y;
        }

        double low = 0, high = CoverFlow.MaxShift;
        if (Top(0) >= limit)
        {
            SyncShiftSlider(0);
            return;
        }
        // Cuanto más sube la cámara, más abajo se ve la caja: se busca el punto justo.
        for (var i = 0; i < 24; i++)
        {
            var middle = (low + high) / 2;
            if (Top(middle) >= limit)
                high = middle;
            else
                low = middle;
        }
        _flow.Shift = high;
        SyncShiftSlider(high);
    }

    bool _syncingShift;

    void SyncShiftSlider(double shift)
    {
        _syncingShift = true;
        ShiftSlider.Value = shift;
        _syncingShift = false;
    }

    void OnShiftChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_flow is null || !IsLoaded || _syncingShift || _settings.CarouselAutoShift)
            return;
        _flow.Shift = e.NewValue;
        _settings.CarouselShift = _flow.Shift;
        _settings.Save();
        UpdateOverlayLayout();
    }

    /// <summary>Cambia entre la alineación clásica (en curva) y la moderna (las bases en línea recta).</summary>
    void OnAlignClick(object sender, RoutedEventArgs e)
    {
        _settings.CarouselAligned = AlignToggle.IsChecked == true;
        _settings.Save();
        _flow.Aligned = _settings.CarouselAligned;
        UpdateOverlayLayout();
    }

    void OnAutoShiftClick(object sender, RoutedEventArgs e)
    {
        _settings.CarouselAutoShift = AutoShiftToggle.IsChecked == true;
        ShiftSlider.IsEnabled = !_settings.CarouselAutoShift;
        // Al quitar el automático, la barra se queda donde lo había dejado y desde ahí se mueve a mano.
        if (!_settings.CarouselAutoShift)
            _settings.CarouselShift = _flow.Shift;
        _settings.Save();
        UpdateOverlayLayout();
    }

    void StepZoom(double delta) => ZoomSlider.Value = Math.Clamp(ZoomSlider.Value + delta, ZoomSlider.Minimum, ZoomSlider.Maximum);

    // ---- Buscador ----

    /// <summary>Una sugerencia del buscador: el juego y, a la derecha, su consola.</summary>
    sealed record SearchHit(Game Game, string Name, string Detail);

    void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    void OnSearchFocusChanged(object sender, KeyboardFocusChangedEventArgs e) =>
        SearchHint.Visibility = SearchBox.IsKeyboardFocused || SearchBox.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;

    void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.IsKeyboardFocused || SearchBox.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        var query = SearchBox.Text.Trim();
        if (query.Length == 0)
        {
            SearchPopup.IsOpen = false;
            return;
        }

        var hits = GameSearch.Find(Games, game => game.Name, query, 8)
            .Select(game => new SearchHit(game, game.Name, Platforms.Find(game.Platform)?.Name ?? ""))
            .ToList();
        SearchResults.ItemsSource = hits;
        SearchResults.SelectedIndex = hits.Count > 0 ? 0 : -1;
        SearchResults.Visibility = hits.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchEmpty.Visibility = hits.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        // El menú va escalado como el resto de la interfaz (un Popup no hereda la escala de la ventana).
        SearchPopupCard.LayoutTransform = _uiScale;
        SearchPopupCard.MinWidth = Math.Max(320, SearchField.ActualWidth);
        SearchPopup.IsOpen = true;
    }

    void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        var count = SearchResults.Items.Count;
        switch (e.Key)
        {
            case Key.Down when count > 0:
                SearchResults.SelectedIndex = (SearchResults.SelectedIndex + 1) % count;
                SearchResults.ScrollIntoView(SearchResults.SelectedItem);
                break;
            case Key.Up when count > 0:
                SearchResults.SelectedIndex = (SearchResults.SelectedIndex - 1 + count) % count;
                SearchResults.ScrollIntoView(SearchResults.SelectedItem);
                break;
            case Key.Enter:
                if (SearchPopup.IsOpen && SearchResults.SelectedItem is SearchHit hit)
                    GoToGame(hit.Game);
                break;
            case Key.Escape:
                EndSearch();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    void OnSearchResultClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(SearchResults, source) is ListBoxItem { Content: SearchHit hit })
            GoToGame(hit.Game);
    }

    /// <summary>Lleva el carrusel al juego elegido. Si no está en lo que se ve (otra consola, otro filtro), se pasa a «Todos los juegos».</summary>
    void GoToGame(Game game)
    {
        EndSearch();
        if (_flow.ShelfMode)
            ExitShelf();
        if (!_visibleGames.Contains(game))
            SelectView(null, null);
        var index = _visibleGames.IndexOf(game);
        if (index >= 0)
            _flow.JumpTo(index);
    }

    void EndSearch()
    {
        SearchPopup.IsOpen = false;
        SearchBox.Text = "";
        Focus();
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // La misma pulsación del mando repetida como tecla por Steam: se ignora.
        if (!_injectingKey && KeyIsDuplicate(e.Key))
        {
            e.Handled = true;
            return;
        }

        // Al volver al teclado, la barra de abajo vuelve a enseñar las teclas.
        if (!_injectingKey)
            SetPadMode(false);

        // En la página de logros: Esc (o volver) la cierra; arriba, abajo y la rueda la recorren.
        if (AchievementsPage.IsVisible)
        {
            switch (e.Key)
            {
                // Escribiendo en un formulario, las teclas son para escribir; Esc cierra el formulario.
                case var _ when Keyboard.FocusedElement is TextBox:
                    if (e.Key != Key.Escape)
                        return;
                    CloseTrophyEditor();
                    break;
                case Key.Escape when TrophyEditor.IsVisible: CloseTrophyEditor(); break;
                case Key.Escape or Key.Up when e.Key != Key.Up || AchievementsScroll.VerticalOffset <= 0: CloseAchievementsPage(); break;
                case Key.Up: AchievementsScroll.ScrollToVerticalOffset(AchievementsScroll.VerticalOffset - 160); break;
                case Key.Down: AchievementsScroll.ScrollToVerticalOffset(AchievementsScroll.VerticalOffset + 160); break;
                case Key.PageUp: AchievementsScroll.PageUp(); break;
                case Key.PageDown: AchievementsScroll.PageDown(); break;
                default: return;
            }
            e.Handled = true;
            return;
        }

        // Con un menú abierto las teclas son para él (escribir el nombre, Enter, Esc); en el buscador, para escribir.
        if (IsPanelOpen || SearchBox.IsKeyboardFocused)
            return;

        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FocusSearch();
            e.Handled = true;
            return;
        }

        if (_flow.ShelfMode)
        {
            if (HandleShelfKey(e.Key))
                e.Handled = true;
            return;
        }

        // En la barra de arriba, las flechas se mueven por ella, Enter pulsa y Esc vuelve.
        if (InBar)
        {
            switch (e.Key)
            {
                case Key.Left: MoveBar(-1); break;
                case Key.Right: MoveBar(1); break;
                case Key.Up: MoveBarRow(-1); break;
                case Key.Down: MoveBarRow(1); break;
                case Key.Enter or Key.Space: ActivateBar(); break;
                case Key.Escape: ExitBar(); break;
                default:
                    ExitBar();
                    goto keys;
            }
            e.Handled = true;
            return;
        }

        keys:
        // En el modo edición, Esc termina.
        if (_editing && e.Key == Key.Escape)
        {
            SetEditMode(false);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.E when Keyboard.Modifiers == ModifierKeys.Control: SetEditMode(!_editing); break;
            case Key.Up: EnterBar(); break;
            case Key.D1 or Key.NumPad1: Press(AllTab); break;
            case Key.D2 or Key.NumPad2: Press(ConsolesTab); break;
            case Key.D3 or Key.NumPad3: Press(FiltersTab); break;
            case Key.D4 or Key.NumPad4: Press(PlayersTab); break;
            case Key.D5 or Key.NumPad5 when FamilyTab.IsVisible: Press(FamilyTab); break;
            case Key.O: Press(SortTab); break;
            case Key.F1: Press(ShortcutsButton); break;
            case Key.L: Press(AlignToggle); break;
            case Key.M: Press(MetadataButton); break;
            case Key.Left: _flow.MoveBy(-1); break;
            case Key.Right: _flow.MoveBy(1); break;
            case Key.Home: _flow.Select(0); break;
            case Key.End: _flow.Select(_flow.Count - 1); break;
            case Key.Tab: CycleFilter(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1); break;
            case Key.Enter or Key.Space: Activate(_flow.SelectedIndex); break;
            case Key.Delete: RemoveSelected(); break;
            case Key.E: OpenCustomizer(_flow.SelectedIndex); break;
            case Key.R: _flow.FlipSelected(); break;
            case Key.I: Import(); break;
            case Key.C: ImportFolder(); break;
            case Key.F: OpenFormatEditor(); break;
            case Key.S: OpenImportMenu(); break;
            case Key.A: OpenThemeEditor(); break;
            case Key.U when _update is not null: _ = UpdateAsync(); break;
            case Key.N: SetTheme(ThemeManager.IsDark ? ColorMode.Light : ColorMode.Dark); break;
            case Key.F11: ToggleFullScreen(); break;
            case Key.OemPlus or Key.Add: StepZoom(0.1); break;
            case Key.OemMinus or Key.Subtract: StepZoom(-0.1); break;
            case Key.Escape when _fullScreen: ToggleFullScreen(); break;
            default: return;
        }
        e.Handled = true;
    }

    void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (IsPanelOpen)
            return;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            StepZoom(e.Delta > 0 ? 0.05 : -0.05);
            e.Handled = true;
            return;
        }

        // Acumulamos para que los touchpads (que mandan deltas pequeños) no aceleren el carrusel.
        _wheelDelta += e.Delta;
        while (Math.Abs(_wheelDelta) >= Mouse.MouseWheelDeltaForOneLine)
        {
            var step = Math.Sign(_wheelDelta);
            if (_flow.ShelfMode)
                ShelfMove(-step);
            else
                _flow.MoveBy(-step);
            _wheelDelta -= step * Mouse.MouseWheelDeltaForOneLine;
        }
    }

    void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_flow.HitTest(e.GetPosition(Viewport)) is not { } index)
            return;

        // En la estantería, un clic solo elige el lomo (con un juego cogido no se cambia de juego).
        if (_flow.ShelfMode)
        {
            if (!_flow.Holding)
                _flow.Select(index);
            return;
        }

        if (index != _flow.SelectedIndex)
        {
            _flow.Select(index);
            return;
        }

        // Sobre la caja seleccionada: si se arrastra, se gira; si solo se hace clic, se juega (al soltar).
        _dragStart = e.GetPosition(Viewport);
        _dragFrom = _flow.InspectTarget;
        _dragged = false;
        Viewport.CaptureMouse();
    }

    void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start)
            return;

        var delta = e.GetPosition(Viewport) - start;
        if (!_dragged && delta.Length < DragThreshold)
            return;

        _dragged = true;
        _flow.SetInspect(
            _dragFrom.Yaw + delta.X * DragDegreesPerPixel,
            _dragFrom.Pitch + delta.Y * DragDegreesPerPixel * CoverFlow.PitchDragFactor);
    }

    void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is null)
            return;

        _dragStart = null;
        Viewport.ReleaseMouseCapture();
        if (!_dragged)
            Activate(_flow.SelectedIndex);
    }

    void OnViewportLostCapture(object sender, MouseEventArgs e) => _dragStart = null;

    void OnViewportRightMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_flow.ShelfMode)
            return;
        if (_flow.HitTest(e.GetPosition(Viewport)) is not { } index || IsAddCard(index))
            return;

        _flow.Select(index);
        OpenCustomizer(index);
    }

    void OnImportClick(object sender, RoutedEventArgs e) => Import();

    void OnImportFolderClick(object sender, RoutedEventArgs e) => ImportFolder();

    void OnFormatClick(object sender, RoutedEventArgs e) => OpenFormatEditor();

    void OnThemeClick(object sender, RoutedEventArgs e) => OpenThemeEditor();

    void OpenThemeEditor()
    {
        ThemeEditor.SetPerformance(_settings.Quality, _settings.Reflections, _settings.AnimatedBackgrounds);
        OpenThemeEditorCore();
    }

    void OpenThemeEditorCore() => ThemeEditor.Open(ImageStore.GetGlobalBackgroundPath(_settings), _settings.TextColor,
        Games.Count(game => game.Background is not null || game.TextColor is not null), _settings.Theme);

    /// <summary>Apariencia general: fondo y color de las letras para todos los juegos.</summary>
    void ApplyTheme(ThemeResult result)
    {
        try
        {
            if (result.Background.NewPath is { } path)
                ImageStore.SetGlobalBackground(_settings, path);
            else if (result.Background.Remove)
                ImageStore.RemoveGlobalBackground(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast("No se pudo copiar el fondo");
        }
        _settings.TextColor = result.TextColor;
        _settings.Save();

        if (result.ClearOwn)
        {
            foreach (var game in Games)
            {
                ImageStore.RemoveBackground(game);
                game.TextColor = null;
            }
            Save();
        }

        _lookBackground = "\\0"; // Fuerza a recargar el fondo.
        ShowDetails(_flow.SelectedIndex);
        ShowToast("Apariencia guardada");
    }

    void OnSteamClick(object sender, RoutedEventArgs e) => OpenImportMenu();

    void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
            return;

        // Con un menú abierto, soltar una imagen la usa como portada del juego o imagen del filtro.
        if (IsPanelOpen)
        {
            if (ArtSearch.IsOpen || paths.FirstOrDefault(ImageStore.IsImage) is not { } image)
                return;

            if (Customizer.IsOpen)
                Customizer.DropImage(image);
            else if (FilterEditor.IsOpen)
                FilterEditor.SetImage(image);
            return;
        }

        // Una carpeta soltada se escanea entera; los archivos sueltos se importan tal cual.
        AddFiles(paths);
        if (paths.FirstOrDefault(Directory.Exists) is { } folder)
            OpenFolderImport(folder);
    }
}
