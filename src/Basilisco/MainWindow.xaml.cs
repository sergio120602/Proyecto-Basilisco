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

    readonly LibraryStore _store = new();
    readonly List<Game> _games;
    readonly CoverFlow _flow;
    int _wheelDelta;
    bool _fullScreen;

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = $"v{AppInfo.Version}";

        _games = _store.Load();
        _flow = new CoverFlow(Viewport);
        _flow.SelectedIndexChanged += ShowDetails;
        RefreshCarousel(0);

        Customizer.Saved += ApplyCustomization;

        Viewport.SizeChanged += (_, _) => UpdateOverlayLayout();
    }

    bool IsAddCard(int index) => index >= _games.Count;

    void RefreshCarousel(int selectedIndex)
    {
        var items = _games
            .Select(ToCarouselItem)
            .Append(new CarouselItem("Importar juego", IsAddCard: true))
            .ToList();

        AllGamesTab.Text = $"Todos los juegos ({_games.Count})";
        _flow.SetItems(items, selectedIndex);
    }

    static CarouselItem ToCarouselItem(Game game) =>
        new(game.Name, IsAddCard: false, CoverStore.GetPath(game));

    void ShowDetails(int index)
    {
        if (IsAddCard(index))
        {
            TitleText.Text = "Importar juego";
            Chips.ItemsSource = new[] { "Ejecutables, accesos directos y scripts", "También puedes arrastrar archivos aquí" };
            ActivateHint.Text = "Importar";
            GameHints.Visibility = Visibility.Collapsed;
        }
        else
        {
            var game = _games[index];
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

    static List<string> DescribeGame(Game game)
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
            Launch(_games[index]);
    }

    void Import()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importar juego",
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
        var firstNew = -1;
        var added = 0;
        var skippedFolders = false;

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                skippedFolders = true;
                continue;
            }
            if (!File.Exists(path) || _games.Any(g => string.Equals(g.Path, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            _games.Add(new Game
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Path = path,
                AddedAt = DateTime.Now,
            });
            if (firstNew < 0)
                firstNew = _games.Count - 1;
            added++;
        }

        if (added == 0)
        {
            ShowToast(skippedFolders
                ? "La importación de carpetas llegará en una próxima versión"
                : "Ya estaba en tu biblioteca");
            return;
        }

        Save();
        RefreshCarousel(firstNew);
        ShowToast(added == 1 ? $"«{_games[firstNew].Name}» añadido a la biblioteca" : $"{added} juegos añadidos a la biblioteca");
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

        var game = _games[index];
        var answer = MessageBox.Show(this, $"¿Quitar «{game.Name}» de la biblioteca?\n\nEl juego no se borrará de tu disco.",
            "Quitar juego", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        CoverStore.RemoveCover(game);
        _games.RemoveAt(index);
        Save();
        RefreshCarousel(index);
    }

    void OpenCustomizer(int index)
    {
        if (!IsAddCard(index))
            Customizer.Open(_games[index]);
    }

    void ApplyCustomization(CustomizeResult result)
    {
        var game = result.Game;
        var index = _games.IndexOf(game);
        if (index < 0)
            return;

        game.Name = result.Name;
        var message = "Cambios guardados";
        try
        {
            if (result.NewImagePath is { } image)
                CoverStore.SetCover(game, image);
            else if (result.RemoveImage)
                CoverStore.RemoveCover(game);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = "No se pudo copiar la imagen; el resto de cambios sí se han guardado";
        }

        Save();
        _flow.UpdateItem(index, ToCarouselItem(game));
        ShowDetails(index);
        ShowToast(message);
    }

    void Save()
    {
        if (!_store.Save(_games))
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
        // Con el menú abierto las teclas son para él (escribir el nombre, Enter, Esc).
        if (Customizer.IsOpen)
            return;

        switch (e.Key)
        {
            case Key.Left: _flow.MoveBy(-1); break;
            case Key.Right: _flow.MoveBy(1); break;
            case Key.Home: _flow.Select(0); break;
            case Key.End: _flow.Select(_flow.Count - 1); break;
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
        if (Customizer.IsOpen)
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

        // Con el menú abierto, soltar una imagen la usa como portada del juego.
        if (Customizer.IsOpen)
        {
            if (paths.FirstOrDefault(CoverStore.IsImage) is { } image)
                Customizer.SetImage(image);
            return;
        }

        AddGames(paths);
    }
}
