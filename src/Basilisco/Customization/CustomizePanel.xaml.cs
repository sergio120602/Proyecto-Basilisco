using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Basilisco.Carousel;
using Basilisco.Models;
using Basilisco.Services;
using Microsoft.Win32;

namespace Basilisco.Customization;

/// <summary>Cambio de imagen de una cara de la caja decidido en el menú.</summary>
/// <param name="NewPath">Imagen nueva elegida (aún sin copiar), o null si no se ha elegido otra.</param>
/// <param name="Remove">True si se ha quitado la imagen personalizada.</param>
public sealed record ImageChange(string? NewPath, bool Remove);

/// <summary>Lo que el usuario ha decidido en el menú de personalización.</summary>
/// <param name="NewPath">Nueva ubicación del juego, o null si no ha cambiado.</param>
/// <param name="FilterIds">Filtros a los que debe pertenecer el juego.</param>
public sealed record CustomizeResult(Game Game, string Name, string? NewPath, ImageChange Front, ImageChange Back, List<string> FilterIds);

/// <summary>
/// Menú para cambiar el nombre, la portada, la contraportada y los filtros de un juego,
/// con la caja en 3D para girarla. Los cambios solo se aplican al guardar.
/// </summary>
public partial class CustomizePanel : UserControl
{
    readonly BoxPreview _preview;
    Game? _game;
    ImageChange _front = new(null, false);
    ImageChange _back = new(null, false);
    string? _newPath;

    static readonly Brush MutedText = new SolidColorBrush(Color.FromRgb(0x5B, 0x65, 0x73));

    public event Action<CustomizeResult>? Saved;

    /// <summary>El usuario quiere buscar una portada (false) o una contraportada (true) en internet para este nombre.</summary>
    public event Action<string, bool>? ArtSearchRequested;
    public event Action? Closed;

    public bool IsOpen => Visibility == Visibility.Visible;

    public CustomizePanel()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
        _preview = new BoxPreview(PreviewViewport);
    }

    public void Open(Game game, IReadOnlyList<GameFilter> filters)
    {
        _game = game;
        _front = _back = new ImageChange(null, false);
        _newPath = null;
        ErrorText.Visibility = Visibility.Collapsed;
        ShowPath(game.Path);
        NameBox.Text = game.Name;

        FilterToggles.Children.Clear();
        foreach (var filter in filters)
        {
            FilterToggles.Children.Add(new ToggleButton
            {
                Content = filter.Name,
                Tag = filter.Id,
                IsChecked = game.FilterIds.Contains(filter.Id),
                Style = (Style)FindResource("FilterToggle"),
            });
        }
        NoFiltersText.Visibility = filters.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        Visibility = Visibility.Visible;
        UpdatePreview();
        // Prioridad Input: el panel ya se ha maquetado y es visible, así que el foco sí se queda.
        Dispatcher.BeginInvoke(() =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    /// <summary>Pone la imagen soltada en la cara de la caja que se está viendo.</summary>
    public void DropImage(string path)
    {
        if (_preview.IsShowingBack)
            SetBackImage(path);
        else
            SetFrontImage(path);
    }

    public void SetFrontImage(string path)
    {
        if (!IsValidImage(path))
            return;
        _front = new ImageChange(path, false);
        UpdatePreview();
        _preview.ShowFront();
    }

    public void SetBackImage(string path)
    {
        if (!IsValidImage(path))
            return;
        _back = new ImageChange(path, false);
        UpdatePreview();
        _preview.ShowBack();
    }

    bool IsValidImage(string path)
    {
        if (ImageStore.IsImage(path) && CoverArt.LoadImage(path) is not null)
        {
            ErrorText.Visibility = Visibility.Collapsed;
            return true;
        }

        ShowError("No se ha podido abrir esa imagen. Prueba con un PNG, JPG, BMP, GIF o WebP.");
        return false;
    }

    static string? Resolve(ImageChange change, string? current) =>
        change.NewPath ?? (change.Remove ? null : current);

    void UpdatePreview()
    {
        if (_game is null)
            return;

        var front = Resolve(_front, ImageStore.GetCoverPath(_game));
        var back = Resolve(_back, ImageStore.GetBackCoverPath(_game));
        _preview.SetItem(new CarouselItem(NameBox.Text, IsAddCard: false, front, back));
        RemoveImageButton.IsEnabled = front is not null;
        RemoveBackImageButton.IsEnabled = back is not null;
    }

    void Save()
    {
        if (_game is null)
            return;

        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            ShowError("El nombre no puede estar vacío.");
            NameBox.Focus();
            return;
        }

        var filterIds = FilterToggles.Children
            .OfType<ToggleButton>()
            .Where(toggle => toggle.IsChecked == true)
            .Select(toggle => (string)toggle.Tag)
            .ToList();

        var result = new CustomizeResult(_game, name, _newPath, _front, _back, filterIds);
        Close();
        Saved?.Invoke(result);
    }

    void Close()
    {
        _game = null;
        _preview.Clear();
        Visibility = Visibility.Collapsed;
        Closed?.Invoke();
    }

    void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    string? PickImage(string title)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = ImageStore.FileFilter };
        return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null;
    }

    void OnChooseImageClick(object sender, RoutedEventArgs e)
    {
        if (PickImage("Elegir portada") is { } path)
            SetFrontImage(path);
    }

    void OnRemoveImageClick(object sender, RoutedEventArgs e)
    {
        _front = new ImageChange(null, true);
        UpdatePreview();
        _preview.ShowFront();
    }

    void OnChooseBackImageClick(object sender, RoutedEventArgs e)
    {
        if (PickImage("Elegir contraportada") is { } path)
            SetBackImage(path);
    }

    void OnRemoveBackImageClick(object sender, RoutedEventArgs e)
    {
        _back = new ImageChange(null, true);
        UpdatePreview();
        _preview.ShowBack();
    }

    void ShowPath(string path)
    {
        var missing = !File.Exists(path);
        PathText.Text = missing ? $"No encontrado · {path}" : path;
        PathText.ToolTip = path;
        PathText.Foreground = missing ? Brushes.Firebrick : MutedText;
    }

    void OnChangeLocationClick(object sender, RoutedEventArgs e)
    {
        if (_game is null)
            return;

        var current = _newPath ?? _game.Path;
        var dialog = new OpenFileDialog
        {
            Title = $"¿Dónde está «{_game.Name}»?",
            Filter = GameLauncher.FileFilter,
            FileName = Path.GetFileName(current),
            DereferenceLinks = false,
        };
        if (Path.GetDirectoryName(current) is { } folder && Directory.Exists(folder))
            dialog.InitialDirectory = folder;

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            _newPath = dialog.FileName;
            ShowPath(_newPath);
        }
    }

    void OnSearchFrontClick(object sender, RoutedEventArgs e) => ArtSearchRequested?.Invoke(NameBox.Text.Trim(), false);

    void OnSearchBackClick(object sender, RoutedEventArgs e) => ArtSearchRequested?.Invoke(NameBox.Text.Trim(), true);

    void OnShowFrontClick(object sender, RoutedEventArgs e) => _preview.ShowFront();

    void OnShowBackClick(object sender, RoutedEventArgs e) => _preview.ShowBack();

    void OnNameChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    void OnSaveClick(object sender, RoutedEventArgs e) => Save();

    void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    // Clic fuera de la tarjeta: cerrar sin guardar.
    void OnBackdropMouseDown(object sender, MouseButtonEventArgs e) => Close();

    void OnCardMouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter: Save(); break;
            case Key.Escape: Close(); break;
            default: return;
        }
        e.Handled = true;
    }
}
