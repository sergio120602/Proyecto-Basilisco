using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Basilisco.Carousel;
using Basilisco.Models;
using Basilisco.Services;
using Microsoft.Win32;

namespace Basilisco.Customization;

/// <summary>Cambio de imagen de una cara de la caja decidido en el menú.</summary>
/// <param name="NewPath">Imagen nueva elegida (aún sin copiar), o null si no se ha elegido otra.</param>
/// <param name="Remove">True si se ha quitado la imagen personalizada.</param>
public sealed record ImageChange(string? NewPath, bool Remove)
{
    public static readonly ImageChange None = new(null, false);
}

/// <summary>Cambios pendientes de las imágenes de un formato (cada formato tiene las suyas).</summary>
public sealed class PendingFaces
{
    readonly ImageChange[] _changes = [ImageChange.None, ImageChange.None, ImageChange.None];
    readonly ImageFrame[] _frames = [ImageFrame.Default, ImageFrame.Default, ImageFrame.Default];

    public PendingFaces(FaceSet? saved)
    {
        foreach (var face in Enum.GetValues<Face>())
            _frames[(int)face] = saved?.GetFrame(face) ?? ImageFrame.Default;
    }

    public ImageChange Change(Face face) => _changes[(int)face];
    public void SetChange(Face face, ImageChange change) => _changes[(int)face] = change;

    public ImageFrame Frame(Face face) => _frames[(int)face];
    public void SetFrame(Face face, ImageFrame frame) => _frames[(int)face] = frame;
}

/// <summary>Lo que el usuario ha decidido en el menú de personalización.</summary>
/// <param name="NewPath">Nueva ubicación del juego, o null si no ha cambiado.</param>
/// <param name="Format">Formato con el que se verá la caja.</param>
/// <param name="Faces">Cambios de imágenes de cada formato.</param>
/// <param name="Platform">Consola elegida para el juego (null: sin consola).</param>
/// <param name="FilterIds">Filtros a los que debe pertenecer el juego.</param>
public sealed record CustomizeResult(
    Game Game, string Name, string? NewPath, CoverFormat Format, IReadOnlyDictionary<CoverFormat, PendingFaces> Faces,
    string? Platform, List<string> FilterIds);

/// <summary>Cara de la caja para la que se busca una imagen en internet.</summary>
public enum ArtTarget { Front, Back, Spine }

/// <summary>
/// Menú para cambiar el nombre, el formato, las imágenes, la consola y los filtros de un juego,
/// con la caja en 3D para girarla. Los cambios solo se aplican al guardar.
/// </summary>
public partial class CustomizePanel : UserControl
{
    readonly BoxPreview _preview;
    Game? _game;
    Dictionary<CoverFormat, PendingFaces> _pending = [];
    CoverFormat _format;
    string? _newPath;
    string? _platform;
    CancellationTokenSource? _autoFetch;

    static readonly Brush MutedText = new SolidColorBrush(Color.FromRgb(0x5B, 0x65, 0x73));

    public event Action<CustomizeResult>? Saved;

    /// <summary>
    /// El usuario quiere buscar en internet una imagen para esa cara de la caja: nombre, cara, formato,
    /// consola del juego y si es en LaunchBox (la segunda opción) en lugar del servicio habitual.
    /// </summary>
    public event Action<string, ArtTarget, CoverFormat, string?, bool>? ArtSearchRequested;
    public event Action? Closed;

    /// <summary>
    /// Descarga sin preguntar una imagen por defecto para una cara (la pone la ventana principal, que tiene
    /// las claves de las APIs). Devuelve la ruta descargada, o null si no hay o el servicio no responde.
    /// </summary>
    public Func<string, string?, CoverFormat, Face, CancellationToken, Task<string?>>? AutoFetch { get; set; }

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
        _format = game.Format;
        _pending = new()
        {
            [CoverFormat.Square] = new PendingFaces(game.Square),
            [CoverFormat.Portrait] = new PendingFaces(game.Portrait),
        };
        _newPath = null;
        ErrorText.Visibility = Visibility.Collapsed;
        AutoFetchText.Visibility = Visibility.Collapsed;
        ShowPath(game.Path);
        NameBox.Text = game.Name;
        SyncFormatToggles();

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

    PendingFaces Pending => _pending[_format];

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

    public void SetFrontImage(string path) => SetImage(Face.Front, path);

    public void SetBackImage(string path) => SetImage(Face.Back, path);

    public void SetSpineImage(string path) => SetImage(Face.Spine, path);

    void SetImage(Face face, string path)
    {
        if (!IsValidImage(path))
            return;

        Pending.SetChange(face, new ImageChange(path, false));
        // Una imagen nueva empieza entera; un banner girado queda muy estrecho entero, así que en el lomo se llena.
        Pending.SetFrame(face, face == Face.Spine && CoverArt.LoadImage(path) is { } image
            ? new ImageFrame(CoverArt.FillZoom(new Size(image.Width, image.Height), CoverArt.SpineSize(_format, _platform), rotateWide: true))
            : ImageFrame.Default);
        UpdatePreview();
        ShowFace(face);
    }

    void ShowFace(Face face)
    {
        switch (face)
        {
            case Face.Front: _preview.ShowFront(); break;
            case Face.Back: _preview.ShowBack(); break;
            default: _preview.ShowSpine(); break;
        }
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

    /// <summary>Imagen que tendrá esa cara en ese formato si se guarda ahora.</summary>
    string? CurrentPath(CoverFormat format, Face face)
    {
        var change = _pending[format].Change(face);
        if (change.NewPath is not null || change.Remove || _game is null)
            return change.NewPath;
        var saved = format == CoverFormat.Square ? _game.Square : _game.Portrait;
        return saved?.Get(face) is { } file ? Path.Combine(AppInfo.CoversDirectory, file) : null;
    }

    void UpdatePreview()
    {
        if (_game is null)
            return;

        _preview.SetItem(CurrentItem());
        var front = CurrentPath(_format, Face.Front) is not null;
        var back = CurrentPath(_format, Face.Back) is not null;
        var spine = CurrentPath(_format, Face.Spine) is not null;
        RemoveImageButton.IsEnabled = FrameFrontButton.IsEnabled = front;
        RemoveBackImageButton.IsEnabled = FrameBackButton.IsEnabled = back;
        RemoveSpineImageButton.IsEnabled = FrameSpineButton.IsEnabled = spine;
    }

    /// <summary>La caja con todo lo elegido hasta ahora (aún sin guardar).</summary>
    CarouselItem CurrentItem() => new(
        NameBox.Text, IsAddCard: false,
        CurrentPath(_format, Face.Front),
        CurrentPath(_format, Face.Back),
        _platform,
        CurrentPath(_format, Face.Spine),
        Pending.Frame(Face.Back), Pending.Frame(Face.Spine),
        _format, Pending.Frame(Face.Front));

    /// <summary>Abre el editor de encuadre para una cara.</summary>
    void OpenFrameEditor(Face face)
    {
        if (_game is null || CurrentPath(_format, face) is not { } path || CoverArt.LoadImage(path) is not { } image)
            return;

        var item = CurrentItem();
        var pending = Pending;
        var size = new Size(image.Width, image.Height);
        ShowFace(face);

        string title;
        double fill;
        Func<ImageFrame, BitmapSource> render;
        switch (face)
        {
            case Face.Front:
                title = "Ajustar portada";
                fill = CoverArt.FillZoom(size, CoverArt.FrontArtSize(_format, _platform), rotateWide: false);
                render = frame => CoverArt.RenderCover(item with { FrontFrame = frame });
                break;
            case Face.Back:
                title = "Ajustar contraportada";
                fill = CoverArt.FillZoom(size, CoverArt.FaceSize(_format, _platform), rotateWide: false);
                render = frame => CoverArt.RenderBack(item with { BackFrame = frame });
                break;
            default:
                title = "Ajustar lomo";
                fill = CoverArt.FillZoom(size, CoverArt.SpineSize(_format, _platform), rotateWide: true);
                render = frame => CoverArt.RenderSpine(item with { SpineFrame = frame });
                break;
        }

        FrameEditor.Open(title, pending.Frame(face), fill, render, frame =>
        {
            pending.SetFrame(face, frame);
            UpdatePreview();
            ShowFace(face);
        });
    }

    void SelectFormat(CoverFormat format)
    {
        if (_format == format)
            return;

        _format = format;
        SyncFormatToggles();
        UpdatePreview();
        _preview.ShowFront();

        // Formato nuevo sin carátula: se intenta poner una por defecto (SteamGridDB delante y TheGamesDB detrás).
        if (CurrentPath(format, Face.Front) is null && CurrentPath(format, Face.Back) is null)
            _ = AutoFetchAsync(format);
    }

    async Task AutoFetchAsync(CoverFormat format)
    {
        if (AutoFetch is null || _game is not { } game)
            return;

        _autoFetch?.Cancel();
        var cancellation = (_autoFetch = new CancellationTokenSource()).Token;
        AutoFetchText.Text = "Buscando una carátula por defecto…";
        AutoFetchText.Visibility = Visibility.Visible;

        var name = NameBox.Text.Trim();
        var found = 0;
        foreach (var face in new[] { Face.Front, Face.Back })
        {
            var path = await AutoFetch(name, _platform, format, face, cancellation);
            // Si mientras tanto has cerrado, cambiado de juego o puesto una imagen tú, no se toca nada.
            if (cancellation.IsCancellationRequested || _game != game || path is null || CurrentPath(format, face) is not null)
                continue;

            _pending[format].SetChange(face, new ImageChange(path, false));
            _pending[format].SetFrame(face, ImageFrame.Default);
            found++;
            if (_format == format)
                UpdatePreview();
        }

        if (cancellation.IsCancellationRequested || _game != game)
            return;
        AutoFetchText.Text = found > 0
            ? "Se ha puesto una carátula por defecto. Puedes cambiarla o ajustarla."
            : "No se ha encontrado carátula por defecto (o falta la clave de la API). Puedes buscarla o elegirla tú.";
    }

    void SyncFormatToggles()
    {
        SquareToggle.IsChecked = _format == CoverFormat.Square;
        PortraitToggle.IsChecked = _format == CoverFormat.Portrait;
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

        var result = new CustomizeResult(_game, name, _newPath, _format, _pending, _platform, filterIds);
        Close();
        Saved?.Invoke(result);
    }

    void Close()
    {
        _autoFetch?.Cancel();
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

    void RemoveImage(Face face)
    {
        Pending.SetChange(face, new ImageChange(null, true));
        Pending.SetFrame(face, ImageFrame.Default);
        UpdatePreview();
        ShowFace(face);
    }

    void OnChooseImageClick(object sender, RoutedEventArgs e)
    {
        if (PickImage("Elegir portada") is { } path)
            SetFrontImage(path);
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

    void OnRemoveImageClick(object sender, RoutedEventArgs e) => RemoveImage(Face.Front);

    void OnRemoveBackImageClick(object sender, RoutedEventArgs e) => RemoveImage(Face.Back);

    void OnRemoveSpineImageClick(object sender, RoutedEventArgs e) => RemoveImage(Face.Spine);

    void OnFrameFrontClick(object sender, RoutedEventArgs e) => OpenFrameEditor(Face.Front);

    void OnFrameBackClick(object sender, RoutedEventArgs e) => OpenFrameEditor(Face.Back);

    void OnFrameSpineClick(object sender, RoutedEventArgs e) => OpenFrameEditor(Face.Spine);

    void OnSquareClick(object sender, RoutedEventArgs e) => SelectFormat(CoverFormat.Square);

    void OnPortraitClick(object sender, RoutedEventArgs e) => SelectFormat(CoverFormat.Portrait);

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

    void RequestSearch(ArtTarget target, bool launchBox) =>
        ArtSearchRequested?.Invoke(NameBox.Text.Trim(), target, _format, _platform, launchBox);

    void OnSearchFrontClick(object sender, RoutedEventArgs e) => RequestSearch(ArtTarget.Front, launchBox: false);

    void OnSearchBackClick(object sender, RoutedEventArgs e) => RequestSearch(ArtTarget.Back, launchBox: false);

    void OnSearchSpineClick(object sender, RoutedEventArgs e) => RequestSearch(ArtTarget.Spine, launchBox: false);

    void OnLaunchBoxFrontClick(object sender, RoutedEventArgs e) => RequestSearch(ArtTarget.Front, launchBox: true);

    void OnLaunchBoxBackClick(object sender, RoutedEventArgs e) => RequestSearch(ArtTarget.Back, launchBox: true);

    void OnLaunchBoxSpineClick(object sender, RoutedEventArgs e) => RequestSearch(ArtTarget.Spine, launchBox: true);

    // El lado derecho ocupa lo que permita la ventana; si no cabe, se desplaza.
    void OnSizeChanged(object sender, SizeChangedEventArgs e) => SideScroll.MaxHeight = Math.Max(300, ActualHeight - 200);

    void OnShowFrontClick(object sender, RoutedEventArgs e) => _preview.ShowFront();

    void OnShowSpineClick(object sender, RoutedEventArgs e) => _preview.ShowSpine();

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
