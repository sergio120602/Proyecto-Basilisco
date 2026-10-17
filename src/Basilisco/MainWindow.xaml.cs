using System.IO;
using System.Windows;
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
    readonly TheGamesDbProvider _theGamesDb = new();
    readonly TheGamesDbProvider _theGamesDbBanners = new(banners: true);
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
        FilterEditor.Saved += ApplyFilter;
        FilterEditor.DeleteRequested += DeleteFilter;
        FolderImporter.ImportRequested += ApplyFolderImport;
        PlatformPicker.Saved += ApplyPlatforms;

        Viewport.SizeChanged += (_, _) => UpdateOverlayLayout();
        Loaded += async (_, _) => await RepairMovedGamesAsync();
    }

    /// <summary>
    /// Busca los juegos cuyo archivo ya no está donde estaba (porque has movido su carpeta)
    /// y actualiza su ubicación sin perder portadas, filtros ni partidas.
    /// </summary>
    async Task RepairMovedGamesAsync()
    {
        var missing = Games.Where(game => !File.Exists(game.Path)).Select(game => (Game: game, game.Path)).ToList();
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
        var index = game is null ? -1 : Games.Where(BelongsToActiveFilter).ToList().IndexOf(game);
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
        if (keep.CoverImage is null)
            (keep.CoverImage, duplicate.CoverImage) = (duplicate.CoverImage, null);
        if (keep.BackImage is null)
            (keep.BackImage, keep.BackFrame, duplicate.BackImage) = (duplicate.BackImage, duplicate.BackFrame, null);
        if (keep.SpineImage is null)
            (keep.SpineImage, keep.SpineFrame, duplicate.SpineImage) = (duplicate.SpineImage, duplicate.SpineFrame, null);
        ImageStore.RemoveSpine(duplicate);
        ImageStore.RemoveCover(duplicate);
        ImageStore.RemoveBackCover(duplicate);

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
            !File.Exists(game.Path));

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

    bool IsPanelOpen => Customizer.IsOpen || FilterEditor.IsOpen || FolderImporter.IsOpen || ArtSearch.IsOpen || PlatformPicker.IsOpen;

    bool IsAddCard(int index) => index >= _visibleGames.Count;

    bool BelongsToActiveFilter(Game game) =>
        (_activePlatform is null || game.Platform == _activePlatform) &&
        (_activeFilter is null || game.FilterIds.Contains(_activeFilter.Id));

    int CountGames(GameFilter filter) => Games.Count(game => game.FilterIds.Contains(filter.Id));

    int CountGames(Platform platform) => Games.Count(game => game.Platform == platform.Id);

    void RefreshCarousel(int selectedIndex, bool jump = false)
    {
        _visibleGames = Games.Where(BelongsToActiveFilter).ToList();
        var items = _visibleGames
            .Select(ToCarouselItem)
            .Append(new CarouselItem("Importar juego", IsAddCard: true))
            .ToList();

        _flow.SetItems(items, selectedIndex, jump);
    }

    static CarouselItem ToCarouselItem(Game game) =>
        new(game.Name, IsAddCard: false, ImageStore.GetCoverPath(game), ImageStore.GetBackCoverPath(game), game.Platform,
            ImageStore.GetSpinePath(game), game.BackFrame, game.SpineFrame);

    void RefreshFilterTabs()
    {
        var tabs = new List<FilterTab>
        {
            new() { Label = $"Todos los juegos ({Games.Count})", IsSelected = _activeFilter is null && _activePlatform is null },
        };

        // Las consolas solo salen arriba si tienen algún juego (o si es la que estás viendo).
        foreach (var platform in Platforms.All)
        {
            var count = CountGames(platform);
            if (count > 0 || platform.Id == _activePlatform)
                tabs.Add(new FilterTab { Platform = platform, Label = $"{platform.Name} ({count})", IsSelected = platform.Id == _activePlatform });
        }

        foreach (var filter in Filters)
        {
            var image = ImageStore.GetFilterImagePath(filter);
            tabs.Add(new FilterTab
            {
                Filter = filter,
                Label = $"{filter.Name} ({CountGames(filter)})",
                Icon = image is null ? null : CoverArt.LoadImage(image, FilterIconDecodeWidth),
                IsSelected = filter == _activeFilter,
            });
        }

        tabs.Add(new FilterTab { Label = "+  Nuevo filtro", IsAddButton = true });
        FilterTabs.ItemsSource = tabs;

        // Si hay muchos filtros, que el seleccionado no quede escondido fuera de la barra.
        var selected = tabs.FindIndex(tab => tab.IsSelected);
        Dispatcher.BeginInvoke(() =>
        {
            if (FilterTabs.ItemContainerGenerator.ContainerFromIndex(selected) is FrameworkElement container)
                container.BringIntoView();
        });
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

        TitlePanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });
    }

    List<string> DescribeGame(Game game)
    {
        var chips = new List<string>
        {
            Path.GetExtension(game.Path).ToLowerInvariant() switch
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

        var folder = Path.GetFileName(Path.GetDirectoryName(game.Path));
        if (!string.IsNullOrEmpty(folder))
            chips.Add($"Carpeta: '{folder}'");

        if (!File.Exists(game.Path))
            chips.Add("Archivo no encontrado");

        return chips;
    }

    void UpdateOverlayLayout()
    {
        if (Viewport.ActualWidth <= 0)
            return;

        var coverTop = _flow.Project(new Point3D(0, CoverFlow.CoverHeight, 0));
        TitlePanel.Margin = new Thickness(0, 0, 0, Math.Max(0, Viewport.ActualHeight - coverTop.Y + 24));

        // El suelo empieza por detrás de las cajas laterales para que todas apoyen sobre él.
        var horizon = _flow.Project(new Point3D(0, 0, -2.5));
        Floor.Margin = new Thickness(0, Math.Max(0, horizon.Y - 40), 0, 0);
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
    void AddGames(IEnumerable<(string Path, string? Name)> games, GameFilter? groupFilter = null)
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
        PlatformPicker.Open(imported, _activePlatform);
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
        if (!File.Exists(game.Path))
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
            GameLauncher.Launch(game);
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

        ImageStore.RemoveCover(game);
        ImageStore.RemoveBackCover(game);
        ImageStore.RemoveSpine(game);
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
        game.Platform = result.Platform;
        // El encuadre por defecto no se guarda: así el archivo queda limpio y se reconoce como «entera».
        game.BackFrame = result.BackFrame == ImageFrame.Default ? null : result.BackFrame;
        game.SpineFrame = result.SpineFrame == ImageFrame.Default ? null : result.SpineFrame;
        game.FilterIds = result.FilterIds;
        var message = "Cambios guardados";
        try
        {
            if (result.Front.NewPath is { } front)
                ImageStore.SetCover(game, front);
            else if (result.Front.Remove)
                ImageStore.RemoveCover(game);

            if (result.Back.NewPath is { } back)
                ImageStore.SetBackCover(game, back);
            else if (result.Back.Remove)
                ImageStore.RemoveBackCover(game);

            if (result.Spine.NewPath is { } spine)
                ImageStore.SetSpine(game, spine);
            else if (result.Spine.Remove)
                ImageStore.RemoveSpine(game);
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
    void OpenArtSearch(string gameName, ArtTarget target)
    {
        switch (target)
        {
            case ArtTarget.Front: ArtSearch.Open(_steamGridDb, _settings, gameName, Customizer.SetFrontImage); break;
            case ArtTarget.Back: ArtSearch.Open(_theGamesDb, _settings, gameName, Customizer.SetBackImage); break;
            case ArtTarget.Spine: ArtSearch.Open(_theGamesDbBanners, _settings, gameName, Customizer.SetSpineImage); break;
        }
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

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Con un menú abierto las teclas son para él (escribir el nombre, Enter, Esc).
        if (IsPanelOpen)
            return;

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
            case Key.F11: ToggleFullScreen(); break;
            case Key.Escape when _fullScreen: ToggleFullScreen(); break;
            default: return;
        }
        e.Handled = true;
    }

    void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (IsPanelOpen)
            return;

        // Acumulamos para que los touchpads (que mandan deltas pequeños) no aceleren el carrusel.
        _wheelDelta += e.Delta;
        while (Math.Abs(_wheelDelta) >= Mouse.MouseWheelDeltaForOneLine)
        {
            var step = Math.Sign(_wheelDelta);
            _flow.MoveBy(-step);
            _wheelDelta -= step * Mouse.MouseWheelDeltaForOneLine;
        }
    }

    void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_flow.HitTest(e.GetPosition(Viewport)) is not { } index)
            return;

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
        if (_flow.HitTest(e.GetPosition(Viewport)) is not { } index || IsAddCard(index))
            return;

        _flow.Select(index);
        OpenCustomizer(index);
    }

    void OnFilterTabClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not FilterTab tab)
            return;

        if (tab.IsAddButton)
            OpenFilterEditor(null);
        else if (tab.Filter != _activeFilter || tab.Platform?.Id != _activePlatform)
            SelectView(tab.Platform?.Id, tab.Filter);
    }

    void OnFilterTabRightClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FilterTab { Filter: { } filter })
            OpenFilterEditor(filter);
    }

    void OnImportClick(object sender, RoutedEventArgs e) => Import();

    void OnImportFolderClick(object sender, RoutedEventArgs e) => ImportFolder();

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
