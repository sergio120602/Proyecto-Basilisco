using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Basilisco.Carousel;
using Basilisco.Models;
using Basilisco.Services;
using Microsoft.Web.WebView2.Core;

namespace Basilisco.Customization;

/// <summary>
/// La tercera doble página de la caja abierta, a la derecha de la banda sonora: con Av Pág (o RB, o el botón) la hoja
/// pasa y aparecen las capturas de pantalla (a la izquierda, como fotos, con todas en pequeño debajo) y el tráiler (a la
/// derecha: su imagen con ▶, y al darle, el reproductor en su sitio). Salen de Steam y de LaunchBox; se buscan la
/// primera vez que se abre la página y se guardan en el juego.
/// </summary>
public partial class OpenCasePanel
{
    bool _mediaShown;
    int _shot;
    int _mediaVersion;
    CancellationTokenSource? _mediaSearch;
    Task? _trailerStarting;
    bool _trailerOn;
    int _trailerState;

    /// <summary>Se han encontrado las capturas y el tráiler de un juego (hay que guardar la biblioteca).</summary>
    public event Action<Game>? MediaFound;

    void OnMediaClick(object sender, RoutedEventArgs e) => ShowMedia();

    void OnBackToSoundtrackClick(object sender, RoutedEventArgs e) => LeaveMedia();

    /// <summary>Pasa la hoja de la banda sonora y enseña las capturas y el tráiler.</summary>
    void ShowMedia()
    {
        if (!_soundtrackShown || _mediaShown || _turning || _game is not { } game || _closing)
            return;
        ClosePicker();
        // El reproductor de YouTube de la música se vería por encima de la hoja al pasar: esa música se para.
        if (_youTube)
            StopMusic();
        FillMedia(game);
        PlaceSoundtrack();
        MediaView.Visibility = Visibility.Visible;
        Panel.SetZIndex(MediaView, 0);
        Panel.SetZIndex(SoundtrackView, 1);
        _turning = true;
        TurnPage(forward: true, () =>
        {
            _turning = false;
            _mediaShown = true;
            SoundtrackView.Visibility = Visibility.Collapsed;
            Panel.SetZIndex(MediaView, 1);
        });
        SoundtrackButtons.Visibility = Visibility.Collapsed;
        MediaButtons.Visibility = Visibility.Visible;
        HintText.Text = _padMode
            ? "← →  Captura     ·     A  Ver el tráiler     ·     LB o B  Banda sonora"
            : "← →  Captura     ·     Z  Verla en grande     ·     Enter  Ver el tráiler     ·     Re Pág o Esc  Banda sonora";
    }

    /// <summary>Vuelve a la banda sonora (la hoja regresa). Con <paramref name="immediate"/>, sin animación (al cerrar la caja).</summary>
    void LeaveMedia(bool immediate = false)
    {
        StopTrailer();
        ShotZoom.Visibility = Visibility.Collapsed;
        _mediaSearch?.Cancel();
        if (immediate || !_mediaShown)
        {
            _mediaShown = false;
            MediaView.Visibility = Visibility.Collapsed;
            PageTurnLayer.Visibility = Visibility.Collapsed;
            return;
        }
        if (_turning)
            return;
        _turning = true;
        SoundtrackView.Visibility = Visibility.Visible;
        PlaceSoundtrack();
        Panel.SetZIndex(SoundtrackView, 0);
        Panel.SetZIndex(MediaView, 1);
        TurnPage(forward: false, () =>
        {
            _turning = false;
            _mediaShown = false;
            MediaView.Visibility = Visibility.Collapsed;
            Panel.SetZIndex(SoundtrackView, 1);
        });
        MediaButtons.Visibility = Visibility.Collapsed;
        SoundtrackButtons.Visibility = Visibility.Visible;
        HintText.Text = _padMode
            ? "↑ ↓  Canción     ·     A  Reproducir / parar     ·     ← →  Retroceder / avanzar     ·     RB  Capturas y tráiler     ·     B  Volver al juego"
            : "↑ ↓  Canción     ·     Enter o clic en el disco  Reproducir / parar     ·     ← →  Retroceder / avanzar     ·     S  Buscar banda sonora     ·     Av Pág  Capturas y tráiler     ·     Esc  Volver al juego";
    }

    /// <summary>
    /// La hoja de en medio pasa como la de un libro: hacia delante, la mitad derecha de la banda sonora se dobla sobre el
    /// lomo y, ya del otro lado, es la mitad izquierda de las capturas; hacia atrás, al revés. Debajo están las dos páginas
    /// (la banda sonora a la izquierda y el tráiler a la derecha), en vivo.
    /// </summary>
    void TurnPage(bool forward, Action done)
    {
        double width = SoundtrackView.Width, height = SoundtrackView.Height, half = width / 2;
        if (double.IsNaN(width) || width < 100)
        {
            done();
            return;
        }
        SoundtrackView.UpdateLayout();
        MediaView.UpdateLayout();

        Brush Half(FrameworkElement view, bool right)
        {
            var offset = VisualTreeHelper.GetOffset(view);
            return new VisualBrush(view)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(offset.X + (right ? half : 0), offset.Y, half, height),
                Stretch = Stretch.Fill,
            };
        }

        PageTurnLayer.Margin = SoundtrackView.Margin;
        PageTurnLayer.Width = width;
        PageTurnLayer.Height = height;
        PageTurnLayer.Children.Clear();
        // Las páginas dejan ver lo de detrás (la otra página): mientras pasa la hoja, sobre el fondo oscuro de la caja.
        PageTurnLayer.Background = Paper;
        var under = new[] { (Half(SoundtrackView, false), 0.0), (Half(MediaView, true), half) };
        foreach (var (brush, x) in under)
        {
            var page = new Rectangle { Width = half, Height = height, Fill = brush };
            Canvas.SetLeft(page, x);
            PageTurnLayer.Children.Add(page);
        }

        // La hoja: la página y, encima, una sombra que se oscurece mientras está de canto.
        var scale = new ScaleTransform();
        var shade = new Rectangle
        {
            Width = half, Height = height, Opacity = 0,
            Fill = new LinearGradientBrush(Color.FromArgb(200, 0, 0, 0), Color.FromArgb(90, 0, 0, 0), 0),
        };
        var leafFace = new Rectangle { Width = half, Height = height };
        var leaf = new Grid { Width = half, Height = height, RenderTransform = scale, Background = Paper, Children = { leafFace, shade } };
        PageTurnLayer.Children.Add(leaf);
        Panel.SetZIndex(PageTurnLayer, 5);
        PageTurnLayer.Visibility = Visibility.Visible;

        // Primera mitad del giro: la hoja se levanta hasta quedar de canto sobre el lomo; segunda: baja del otro lado.
        void Side(bool right, Brush face)
        {
            leafFace.Fill = face;
            Canvas.SetLeft(leaf, right ? half : 0);
            scale.CenterX = right ? 0 : half;
        }
        var time = TimeSpan.FromMilliseconds(260);
        Side(right: forward, forward ? Half(SoundtrackView, true) : Half(MediaView, false));
        var lift = new DoubleAnimation(1, 0, time) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        lift.Completed += (_, _) =>
        {
            Side(right: !forward, forward ? Half(MediaView, false) : Half(SoundtrackView, true));
            var drop = new DoubleAnimation(0, 1, time) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            drop.Completed += (_, _) =>
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                PageTurnLayer.Visibility = Visibility.Collapsed;
                PageTurnLayer.Children.Clear();
                done();
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, drop);
            shade.BeginAnimation(OpacityProperty, new DoubleAnimation(0.6, 0, time));
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, lift);
        shade.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.6, time));
    }

    static readonly Brush Paper = new SolidColorBrush(Color.FromRgb(0x16, 0x18, 0x1D));

    /// <summary>Del mismo tamaño que la banda sonora; la foto y el tráiler se ajustan a su mitad.</summary>
    void PlaceMedia(Rect bounds)
    {
        MediaView.Margin = new Thickness(bounds.X, bounds.Y, 0, 0);
        MediaView.Width = bounds.Width;
        MediaView.Height = bounds.Height;
        var half = bounds.Width / 2;
        ShotImage.MaxWidth = Math.Max(120, half - 48 - 110);
        ShotImage.MaxHeight = Math.Max(80, bounds.Height - 40 - 110 - 60);
        var trailer = Math.Max(160, Math.Min(half - 60, (bounds.Height - 40 - 110) * 16 / 9));
        TrailerFrame.Width = trailer;
        TrailerFrame.Height = trailer * 9 / 16;
    }

    void FillMedia(Game game)
    {
        MediaBackground.ImageSource = SoundtrackBackground.ImageSource;
        MediaPlastic.Background = new SolidColorBrush(PlatformArt.Plastic(game.Platform));
        ShowMediaContent(game);
        if (!GameMediaFinder.NeedsLookup(game))
            return;

        _mediaSearch?.Cancel();
        var search = _mediaSearch = new CancellationTokenSource();
        if (game.Media is null || game.Media.IsEmpty)
        {
            ShotStatus.Text = "Buscando capturas y tráiler en Steam y LaunchBox…";
            ShotStatus.Visibility = Visibility.Visible;
            ShotFrame.Visibility = Visibility.Hidden;
        }
        _ = LookUpAsync(game, search.Token);
    }

    async Task LookUpAsync(Game game, CancellationToken cancellation)
    {
        var status = new Progress<string>(text =>
        {
            if (_game == game && ShotStatus.Visibility == Visibility.Visible)
                ShotStatus.Text = text;
        });
        GameMedia media;
        try
        {
            // Lo de Steam se ve ya (con Looked al mínimo: si se cierra antes de acabar, se vuelve a buscar la próxima vez).
            media = await GameMediaFinder.FindAsync(game, status, cancellation, steam => Dispatcher.Invoke(() =>
            {
                if (game.Media is not { IsEmpty: false })
                    game.Media = steam;
                if (_game == game && !cancellation.IsCancellationRequested)
                    ShowMediaContent(game);
            }));
        }
        catch (OperationCanceledException)
        {
            return;
        }
        // Si no se ha encontrado nada y ya había algo (sin red, por ejemplo), se queda lo de antes.
        if (media.IsEmpty && game.Media is { IsEmpty: false })
            game.Media.Looked = media.Looked;
        else
            game.Media = media;
        MediaFound?.Invoke(game);
        if (_game == game && !cancellation.IsCancellationRequested)
            ShowMediaContent(game);
    }

    /// <summary>Las capturas (la primera en grande y todas en pequeño) y el tráiler del juego, con lo que tenga ya guardado.</summary>
    void ShowMediaContent(Game game)
    {
        var version = ++_mediaVersion;
        var media = game.Media;
        var shots = media?.Screenshots ?? [];
        ShotsTitle.Text = shots.Count > 0 ? $"Capturas · {shots.Count}" : "Capturas";
        ShotThumbs.Children.Clear();
        ShotPrevious.Visibility = ShotNext.Visibility = shots.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (shots.Count == 0)
        {
            ShotFrame.Visibility = Visibility.Hidden;
            ShotStatus.Visibility = Visibility.Visible;
            ShotStatus.Text = media is null ? "Buscando capturas y tráiler en Steam y LaunchBox…" : "No hay capturas de este juego en Steam ni en LaunchBox.";
        }
        else
        {
            for (var index = 0; index < shots.Count; index++)
            {
                var number = index;
                var thumb = new Border
                {
                    Width = 104, Height = 60, Margin = new Thickness(3), Cursor = Cursors.Hand, Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1C, 0x22)),
                    BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Child = new Image { Stretch = Stretch.UniformToFill },
                };
                thumb.MouseLeftButtonUp += (_, _) => ShowShot(number);
                ShotThumbs.Children.Add(thumb);
            }
            _shot = Math.Clamp(_shot, 0, shots.Count - 1);
            ShowShot(_shot);
            _ = LoadThumbsAsync(game, shots, version);
        }

        // El tráiler: el de Steam, o el de YouTube que enlaza LaunchBox (si ya está sonando, se deja).
        if (_trailerOn)
            return;
        TrailerPoster.Source = null;
        TrailerExternal.Visibility = media?.YouTubeTrailer is not null && media.Trailer is null ? Visibility.Visible : Visibility.Collapsed;
        if (media?.Trailer is not null)
        {
            TrailerInfo.Text = media.TrailerName is { Length: > 0 } name ? $"{name} · Steam" : "Tráiler de Steam";
            TrailerPlayButton.Visibility = Visibility.Visible;
            _ = LoadPosterAsync(game, media.TrailerPoster, version);
        }
        else if (media?.YouTubeTrailer is { } video)
        {
            TrailerInfo.Text = "Tráiler en YouTube · de LaunchBox";
            TrailerPlayButton.Visibility = Visibility.Visible;
            _ = LoadPosterAsync(game, $"https://i.ytimg.com/vi/{video}/hqdefault.jpg", version);
        }
        else
        {
            TrailerInfo.Text = media is null ? "Buscando el tráiler…" : "No hay tráiler de este juego en Steam ni en LaunchBox.";
            TrailerPlayButton.Visibility = Visibility.Collapsed;
        }
        if (media?.Sources is { } sources)
            TrailerInfo.Text += $"\nCapturas de {sources}.";
    }

    async Task LoadThumbsAsync(Game game, List<string> shots, int version)
    {
        for (var index = 0; index < shots.Count; index++)
        {
            var path = await GameMediaFinder.ImagePathAsync(game, shots[index], CancellationToken.None);
            if (version != _mediaVersion)
                return;
            if (path is not null && index < ShotThumbs.Children.Count && ShotThumbs.Children[index] is Border { Child: Image image })
                image.Source = CoverArt.LoadImage(path, 220);
        }
    }

    async Task LoadPosterAsync(Game game, string? url, int version)
    {
        if (url is null)
            return;
        var path = await GameMediaFinder.ImagePathAsync(game, url, CancellationToken.None);
        if (version == _mediaVersion && path is not null)
            TrailerPoster.Source = CoverArt.LoadImage(path, 900);
    }

    /// <summary>Enseña esa captura en grande (la descarga si aún no estaba) y la marca abajo.</summary>
    async void ShowShot(int index)
    {
        if (_game is not { Media: { Screenshots.Count: > 0 } media } game)
            return;
        var shots = media.Screenshots;
        _shot = (index % shots.Count + shots.Count) % shots.Count;
        var version = _mediaVersion;
        var shot = _shot;
        ShotCounter.Text = $"{shot + 1} / {shots.Count}";
        for (var i = 0; i < ShotThumbs.Children.Count; i++)
        {
            if (ShotThumbs.Children[i] is Border thumb)
            {
                thumb.BorderBrush = i == shot ? Brushes.White : Brushes.Transparent;
                if (i == shot)
                    thumb.BringIntoView();
            }
        }
        var path = await GameMediaFinder.ImagePathAsync(game, shots[shot], CancellationToken.None);
        if (version != _mediaVersion || shot != _shot)
            return;
        if (path is null)
        {
            ShotFrame.Visibility = Visibility.Hidden;
            ShotStatus.Text = "No se ha podido descargar esta captura.";
            ShotStatus.Visibility = Visibility.Visible;
            return;
        }
        ShotImage.Source = CoverArt.LoadImage(path, 1920);
        ShotFrame.Visibility = Visibility.Visible;
        ShotStatus.Visibility = Visibility.Collapsed;
        if (ShotZoom.Visibility == Visibility.Visible)
            ShotZoomImage.Source = ShotImage.Source;
    }

    void OnShotPreviousClick(object sender, RoutedEventArgs e) => ShowShot(_shot - 1);

    void OnShotNextClick(object sender, RoutedEventArgs e) => ShowShot(_shot + 1);

    void OnShotClick(object sender, MouseButtonEventArgs e) => ZoomShot();

    void ZoomShot()
    {
        if (ShotImage.Source is null || ShotFrame.Visibility != Visibility.Visible)
            return;
        // El tráiler se vería por encima (es una ventana aparte): se para.
        StopTrailer();
        ShotZoomImage.Source = ShotImage.Source;
        ShotZoom.Visibility = Visibility.Visible;
    }

    void OnShotZoomClick(object sender, MouseButtonEventArgs e) => ShotZoom.Visibility = Visibility.Collapsed;

    // ---- Tráiler ----

    void OnTrailerPlayClick(object sender, MouseButtonEventArgs e) => PlayTrailer();

    /// <summary>Pone el tráiler en su marco (la música de la banda sonora se para mientras); si ya está puesto, lo para o lo sigue.</summary>
    async void PlayTrailer()
    {
        if (_game?.Media is not { } media || media.Trailer is null && media.YouTubeTrailer is null)
            return;
        if (_trailerOn)
        {
            await TrailerScriptAsync(_trailerState == 1 ? "window.bas && bas.pause()" : "window.bas && bas.play()");
            return;
        }
        try
        {
            await (_trailerStarting ??= StartTrailerCoreAsync());
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or WebView2RuntimeNotFoundException)
        {
            _trailerStarting = null;
            TrailerInfo.Text = "Para ver el tráiler hace falta el navegador Edge (WebView2) de Windows.";
            return;
        }
        if (!_mediaShown || TrailerPlayer.CoreWebView2 is not { } core)
            return;
        if (_musicPlaying)
            Pause();
        _trailerOn = true;
        _trailerState = 0;
        TrailerPlayButton.Visibility = Visibility.Collapsed;
        TrailerPlayer.Visibility = Visibility.Visible;
        core.Navigate(media.Trailer is { } trailer
            ? GameMediaFinder.TrailerUrl(trailer, VolumeSlider.Value)
            : YouTubeMusic.PlayerUrl(media.YouTubeTrailer!, VolumeSlider.Value));
    }

    async Task StartTrailerCoreAsync()
    {
        var environment = await WebViews.EnvironmentAsync();
        await TrailerPlayer.EnsureCoreWebView2Async(environment);
        var core = TrailerPlayer.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.SetVirtualHostNameToFolderMapping(YouTubeMusic.Host, GameMediaFinder.PlayerFolder(), CoreWebView2HostResourceAccessKind.Allow);
        core.WebMessageReceived += OnTrailerMessage;
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenInBrowser(e.Uri);
        };
    }

    /// <summary>Lo que cuenta el reproductor: suena (1), parado (2) o acabado (0); o que no se puede poner aquí.</summary>
    void OnTrailerMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var json = JsonDocument.Parse(e.TryGetWebMessageAsString());
            if (json.RootElement.TryGetProperty("error", out _))
            {
                StopTrailer();
                TrailerInfo.Text = _game?.Media?.Trailer is null
                    ? "Este vídeo de YouTube no se puede ver fuera de YouTube: ábrelo con «Ver en YouTube»."
                    : "No se ha podido poner el tráiler (¿sin conexión?).";
                return;
            }
            if (json.RootElement.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.Number)
            {
                _trailerState = state.GetInt32();
                if (_trailerState == 0)
                    StopTrailer();
            }
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
        }
    }

    async Task TrailerScriptAsync(string script)
    {
        try
        {
            if (TrailerPlayer.CoreWebView2 is { } core)
                await core.ExecuteScriptAsync(script);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
        }
    }

    /// <summary>Quita el reproductor y deja la imagen del tráiler con ▶.</summary>
    void StopTrailer()
    {
        if (!_trailerOn)
            return;
        _trailerOn = false;
        _trailerState = 0;
        TrailerPlayer.CoreWebView2?.Navigate("about:blank");
        TrailerPlayer.Visibility = Visibility.Collapsed;
        TrailerPlayButton.Visibility = Visibility.Visible;
    }

    void OnTrailerExternalClick(object sender, RoutedEventArgs e)
    {
        if (_game?.Media?.YouTubeTrailer is { } video)
            OpenInBrowser($"https://www.youtube.com/watch?v={video}");
    }

    /// <summary>Las teclas de esta página (las de la banda sonora no llegan mientras se ve).</summary>
    bool HandleMediaKey(Key key)
    {
        if (ShotZoom.Visibility == Visibility.Visible)
        {
            switch (key)
            {
                case Key.Left: ShowShot(_shot - 1); break;
                case Key.Right: ShowShot(_shot + 1); break;
                case Key.Escape or Key.Back or Key.Z or Key.Enter or Key.Space: ShotZoom.Visibility = Visibility.Collapsed; break;
                default: return false;
            }
            return true;
        }
        switch (key)
        {
            case Key.Left: ShowShot(_shot - 1); break;
            case Key.Right: ShowShot(_shot + 1); break;
            case Key.Z: ZoomShot(); break;
            case Key.Enter or Key.Space: PlayTrailer(); break;
            case Key.PageUp or Key.Escape or Key.Back: LeaveMedia(); break;
            default: return false;
        }
        return true;
    }
}
