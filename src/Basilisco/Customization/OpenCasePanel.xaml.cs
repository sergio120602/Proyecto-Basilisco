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
    static readonly TimeSpan CloseDelay = TimeSpan.FromSeconds(1.6);

    readonly OpenCaseScene _scene;
    Game? _game;
    bool _playing;
    bool _closing;

    /// <summary>Hay que lanzar el juego (el disco ya está girando).</summary>
    public event Action<Game>? PlayRequested;

    public event Action? Closed;

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
        _playing = false;
        _closing = false;
        TitleText.Text = game.Name;
        DetailText.Text = Platforms.Find(game.Platform)?.Name ?? "";
        PlayButton.IsEnabled = BackButton.IsEnabled = true;
        HintText.Text = padMode ? "A  Jugar     ·     B  Volver" : "Enter  Jugar     ·     Esc  Volver";

        var shape = BoxShape.Of(item);
        var plastic = PlatformArt.Plastic(game.Platform);
        var aspect = shape.Width / shape.Height;
        // Un disco de 12 cm en una caja de DVD (13,5 cm de ancho); en las cajas pequeñas, que quepa.
        var disc = Math.Min(shape.Width, shape.Height) * 0.88;
        _scene.Show(shape,
            CoverArt.RenderCover(item),
            OpenCaseArt.RenderInside(plastic, aspect, ImageStore.GetExtraPath(game, GameExtra.Manual), game.Name),
            OpenCaseArt.RenderTray(plastic, aspect, disc / shape.Width),
            OpenCaseArt.RenderDisc(ImageStore.GetExtraPath(game, GameExtra.Disc), item.ImagePath),
            disc);

        Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(() => Focus(), DispatcherPriority.Input);
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
        PlayRequested?.Invoke(game);

        await Task.Delay(CloseDelay);
        if (IsOpen && !_closing && _game == game)
            Close();
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

    void OnViewportClick(object sender, MouseButtonEventArgs e) => Play();

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
            default:
                return;
        }
        e.Handled = true;
    }
}
