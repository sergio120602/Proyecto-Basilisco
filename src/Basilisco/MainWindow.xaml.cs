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
    readonly TheGamesDbProvider _theGamesDb = new();
    readonly TheGamesDbProvider _theGamesDbBanners = new(banners: true);
    readonly LaunchBoxProvider _launchBoxFront = new(Face.Front);
    readonly LaunchBoxProvider _launchBoxBack = new(Face.Back);
    readonly LaunchBoxProvider _launchBoxSpine = new(Face.Spine);
    readonly Library _library;
    readonly CoverFlow _flow;

    // Lo seleccionado arriba: una consola, un filtro o ninguno de los dos («Todos los juegos»).
    string? _activePlatform;
    GameFilter? _activeFilter;

    /// <summary>Nombre de lo que se está viendo, para los textos («PS2», «RPG»…), o null en «Todos los juegos».</summary>
    string? ActiveViewName => _activeFilter?.Name ?? Platforms.Find(_activePlatform)?.Name;

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
        InitializeComponent();
        VersionText.Text = $"v{AppInfo.Version}";

        _library = _store.Load();
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
        FolderImporter.ImportRequested += ApplyFolderImport;
        SteamImporter.ImportRequested += ApplySteamImport;
        ThemeEditor.Saved += ApplyTheme;
        PlatformPicker.Saved += ApplyPlatforms;

        Viewport.SizeChanged += (_, _) => UpdateOverlayLayout();
        Activated += async (_, _) =>
        {
            if (BackgroundVideo.Source is not null) BackgroundVideo.Play();
            EndPlaySession(returned: true);
            if (DateTime.Now - _lastSteamSync > TimeSpan.FromSeconds(30))
                await SyncSteamPlaytimeAsync(online: false);
        };
        Deactivated += (_, _) => BackgroundVideo.Pause();
        ZoomSlider.Value = Math.Clamp(_settings.Zoom, CoverFlow.MinZoom, CoverFlow.MaxZoom);
        ApplyZoom(ZoomSlider.Value, save: false);

        _gamepad.IsActive = () => IsActive && WindowState != WindowState.Minimized;
        _gamepad.Pressed += OnPadPressed;
        _gamepad.Navigate += OnPadNavigate;
        _gamepad.RightStick += OnPadRightStick;
        _gamepad.NavigateVertical += OnPadVertical;
        _gamepad.Connected += (name, style) =>
        {
            SetPadMode(true);
            ShowToast($"Mando conectado: {name}");
        };
        _gamepad.Disconnected += () => ShowToast("Mando desconectado");
        Closed += (_, _) => _gamepad.Dispose();
        Loaded += async (_, _) =>
        {
            await RepairMovedGamesAsync();
            await SyncSteamPlaytimeAsync(online: true);
        };
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
        var missing = Games.Where(game => !GameLauncher.IsLink(game.Path) && !File.Exists(game.Path)).Select(game => (Game: game, game.Path)).ToList();
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

    bool IsPanelOpen => Customizer.IsOpen || FilterEditor.IsOpen || FolderImporter.IsOpen || ArtSearch.IsOpen || PlatformPicker.IsOpen ||
                        FormatEditor.IsOpen || SteamImporter.IsOpen || ThemeEditor.IsOpen;

    bool IsAddCard(int index) => index >= _visibleGames.Count;

    bool BelongsToActiveFilter(Game game) =>
        (_activePlatform is null || game.Platform == _activePlatform) &&
        (_activeFilter is null || game.FilterIds.Contains(_activeFilter.Id));

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
            game.Format, game.CurrentFaces.FrontFrame);

    /// <summary>
    /// Barra de arriba: «Todos los juegos», el grupo de consolas, el de filtros y el orden. Cada botón dice lo que
    /// está elegido (por ejemplo «PS2 (3)») y se ve marcado si es lo que se está viendo.
    /// </summary>
    void RefreshFilterTabs()
    {
        AllTab.Content = $"Todos los juegos ({Games.Count})";
        AllTab.IsChecked = _activeFilter is null && _activePlatform is null;

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
            FiltersTab.Content = "Filtros  ▾";
            FiltersTab.ToolTip = "Ver solo los juegos de un filtro, crear filtros nuevos o editarlos";
        }
        FiltersTab.IsChecked = _activeFilter is not null;

        SortTab.Content = $"Orden: {SortModes.Short(_settings.SortMode)}  ▾";
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
        if (_activeFilter is not null || _activePlatform is not null)
            SelectView(null, null);
        else
            RefreshFilterTabs();
    }

    /// <summary>Grupo «Consolas»: las que tienen juegos (y la que estás viendo).</summary>
    void OnConsolesTabClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Todas las consolas", () => SelectView(null, _activeFilter), isChecked: _activePlatform is null));
        menu.Items.Add(new Separator());

        var any = false;
        foreach (var platform in Platforms.All)
        {
            var count = CountGames(platform);
            if (count == 0 && platform.Id != _activePlatform)
                continue;
            any = true;
            var id = platform.Id;
            menu.Items.Add(Item($"{platform.Name}  ({count})", () => SelectView(id, null), isChecked: id == _activePlatform));
        }
        if (!any)
            menu.Items.Add(Item("Aún no hay juegos con consola (elígela en Personalizar)", null, enabled: false));

        ShowMenu(ConsolesTab, menu);
    }

    /// <summary>Grupo «Filtros»: tus filtros, crear uno nuevo y editar los que hay.</summary>
    void OnFiltersTabClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Todos los filtros", () => SelectView(_activePlatform, null), isChecked: _activeFilter is null));
        menu.Items.Add(new Separator());

        foreach (var filter in Filters)
        {
            var image = ImageStore.GetFilterImagePath(filter);
            var icon = image is null ? null : new Image { Source = CoverArt.LoadImage(image, FilterIconDecodeWidth), Width = 22, Height = 22, Stretch = Stretch.Uniform };
            var chosen = filter;
            menu.Items.Add(Item($"{filter.Name}  ({CountGames(filter)})", () => SelectView(null, chosen), isChecked: filter == _activeFilter, icon: icon));
        }
        if (Filters.Count > 0)
            menu.Items.Add(new Separator());

        menu.Items.Add(Item("+  Nuevo filtro…", () => OpenFilterEditor(null)));
        if (Filters.Count > 0)
        {
            var edit = Item("Editar filtro", null);
            foreach (var filter in Filters)
            {
                var chosen = filter;
                edit.Items.Add(Item(filter.Name, () => OpenFilterEditor(chosen)));
            }
            menu.Items.Add(edit);
        }

        ShowMenu(FiltersTab, menu);
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
        var menu = new ContextMenu();
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
        MainHints.Visibility = shelf ? Visibility.Collapsed : Visibility.Visible;
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

    void SelectView(string? platform, GameFilter? filter)
    {
        _activePlatform = platform;
        _activeFilter = filter;
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
            ActivateHint.Text = "Jugar";
            GameHints.Visibility = Visibility.Visible;
        }

        var selectedGame = IsAddCard(index) ? null : _visibleGames[index];
        // (con la tarjeta de importar se ven la apariencia general)
        ApplyLook(selectedGame);
        PlayTimeText.Text = selectedGame is null ? "" : PlayTimeLabel(selectedGame.PlayMinutes);
        PlayTimeText.Visibility = selectedGame is null || _flow.ShelfMode ? Visibility.Collapsed : Visibility.Visible;

        UpdateOverlayLayout();
        TitlePanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });
    }

    static string PlayTimeLabel(int minutes) => minutes switch
    {
        <= 0 => "⏱  Aún sin tiempo de juego",
        < 60 => $"⏱  {minutes} min jugados",
        _ when minutes % 60 == 0 => $"⏱  {minutes / 60} h jugadas",
        _ => $"⏱  {minutes / 60} h {minutes % 60} min jugadas",
    };

    string? _lookBackground;

    /// <summary>
    /// Fondo y color de letras del juego seleccionado (o los de siempre). El fondo cambia con un fundido y solo
    /// se vuelve a cargar si es otro archivo.
    /// </summary>
    void ApplyLook(Game? game)
    {
        var hexColor = game?.TextColor ?? _settings.TextColor;
        var color = hexColor is { } hex && ColorConverter.ConvertFromString(hex) is Color custom ? custom : Color.FromRgb(0x1F, 0x26, 0x30);
        Resources["UiText"] = new SolidColorBrush(color);
        // Con letras claras, el halo alrededor del título pasa a ser oscuro para que se lean.
        var light = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255 > 0.6;
        var glow = light ? Colors.Black : Colors.White;
        if (TitleText.Effect is System.Windows.Media.Effects.DropShadowEffect titleGlow)
            titleGlow.Color = glow;
        PlayTimeGlow.Color = glow;

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
            }
            else
            {
                BackgroundImage.Source = CoverArt.LoadImage(path, 2560);
            }
            BackgroundLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(320)));
        };
        BackgroundLayer.BeginAnimation(OpacityProperty, fadeOut);
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

    /// <summary>Partida en curso: desde que se lanza hasta que se cierra el juego o se vuelve a Basilisco.</summary>
    sealed record PlaySession(Game Game, DateTime Started, System.Diagnostics.Process? Process);

    PlaySession? _session;

    void StartPlaySession(Game game, System.Diagnostics.Process? process)
    {
        EndPlaySession(returned: false);
        _session = new PlaySession(game, DateTime.Now, process);
        if (process is null)
            return;
        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                // Un lanzador que se cierra al momento no cuenta: se espera a volver a Basilisco.
                if (_session?.Process == process && DateTime.Now - _session.Started > TimeSpan.FromSeconds(20))
                    EndPlaySession(returned: false);
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    /// <param name="returned">True si se acaba porque has vuelto a Basilisco (los primeros segundos no cuentan: aún carga el juego).</param>
    void EndPlaySession(bool returned)
    {
        if (_session is not { } session)
            return;
        var elapsed = DateTime.Now - session.Started;
        if (returned && elapsed < TimeSpan.FromSeconds(20))
            return;

        _session = null;
        var minutes = (int)Math.Min(elapsed.TotalMinutes, 12 * 60);
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
            game.PlayCount switch
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

        if (game.SteamOwner is { } owner)
            chips.Add($"Steam Familiar · de {owner}");

        var folder = GameLauncher.IsLink(game.Path) ? null : Path.GetFileName(Path.GetDirectoryName(game.Path));
        if (!string.IsNullOrEmpty(folder))
            chips.Add($"Carpeta: '{folder}'");

        if (!GameLauncher.IsLink(game.Path) && !File.Exists(game.Path))
            chips.Add("Archivo no encontrado");

        return chips;
    }

    void UpdateOverlayLayout()
    {
        if (Viewport.ActualWidth <= 0)
            return;

        // Las cajas rectangulares miden según su consola: el título va justo encima de la seleccionada.
        var coverTop = _flow.Project(new Point3D(0, _flow.SelectedHeight, 0));
        TitlePanel.Margin = new Thickness(0, 0, 0, Math.Max(0, Viewport.ActualHeight - coverTop.Y + 24));

        // El suelo empieza por detrás de las cajas laterales para que todas apoyen sobre él.
        var horizon = _flow.Project(new Point3D(0, 0, -2.5));
        Floor.Margin = new Thickness(0, Math.Max(0, horizon.Y - 40), 0, 0);

        // El tiempo de juego, justo debajo de la caja seleccionada.
        var coverBottom = _flow.Project(new Point3D(0, 0, 0.1));
        PlayTimeText.Margin = new Thickness(0, coverBottom.Y + 14, 0, 0);
    }

    void Activate(int index)
    {
        if (IsAddCard(index))
            Import();
        else
            Launch(_visibleGames[index]);
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
    /// Un juego de Steam ya está en la biblioteca si se importó desde Steam o si ya tenías su ejecutable
    /// (importado de la carpeta del juego dentro de steamapps).
    /// </summary>
    bool IsSteamAppInLibrary(SteamApp app) =>
        Games.Any(game => game.SteamAppId == app.AppId || SamePath(game.Path, app.LaunchUri) ||
                          app.InstallDir is { } dir && game.Path.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));

    void ApplySteamImport(SteamImportResult result)
    {
        var familyFilter = result.FamilyFilter && result.Apps.Any(app => app.Source == SteamSource.Family)
            ? FilterNamed("Steam Familiar")
            : null;
        var byLink = result.Apps.ToDictionary(app => app.LaunchUri, StringComparer.OrdinalIgnoreCase);

        AddGames(result.Apps.Select(app => (app.LaunchUri, (string?)app.Name)), configure: game =>
        {
            if (!byLink.TryGetValue(game.Path, out var app))
                return;
            game.SteamAppId = app.AppId;
            game.SteamOwner = app.Source == SteamSource.Family ? app.Owner ?? "otro miembro" : null;
            game.PlayMinutes = Math.Max(game.PlayMinutes, app.PlaytimeMinutes);
            game.Platform ??= "pc";
            if (familyFilter is not null && app.Source == SteamSource.Family && !game.FilterIds.Contains(familyFilter.Id))
                game.FilterIds.Add(familyFilter.Id);
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
        if (!GameLauncher.IsLink(game.Path) && !File.Exists(game.Path))
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
        game.TextColor = result.TextColor;
        game.PlayMinutes = result.PlayMinutes;
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
    /// y la contraportada y el lomo (a partir de sus banners) en TheGamesDB.
    /// </summary>
    void OpenArtSearch(string gameName, ArtTarget target, CoverFormat format, string? platform, bool launchBox)
    {
        if (launchBox)
        {
            // LaunchBox: la segunda opción, con fotos reales de las tres caras de la caja.
            var (provider, onChosen) = target switch
            {
                ArtTarget.Front => (_launchBoxFront, (Action<string>)Customizer.SetFrontImage),
                ArtTarget.Back => (_launchBoxBack, Customizer.SetBackImage),
                _ => (_launchBoxSpine, Customizer.SetSpineImage),
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

    /// <summary>
    /// Imagen por defecto para una cara de un formato: la portada de SteamGridDB (con las medidas del formato)
    /// y la contraportada de TheGamesDB. Null si falta la clave, no hay resultados o el servicio no responde.
    /// </summary>
    /// <remarks>Si no hay resultado y la base de datos de LaunchBox ya está descargada, se prueba con ella.</remarks>
    async Task<string?> AutoFetchAsync(string name, string? platform, CoverFormat format, Face face, CancellationToken cancellation)
    {
        // Las cajas rectangulares no llevan franja: la portada real de LaunchBox (con su franja impresa) va primero.
        if (format == CoverFormat.Portrait && face == Face.Front && LaunchBoxDatabase.IsDownloaded)
        {
            _launchBoxFront.PlatformHint = platform;
            if (await ArtAutoFetch.FirstImageAsync(_launchBoxFront, _settings, name, cancellation) is { } real)
                return real;
        }

        ArtProvider? provider = face switch
        {
            Face.Front => format == CoverFormat.Portrait ? _steamGridDbPortrait : _steamGridDb,
            Face.Back => _theGamesDb,
            _ => null,
        };
        if (provider is not null && await ArtAutoFetch.FirstImageAsync(provider, _settings, name, cancellation) is { } path)
            return path;

        if (!LaunchBoxDatabase.IsDownloaded || face == Face.Spine)
            return null;
        var launchBox = face == Face.Front ? _launchBoxFront : _launchBoxBack;
        launchBox.PlatformHint = platform;
        return await ArtAutoFetch.FirstImageAsync(launchBox, _settings, name, cancellation);
    }

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

        if (string.IsNullOrEmpty(_settings.SteamGridDbApiKey) && string.IsNullOrEmpty(_settings.TheGamesDbApiKey))
        {
            ShowToast("Para poner carátulas por defecto, guarda tus claves de SteamGridDB y TheGamesDB (Personalizar → Buscar)");
            return;
        }

        // Una a una, para no saturar las APIs; cada caja se actualiza en cuanto llega su imagen.
        var done = 0;
        var withArt = 0;
        foreach (var game in empty)
        {
            ShowToast($"Buscando carátulas por defecto… {++done} de {empty.Count}");
            var changed = false;
            foreach (var face in new[] { Face.Front, Face.Back })
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

        var message = isNew ? $"Filtro «{filter.Name}» creado" : "Filtro guardado";
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
        var answer = MessageBox.Show(this, $"¿Eliminar el filtro «{filter.Name}»?\n\nLos juegos que tenga no se borran: siguen en «Todos los juegos».",
            "Eliminar filtro", MessageBoxButton.YesNo, MessageBoxImage.Question);
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
        ShowToast($"Filtro «{filter.Name}» eliminado");
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
        if (IsPanelOpen)
        {
            if (action == PadAction.Confirm)
                InjectKey(Key.Enter);
            else if (action == PadAction.Back)
                InjectKey(Key.Escape);
            return;
        }

        switch (action)
        {
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
        if (IsPanelOpen)
            return;
        if (_flow.ShelfMode)
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
        RemoveHint.Visibility = ImportHints.Visibility = pad ? Visibility.Collapsed : Visibility.Visible;
        UpdateShelfHints();
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

    void StepZoom(double delta) => ZoomSlider.Value = Math.Clamp(ZoomSlider.Value + delta, ZoomSlider.Minimum, ZoomSlider.Maximum);

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

        // Con un menú abierto las teclas son para él (escribir el nombre, Enter, Esc).
        if (IsPanelOpen)
            return;

        if (_flow.ShelfMode)
        {
            if (HandleShelfKey(e.Key))
                e.Handled = true;
            return;
        }

        switch (e.Key)
        {
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
            case Key.S: OpenSteamImport(); break;
            case Key.A: OpenThemeEditor(); break;
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

    void OpenThemeEditor() => ThemeEditor.Open(ImageStore.GetGlobalBackgroundPath(_settings), _settings.TextColor,
        Games.Count(game => game.Background is not null || game.TextColor is not null));

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

    void OnSteamClick(object sender, RoutedEventArgs e) => OpenSteamImport();

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
