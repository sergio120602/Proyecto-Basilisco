using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Basilisco.Carousel;
using Basilisco.Models;
using Basilisco.Services;

namespace Basilisco.Customization;

/// <summary>
/// Al darle a un juego se abre su caja: la tapa gira y deja ver el manual a la izquierda y el disco a la derecha.
/// Al darle a Jugar el disco gira y, cuando coge velocidad, empieza el juego; después la caja se cierra sola.
/// </summary>
public partial class OpenCasePanel : UserControl
{
    // Lo que gira el disco antes de lanzar el juego, y lo que sigue girando después antes de cerrarse.
    static readonly TimeSpan LaunchDelay = TimeSpan.FromSeconds(1.3);

    readonly OpenCaseScene _scene;
    Game? _game;
    CarouselItem? _item;
    System.Windows.Media.Imaging.BitmapSource? _inside;
    bool _playing;
    bool _closing;

    /// <summary>Hay que lanzar el juego (el disco ya está girando).</summary>
    public event Action<Game>? PlayRequested;

    public event Action? Closed;

    /// <summary>Hay que abrir (o crear) el manual en PDF del juego.</summary>
    public event Action<Game>? ManualRequested;

    public Game? Game => _game;

    public bool IsOpen => Visibility == Visibility.Visible;

    public OpenCasePanel()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
        _scene = new OpenCaseScene(CaseViewport);
    }

    /// <param name="item">La caja tal como se ve en el carrusel, para la portada de fuera.</param>
    /// <param name="padMode">True si se está usando el mando (cambian las teclas de la pista de abajo).</param>
    public void Open(Game game, CarouselItem item, bool padMode)
    {
        _game = game;
        _item = item;
        _playing = false;
        _closing = false;
        TitleText.Text = game.Name;
        DetailText.Text = Platforms.Find(game.Platform)?.Name ?? "";
        PlayButton.IsEnabled = BackButton.IsEnabled = true;
        HintText.Text = padMode ? "A  Jugar     ·     X  Manual     ·     B  Volver" : "Enter  Jugar     ·     M  Manual     ·     Esc  Volver";
        ManualButton.Content = game.ManualPdf is null ? "Crear el manual" : "Leer el manual";
        ManualButton.IsEnabled = true;

        var shape = BoxShape.Of(item);
        var plastic = PlatformArt.Plastic(game.Platform);
        var aspect = shape.Width / shape.Height;
        // Un disco de 12 cm en una caja de DVD (13,5 cm de ancho), o el cartucho de las consolas que lo usan.
        var content = CartridgeArt.ContentFor(game.Platform, shape, ImageStore.GetExtraPath(game, GameExtra.Disc), item.ImagePath);
        // El plástico de dentro es translúcido: deja ver la imagen del interior, si el juego tiene.
        _inside = ImageStore.GetExtraPath(game, GameExtra.Inside) is { } insidePath ? CoverArt.LoadImage(insidePath, 2400) : null;
        _scene.Show(shape,
            CoverArt.RenderCover(item),
            OpenCaseArt.RenderInside(plastic, aspect, ImageStore.GetExtraPath(game, GameExtra.Manual), game.Name, _inside, game.ManualFrame, game.InsideFrame),
            OpenCaseArt.RenderTray(plastic, aspect, content.Size.Width / shape.Width, _inside, game.InsideFrame, OpenCaseArt.SlotOf(content, shape)),
            content);

        Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(() => Focus(), DispatcherPriority.Input);
    }

    /// <summary>Vuelve a dibujar el interior de la tapa (al crear el manual, su portada pasa a ser el librito).</summary>
    public void RefreshManual()
    {
        if (_game is not { } game || _item is not { } item)
            return;
        var shape = BoxShape.Of(item);
        _scene.SetInside(OpenCaseArt.RenderInside(PlatformArt.Plastic(game.Platform), shape.Width / shape.Height,
            ImageStore.GetExtraPath(game, GameExtra.Manual), game.Name, _inside, game.ManualFrame, game.InsideFrame));
        ManualButton.Content = game.ManualPdf is null ? "Crear el manual" : "Leer el manual";
        ManualButton.IsEnabled = true;
    }

    /// <summary>Mientras se crea el manual, el botón lo dice.</summary>
    public void SetManualBusy(string text)
    {
        ManualButton.Content = text;
        ManualButton.IsEnabled = false;
    }

    public void RequestManual()
    {
        if (_game is { } game && !_closing && !_playing && ManualButton.IsEnabled)
            ManualRequested?.Invoke(game);
    }

    async void Play()
    {
        if (_playing || _game is not { } game)
            return;
        _playing = true;
        PlayButton.IsEnabled = false;
        _scene.Spin();

        await Task.Delay(LaunchDelay);
        // Si mientras tanto se ha cerrado la caja, el juego no se lanza.
        if (!IsOpen || _closing || _game != game)
            return;
        // El disco sigue girando mientras el juego esté abierto: al cerrarlo, la ventana principal llama a GameEnded.
        PlayRequested?.Invoke(game);
    }

    /// <summary>True mientras el juego lanzado desde la caja sigue abierto (el disco girando).</summary>
    public bool IsPlaying => IsOpen && _playing && !_closing;

    /// <summary>Mientras juegas, la caja no se ve: su animación se para y sigue al volver.</summary>
    public void SetPaused(bool paused) => _scene.Paused = paused;

    /// <summary>El juego se ha cerrado (o no ha llegado a arrancar): el disco frena poco a poco y la tapa se cierra.</summary>
    public void GameEnded()
    {
        if (!IsPlaying)
            return;
        _scene.Paused = false;
        _scene.StopSpin(() =>
        {
            if (IsOpen && !_closing)
                Close();
        });
    }

    public void Close()
    {
        if (!IsOpen || _closing)
            return;
        _closing = true;
        BackButton.IsEnabled = PlayButton.IsEnabled = false;
        _scene.Close(() =>
        {
            Visibility = Visibility.Collapsed;
            _scene.Clear();
            _game = null;
            Closed?.Invoke();
        });
    }

    void OnPlayClick(object sender, RoutedEventArgs e) => Play();

    void OnBackClick(object sender, RoutedEventArgs e) => Close();

    // Clic en la caja: a la izquierda está el manual; a la derecha, el disco.
    void OnViewportClick(object sender, MouseButtonEventArgs e)
    {
        if (e.GetPosition(CaseViewport).X < CaseViewport.ActualWidth / 2)
            RequestManual();
        else
            Play();
    }

    void OnManualClick(object sender, RoutedEventArgs e) => RequestManual();

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter or Key.Space:
                Play();
                break;
            case Key.Escape or Key.Back:
                Close();
                break;
            case Key.M:
                RequestManual();
                break;
            default:
                return;
        }
        e.Handled = true;
    }
}
