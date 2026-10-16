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
    const string ImportFilter =
        "Juegos y aplicaciones (*.exe;*.lnk;*.url;*.bat;*.cmd)|*.exe;*.lnk;*.url;*.bat;*.cmd|Todos los archivos (*.*)|*.*";

    // El logo se pinta hasta 180 px de ancho; con margen para pantallas con escalado.
    const int FilterIconDecodeWidth = 480;

    readonly LibraryStore _store = new();
    readonly Library _library;
    readonly CoverFlow _flow;

    /// <summary>Filtro seleccionado arriba; null es «Todos los juegos».</summary>
    GameFilter? _activeFilter;

    /// <summary>Juegos que se ven en el carrusel con el filtro actual, en el mismo orden.</summary>
    List<Game> _visibleGames = [];

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
        FilterEditor.Saved += ApplyFilter;
        FilterEditor.DeleteRequested += DeleteFilter;

        Viewport.SizeChanged += (_, _) => UpdateOverlayLayout();
    }

    List<Game> Games => _library.Games;
    List<GameFilter> Filters => _library.Filters;

    bool IsPanelOpen => Customizer.IsOpen || FilterEditor.IsOpen;

    bool IsAddCard(int index) => index >= _visibleGames.Count;

    bool BelongsToActiveFilter(Game game) => _activeFilter is null || game.FilterIds.Contains(_activeFilter.Id);

    int CountGames(GameFilter filter) => Games.Count(game => game.FilterIds.Contains(filter.Id));

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
        new(game.Name, IsAddCard: false, ImageStore.GetCoverPath(game));

    void RefreshFilterTabs()
    {
        var tabs = new List<FilterTab>
        {
            new() { Label = $"Todos los juegos ({Games.Count})", IsSelected = _activeFilter is null },
        };

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

    void SelectFilter(GameFilter? filter)
    {
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
        var options = new List<GameFilter?> { null };
        options.AddRange(Filters);
        var current = options.IndexOf(_activeFilter);
        SelectFilter(options[(current + direction + options.Count) % options.Count]);
    }

    void ShowDetails(int index)
    {
        if (IsAddCard(index))
        {
            TitleText.Text = "Importar juego";
            Chips.ItemsSource = _activeFilter is { } filter && _visibleGames.Count == 0
                ? new[] { $"«{filter.Name}» está vacío", "Asigna juegos con E desde «Todos los juegos»", "o importa juegos nuevos aquí" }
                : new[] { "Ejecutables, accesos directos y scripts", "También puedes arrastrar archivos aquí" };
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
            Title = _activeFilter is null ? "Importar juego" : $"Importar juego a «{_activeFilter.Name}»",
            Filter = ImportFilter,
            Multiselect = true,
            // Queremos el acceso directo tal cual, no el archivo al que apunta, para conservar sus argumentos.
            DereferenceLinks = false,
        };

        if (dialog.ShowDialog(this) == true)
            AddGames(dialog.FileNames);
    }

    void AddGames(IEnumerable<string> paths)
    {
        Game? first = null;
        var added = 0;
        var skippedFolders = false;

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                skippedFolders = true;
                continue;
            }
            if (!File.Exists(path))
                continue;

            var game = Games.FirstOrDefault(g => string.Equals(g.Path, path, StringComparison.OrdinalIgnoreCase));
            if (game is null)
            {
                game = new Game
                {
                    Name = Path.GetFileNameWithoutExtension(path),
                    Path = path,
                    AddedAt = DateTime.Now,
                };
                Games.Add(game);
            }
            else if (BelongsToActiveFilter(game))
            {
                continue;
            }

            // Lo que se importa dentro de un filtro entra directamente en él.
            if (_activeFilter is not null)
                game.FilterIds.Add(_activeFilter.Id);

            first ??= game;
            added++;
        }

        if (first is null)
        {
            ShowToast(skippedFolders
                ? "La importación de carpetas llegará en una próxima versión"
                : "Ya estaba en tu biblioteca");
            return;
        }

        Save();
        RefreshFilterTabs();
        RefreshCarousel(0);
        _flow.Select(_visibleGames.IndexOf(first));

        var target = _activeFilter is null ? "la biblioteca" : $"«{_activeFilter.Name}»";
        ShowToast(added == 1 ? $"«{first.Name}» añadido a {target}" : $"{added} juegos añadidos a {target}");
    }

    void Launch(Game game)
    {
        if (!File.Exists(game.Path))
        {
            MessageBox.Show(this, $"No se encuentra «{game.Name}» en:\n\n{game.Path}\n\n¿Se ha movido o desinstalado?",
                "Basilisco", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
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
        game.FilterIds = result.FilterIds;
        var message = "Cambios guardados";
        try
        {
            if (result.NewImagePath is { } image)
                ImageStore.SetCover(game, image);
            else if (result.RemoveImage)
                ImageStore.RemoveCover(game);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = "No se pudo copiar la imagen; el resto de cambios sí se han guardado";
        }

        Save();
        RefreshFilterTabs();

        if (BelongsToActiveFilter(game))
        {
            _flow.UpdateItem(index, ToCarouselItem(game));
            ShowDetails(index);
        }
        else
        {
            // Lo has sacado del filtro que estás viendo: desaparece del carrusel.
            RefreshCarousel(index);
            message = $"«{game.Name}» ya no está en «{_activeFilter!.Name}»";
        }
        ShowToast(message);
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
            SelectFilter(null);
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
            case Key.I: Import(); break;
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

        if (index == _flow.SelectedIndex)
            Activate(index);
        else
            _flow.Select(index);
    }

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
        else if (tab.Filter != _activeFilter)
            SelectFilter(tab.Filter);
    }

    void OnFilterTabRightClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FilterTab { Filter: { } filter })
            OpenFilterEditor(filter);
    }

    void OnImportClick(object sender, RoutedEventArgs e) => Import();

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
            if (paths.FirstOrDefault(ImageStore.IsImage) is not { } image)
                return;

            if (Customizer.IsOpen)
                Customizer.SetImage(image);
            else
                FilterEditor.SetImage(image);
            return;
        }

        AddGames(paths);
    }
}
