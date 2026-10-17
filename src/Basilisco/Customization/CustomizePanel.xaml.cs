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
/// <param name="Platform">Consola elegida para el juego (null: sin consola).</param>
/// <param name="FilterIds">Filtros a los que debe pertenecer el juego.</param>
public sealed record CustomizeResult(
    Game Game, string Name, string? NewPath, ImageChange Front, ImageChange Back, ImageChange Spine,
    ImageFrame BackFrame, ImageFrame SpineFrame, string? Platform, List<string> FilterIds);

/// <summary>Cara de la caja para la que se busca una imagen en internet.</summary>
public enum ArtTarget { Front, Back, Spine }

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
    ImageChange _spine = new(null, false);
    ImageFrame _backFrame = ImageFrame.Default;
    ImageFrame _spineFrame = ImageFrame.Default;
    string? _newPath;
    string? _platform;

    static readonly Brush MutedText = new SolidColorBrush(Color.FromRgb(0x5B, 0x65, 0x73));

    public event Action<CustomizeResult>? Saved;

    /// <summary>El usuario quiere buscar en internet una imagen para esa cara de la caja, con este nombre.</summary>
    public event Action<string, ArtTarget>? ArtSearchRequested;
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
        _front = _back = _spine = new ImageChange(null, false);
        _backFrame = game.BackFrame ?? ImageFrame.Default;
        _spineFrame = game.SpineFrame ?? ImageFrame.Default;
        _newPath = null;
        ErrorText.Visibility = Visibility.Collapsed;
        ShowPath(game.Path);
        NameBox.Text = game.Name;

        _platform = game.Platform;
        PlatformToggles.Children.Clear();
        foreach (var option in PlatformOption.All)
        {
            var toggle = new ToggleButton
            {
                Content = option.Name,
                Tag = option,
                IsChecked = option.Id == _platform,
                Style = (Style)FindResource("FilterToggle"),
            };
            toggle.Click += OnPlatformClick;
            PlatformToggles.Children.Add(toggle);
        }

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
        if (FrameEditor.IsOpen)
            return;
        if (_preview.IsShowingSpine)
            SetSpineImage(path);
        else if (_preview.IsShowingBack)
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
        _backFrame = ImageFrame.Default; // Una imagen nueva empieza entera.
        UpdatePreview();
        _preview.ShowBack();
    }

    public void SetSpineImage(string path)
    {
        if (!IsValidImage(path))
            return;
        _spine = new ImageChange(path, false);
        // Un banner girado queda muy estrecho entero: por defecto se llena el lomo.
        _spineFrame = CoverArt.LoadImage(path) is { } image
            ? new ImageFrame(CoverArt.FillZoom(new Size(image.Width, image.Height), CoverArt.SpineSize, rotateWide: true))
            : ImageFrame.Default;
        UpdatePreview();
        _preview.ShowSpine();
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
        var spine = Resolve(_spine, ImageStore.GetSpinePath(_game));
        _preview.SetItem(CurrentItem());
        RemoveImageButton.IsEnabled = front is not null;
        RemoveBackImageButton.IsEnabled = FrameBackButton.IsEnabled = back is not null;
        RemoveSpineImageButton.IsEnabled = FrameSpineButton.IsEnabled = spine is not null;
    }

    /// <summary>La caja con todo lo elegido hasta ahora (aún sin guardar).</summary>
    CarouselItem CurrentItem() => new(
        NameBox.Text, IsAddCard: false,
        Resolve(_front, ImageStore.GetCoverPath(_game!)),
        Resolve(_back, ImageStore.GetBackCoverPath(_game!)),
        _platform,
        Resolve(_spine, ImageStore.GetSpinePath(_game!)),
        _backFrame, _spineFrame);

    /// <summary>Abre el editor de encuadre para la contraportada (back) o el lomo.</summary>
    void OpenFrameEditor(bool back)
    {
        if (_game is null)
            return;

        var item = CurrentItem();
        var path = back ? item.BackImagePath : item.SpineImagePath;
        if (path is null || CoverArt.LoadImage(path) is not { } image)
            return;

        var size = new Size(image.Width, image.Height);
        if (back)
        {
            _preview.ShowBack();
            FrameEditor.Open("Ajustar contraportada", _backFrame,
                CoverArt.FillZoom(size, CoverArt.BackSize, rotateWide: false),
                frame => CoverArt.RenderBack(item with { BackFrame = frame }),
                frame => { _backFrame = frame; UpdatePreview(); _preview.ShowBack(); });
        }
        else
        {
            _preview.ShowSpine();
            FrameEditor.Open("Ajustar lomo", _spineFrame,
                CoverArt.FillZoom(size, CoverArt.SpineSize, rotateWide: true),
                frame => CoverArt.RenderSpine(item with { SpineFrame = frame }),
                frame => { _spineFrame = frame; UpdatePreview(); _preview.ShowSpine(); });
        }
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

        var result = new CustomizeResult(_game, name, _newPath, _front, _back, _spine, _backFrame, _spineFrame, _platform, filterIds);
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

    void OnChooseSpineImageClick(object sender, RoutedEventArgs e)
    {
        if (PickImage("Elegir imagen del lomo") is { } path)
            SetSpineImage(path);
    }

    void OnRemoveSpineImageClick(object sender, RoutedEventArgs e)
    {
        _spine = new ImageChange(null, true);
        _spineFrame = ImageFrame.Default;
        UpdatePreview();
        _preview.ShowSpine();
    }

    void OnFrameBackClick(object sender, RoutedEventArgs e) => OpenFrameEditor(back: true);

    void OnFrameSpineClick(object sender, RoutedEventArgs e) => OpenFrameEditor(back: false);

    void OnShowSpineClick(object sender, RoutedEventArgs e) => _preview.ShowSpine();

    void OnRemoveBackImageClick(object sender, RoutedEventArgs e)
    {
        _back = new ImageChange(null, true);
        _backFrame = ImageFrame.Default;
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

    /// <summary>Solo una consola a la vez: al elegir una se desmarcan las demás y la caja cambia de borde.</summary>
    void OnPlatformClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: PlatformOption option })
            return;

        _platform = option.Id;
        foreach (var toggle in PlatformToggles.Children.OfType<ToggleButton>())
            toggle.IsChecked = Equals(toggle.Tag, option);
        UpdatePreview();
        _preview.ShowFront();
    }

    void OnSearchFrontClick(object sender, RoutedEventArgs e) => ArtSearchRequested?.Invoke(NameBox.Text.Trim(), ArtTarget.Front);

    void OnSearchBackClick(object sender, RoutedEventArgs e) => ArtSearchRequested?.Invoke(NameBox.Text.Trim(), ArtTarget.Back);

    void OnSearchSpineClick(object sender, RoutedEventArgs e) => ArtSearchRequested?.Invoke(NameBox.Text.Trim(), ArtTarget.Spine);

    // El lado derecho ocupa lo que permita la ventana; si no cabe, se desplaza.
    void OnSizeChanged(object sender, SizeChangedEventArgs e) => SideScroll.MaxHeight = Math.Max(300, ActualHeight - 200);

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
        // Con el editor de encuadre abierto, Enter y Esc son para él.
        if (FrameEditor.IsOpen)
            return;

        switch (e.Key)
        {
            case Key.Enter: Save(); break;
            case Key.Escape: Close(); break;
            default: return;
        }
        e.Handled = true;
    }
}
