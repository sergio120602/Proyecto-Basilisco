using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Basilisco.Carousel;
using Basilisco.Models;
using Basilisco.Services;
using Microsoft.Web.WebView2.Core;

namespace Basilisco.Customization;

/// <summary>
/// La tercera doble página de la caja abierta, a la derecha de la banda sonora: con Av Pág (o RB, o el botón) la hoja
/// pasa y aparecen las capturas de pantalla (a la izquierda) y los vídeos (a la derecha), cada uno con un desplegable de
/// sus orígenes (Steam, LaunchBox, las tuyas de Steam y tus carpetas) y las de ese origen en carrusel. Las de Steam y
/// LaunchBox se buscan la primera vez que se abre la página y se guardan en el juego; las tuyas se leen del disco.
/// </summary>
public partial class OpenCasePanel
{
    bool _mediaShown;
    int _mediaVersion;
    CancellationTokenSource? _mediaSearch;
    Task? _trailerStarting;
    bool _trailerOn;
    int _trailerState;
    List<MediaGroup> _shotGroups = [], _videoGroups = [];
    int _shotGroup, _videoGroup;
    // Si has elegido tú el grupo (en el desplegable, con G o H, o al añadir una carpeta), se respeta al rehacer la lista;
    // si no, se ve el primero (Steam, LaunchBox, los tuyos de Steam y tus carpetas, en ese orden).
    bool _shotChosen, _videoChosen;
    bool _mediaWired;

    // Carpetas del disco que ve el reproductor (cada una con su dirección propia, para poner tus vídeos y las grabaciones).
    readonly Dictionary<string, string> _videoHosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Se han encontrado las capturas y los tráileres de un juego, o has añadido o quitado una carpeta (hay que guardar).</summary>
    public event Action<Game>? MediaFound;

    void OnMediaClick(object sender, RoutedEventArgs e) => ShowMedia();

    void OnBackToSoundtrackClick(object sender, RoutedEventArgs e) => LeaveMedia();

    /// <summary>Pasa la hoja de la banda sonora y enseña las capturas y los vídeos.</summary>
    void ShowMedia()
    {
        if (!_soundtrackShown || _mediaShown || _turning || _game is not { } game || _closing)
            return;
        ClosePicker();
        // El reproductor de YouTube de la música se vería por encima de la hoja al pasar: esa música se para.
        if (_youTube)
            StopMusic();
        WireMedia();
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
            ? "← →  Captura     ·     ↑ ↓  Vídeo     ·     A  Poner el vídeo     ·     LT / RT  Otras capturas / otros vídeos     ·     LB o B  Banda sonora"
            : "← →  Captura     ·     ↑ ↓  Vídeo     ·     Z  Captura en grande     ·     Enter  Poner el vídeo     ·     G / H  Otras capturas / otros vídeos     ·     Re Pág o Esc  Banda sonora";
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
            ? "↑ ↓  Canción     ·     A  Reproducir / parar     ·     ← →  Retroceder / avanzar     ·     RB  Capturas y vídeos     ·     B  Volver al juego"
            : "↑ ↓  Canción     ·     Enter o clic en el disco  Reproducir / parar     ·     ← →  Retroceder / avanzar     ·     S  Buscar banda sonora     ·     Av Pág  Capturas y vídeos     ·     Esc  Volver al juego";
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

    /// <summary>Del mismo tamaño que la banda sonora.</summary>
    void PlaceMedia(Rect bounds)
    {
        MediaView.Margin = new Thickness(bounds.X, bounds.Y, 0, 0);
        MediaView.Width = bounds.Width;
        MediaView.Height = bounds.Height;
        if (_trailerOn)
            Dispatcher.BeginInvoke(PlacePlayer, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Los carruseles cargan sus imágenes con lo de aquí (una sola vez).</summary>
    void WireMedia()
    {
        if (_mediaWired)
            return;
        _mediaWired = true;
        ShotCarousel.LoadPoster = item => LoadImageAsync(item.Poster, item.Kind == MediaKind.LocalImage, 1600);
        VideoCarousel.LoadPoster = async item => item.Kind == MediaKind.LocalVideo
            ? await LoadImageAsync(await GameMediaLibrary.VideoThumbnailAsync(item.Source), true, 900)
            : await LoadImageAsync(item.Poster, item.Kind == MediaKind.SteamClip, 900);
        ShotCarousel.Activated += _ => ZoomShot();
        VideoCarousel.Activated += item => PlayVideo(item);
        VideoCarousel.SelectionChanged += _ =>
        {
            StopTrailer();
            ShowVideoInfo();
        };
        ShotCarousel.SelectionChanged += _ =>
        {
            if (ShotZoom.Visibility == Visibility.Visible)
                ZoomShot();
        };
    }

    async Task<ImageSource?> LoadImageAsync(string? source, bool local, int width)
    {
        if (source is null || _game is not { } game)
            return null;
        var path = local ? source : await GameMediaFinder.ImagePathAsync(game, source, CancellationToken.None);
        return path is not null && File.Exists(path) ? CoverArt.LoadImage(path, width) : null;
    }

    void FillMedia(Game game)
    {
        MediaBackground.ImageSource = SoundtrackBackground.ImageSource;
        MediaPlastic.Background = new SolidColorBrush(PlatformArt.Plastic(game.Platform));
        _shotGroup = _videoGroup = -1;
        _shotChosen = _videoChosen = false;
        ShowMediaContent(game);
        if (!GameMediaFinder.NeedsLookup(game))
            return;

        _mediaSearch?.Cancel();
        var search = _mediaSearch = new CancellationTokenSource();
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
                if (game.Media is not { IsEmpty: false } || game.Media.Version < GameMedia.CurrentVersion)
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
        if (media.IsEmpty && game.Media is { IsEmpty: false, Version: GameMedia.CurrentVersion })
            game.Media.Looked = media.Looked;
        else
            game.Media = media;
        MediaFound?.Invoke(game);
        if (_game == game && !cancellation.IsCancellationRequested)
            ShowMediaContent(game);
    }

    /// <summary>
    /// Rehace los grupos de los dos desplegables y enseña el elegido de cada uno (el mismo de antes si sigue, o el primero
    /// que tenga algo).
    /// </summary>
    void ShowMediaContent(Game game)
    {
        _mediaVersion++;
        var searching = GameMediaFinder.NeedsLookup(game);
        var shotName = _shotChosen && _shotGroup >= 0 && _shotGroup < _shotGroups.Count ? _shotGroups[_shotGroup].Name : null;
        var videoName = _videoChosen && _videoGroup >= 0 && _videoGroup < _videoGroups.Count ? _videoGroups[_videoGroup].Name : null;
        _shotGroups = GameMediaLibrary.ScreenshotGroups(game);
        _videoGroups = GameMediaLibrary.VideoGroups(game);
        SelectShotGroup(Math.Max(0, _shotGroups.FindIndex(group => group.Name == shotName)), searching);
        if (!_trailerOn)
            SelectVideoGroup(Math.Max(0, _videoGroups.FindIndex(group => group.Name == videoName)), searching);
    }

    void SelectShotGroup(int index, bool searching = false)
    {
        _shotGroup = _shotGroups.Count == 0 ? -1 : Math.Clamp(index, 0, _shotGroups.Count - 1);
        var group = _shotGroup >= 0 ? _shotGroups[_shotGroup] : null;
        ShotGroupButton.Content = group is null ? "Ninguna  ▾" : $"{group.Name} · {group.Items.Count}  ▾";
        ShotCarousel.SetItems(group?.Items ?? []);
        ShotStatus.Visibility = group is { Items.Count: > 0 } ? Visibility.Collapsed : Visibility.Visible;
        ShotStatus.Text = searching ? "Buscando capturas en Steam y LaunchBox…"
            : group is null ? "No hay capturas de este juego en Steam ni en LaunchBox. Añade una carpeta con las tuyas en el desplegable de arriba."
            : "Esta carpeta no tiene imágenes.";
    }

    void SelectVideoGroup(int index, bool searching = false)
    {
        StopTrailer();
        _videoGroup = _videoGroups.Count == 0 ? -1 : Math.Clamp(index, 0, _videoGroups.Count - 1);
        var group = _videoGroup >= 0 ? _videoGroups[_videoGroup] : null;
        VideoGroupButton.Content = group is null ? "Ninguno  ▾" : $"{group.Name} · {group.Items.Count}  ▾";
        VideoCarousel.SetItems(group?.Items ?? []);
        VideoStatus.Visibility = group is { Items.Count: > 0 } ? Visibility.Collapsed : Visibility.Visible;
        VideoStatus.Text = searching ? "Buscando tráilers en Steam y LaunchBox…"
            : group is null ? "No hay tráilers de este juego en Steam ni en LaunchBox. Añade una carpeta con tus vídeos en el desplegable de arriba."
            : "Esta carpeta no tiene vídeos (.mp4, .webm, .mov, .m4v o .mkv).";
        ShowVideoInfo();
    }

    void ShowVideoInfo()
    {
        var item = VideoCarousel.SelectedItem;
        TrailerExternal.Visibility = item?.Kind is MediaKind.YouTube or MediaKind.SteamClip or MediaKind.LocalVideo ? Visibility.Visible : Visibility.Collapsed;
        TrailerExternal.Content = item?.Kind == MediaKind.YouTube ? "Ver en YouTube" : "Abrir la carpeta";
        TrailerInfo.Text = item is null ? "" : item.Kind switch
        {
            MediaKind.Stream => "Tráiler de Steam. Enter o clic para verlo aquí.",
            MediaKind.YouTube => "Tráiler de YouTube que enlaza LaunchBox. Enter o clic para verlo aquí.",
            MediaKind.SteamClip => "Grabación de Steam. Enter o clic para verla aquí.",
            _ => "Vídeo de tu carpeta. Enter o clic para verlo aquí.",
        };
    }

    // ---- Los desplegables ----

    void OnShotGroupClick(object sender, RoutedEventArgs e) => OpenGroupMenu(ShotGroupButton, screenshots: true);

    void OnVideoGroupClick(object sender, RoutedEventArgs e) => OpenGroupMenu(VideoGroupButton, screenshots: false);

    /// <summary>
    /// El desplegable: cada origen con cuántas tiene (el que se ve, marcado), y debajo añadir una carpeta tuya o quitar la
    /// que se está viendo.
    /// </summary>
    void OpenGroupMenu(Button button, bool screenshots)
    {
        if (_game is not { } game)
            return;
        var groups = screenshots ? _shotGroups : _videoGroups;
        var current = screenshots ? _shotGroup : _videoGroup;
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        for (var index = 0; index < groups.Count; index++)
        {
            var number = index;
            var item = new MenuItem { Header = $"{groups[index].Name}  ({groups[index].Items.Count})", IsCheckable = true, IsChecked = index == current };
            item.Click += (_, _) => ChooseGroup(screenshots, number);
            menu.Items.Add(item);
        }
        if (groups.Count > 0)
            menu.Items.Add(new Separator());
        var add = new MenuItem { Header = screenshots ? "Añadir una carpeta con tus capturas…" : "Añadir una carpeta con tus vídeos…" };
        add.Click += (_, _) => AddMediaFolder(game, screenshots);
        menu.Items.Add(add);
        if (current >= 0 && current < groups.Count && groups[current].Folder is { } folder)
        {
            var remove = new MenuItem { Header = "Quitar esta carpeta (los archivos no se borran)" };
            remove.Click += (_, _) => RemoveMediaFolder(game, folder, screenshots);
            menu.Items.Add(remove);
        }
        menu.IsOpen = true;
    }

    void AddMediaFolder(Game game, bool screenshots)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = screenshots ? $"Carpeta con capturas de {game.Name}" : $"Carpeta con vídeos de {game.Name}",
            Multiselect = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        var folders = screenshots ? game.ScreenshotFolders ??= [] : game.VideoFolders ??= [];
        foreach (var folder in dialog.FolderNames.Where(folder => !folders.Contains(folder, StringComparer.OrdinalIgnoreCase)))
            folders.Add(folder);
        MediaFound?.Invoke(game);
        ShowMediaContent(game);
        // Se ve la carpeta recién añadida.
        var name = dialog.FolderNames.LastOrDefault();
        var groups = screenshots ? _shotGroups : _videoGroups;
        var index = groups.FindIndex(group => string.Equals(group.Folder, name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            ChooseGroup(screenshots, index);
    }

    /// <summary>Elegir tú un grupo: se ve y se queda aunque llegue algo nuevo de Steam o LaunchBox.</summary>
    void ChooseGroup(bool screenshots, int index)
    {
        if (screenshots)
        {
            _shotChosen = true;
            SelectShotGroup(index);
        }
        else
        {
            _videoChosen = true;
            SelectVideoGroup(index);
        }
    }

    void RemoveMediaFolder(Game game, string folder, bool screenshots)
    {
        (screenshots ? game.ScreenshotFolders : game.VideoFolders)?.RemoveAll(path => string.Equals(path, folder, StringComparison.OrdinalIgnoreCase));
        MediaFound?.Invoke(game);
        if (screenshots)
            _shotChosen = false;
        else
            _videoChosen = false;
        ShowMediaContent(game);
    }

    /// <summary>Pasa al siguiente origen del desplegable (G para las capturas y H para los vídeos; LT y RT en el mando).</summary>
    void NextGroup(bool screenshots)
    {
        var groups = screenshots ? _shotGroups : _videoGroups;
        if (groups.Count >= 2)
            ChooseGroup(screenshots, ((screenshots ? _shotGroup : _videoGroup) + 1) % groups.Count);
    }

    // ---- Capturas en grande ----

    async void ZoomShot()
    {
        if (ShotCarousel.SelectedItem is not { } item)
            return;
        // El vídeo se vería por encima (es una ventana aparte): se para.
        StopTrailer();
        ShotZoom.Visibility = Visibility.Visible;
        var image = await LoadImageAsync(item.Poster, item.Kind == MediaKind.LocalImage, 2560);
        if (ShotCarousel.SelectedItem == item)
            ShotZoomImage.Source = image;
    }

    void OnShotZoomClick(object sender, MouseButtonEventArgs e) => ShotZoom.Visibility = Visibility.Collapsed;

    // ---- Vídeos ----

    /// <summary>
    /// Pone el vídeo encima del del centro del carrusel (la música de la banda sonora se para mientras); si ya está puesto,
    /// lo para o lo sigue.
    /// </summary>
    async void PlayVideo(MediaItem? item)
    {
        if (item is null || !item.IsVideo)
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
            TrailerInfo.Text = "Para ver los vídeos hace falta el navegador Edge (WebView2) de Windows.";
            return;
        }
        if (!_mediaShown || TrailerPlayer.CoreWebView2 is not { } core || VideoCarousel.SelectedItem != item)
            return;
        var url = item.Kind switch
        {
            MediaKind.YouTube => YouTubeMusic.PlayerUrl(item.Source, VolumeSlider.Value),
            MediaKind.Stream => GameMediaFinder.TrailerUrl(item.Source, VolumeSlider.Value),
            MediaKind.SteamClip => GameMediaFinder.TrailerUrl($"https://{HostFor(core, item.Source)}/session.mpd", VolumeSlider.Value, item.Codec),
            _ => GameMediaFinder.TrailerUrl($"https://{HostFor(core, System.IO.Path.GetDirectoryName(item.Source)!)}/{Uri.EscapeDataString(System.IO.Path.GetFileName(item.Source))}", VolumeSlider.Value),
        };
        if (_musicPlaying)
            Pause();
        _trailerOn = true;
        _trailerState = 0;
        PlacePlayer();
        TrailerFrame.Visibility = Visibility.Visible;
        core.Navigate(url);
    }

    /// <summary>La dirección con la que el reproductor ve esa carpeta del disco (una por carpeta, solo para leer).</summary>
    string HostFor(CoreWebView2 core, string folder)
    {
        if (!_videoHosts.TryGetValue(folder, out var host))
        {
            host = $"media{_videoHosts.Count + 1}.basilisco";
            core.SetVirtualHostNameToFolderMapping(host, folder, CoreWebView2HostResourceAccessKind.Allow);
            _videoHosts[folder] = host;
        }
        return host;
    }

    /// <summary>El reproductor, justo encima del vídeo del centro del carrusel.</summary>
    void PlacePlayer()
    {
        var rect = VideoCarousel.CenterRect;
        TrailerFrame.Margin = new Thickness(rect.X, rect.Y, 0, 0);
        TrailerFrame.Width = Math.Max(1, rect.Width);
        TrailerFrame.Height = Math.Max(1, rect.Height);
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
            if (json.RootElement.TryGetProperty("error", out var error))
            {
                var item = VideoCarousel.SelectedItem;
                StopTrailer();
                TrailerInfo.Text = item?.Kind switch
                {
                    MediaKind.YouTube => "Este vídeo de YouTube no se puede ver fuera de YouTube: ábrelo con «Ver en YouTube».",
                    MediaKind.SteamClip when error.GetString() == "codec" =>
                        $"Esta grabación está en {(item.Codec?.StartsWith("hev", StringComparison.Ordinal) == true || item.Codec?.StartsWith("hvc", StringComparison.Ordinal) == true ? "HEVC (H.265)" : item.Codec)}, " +
                        "que este equipo no sabe poner. Instala las «Extensiones de vídeo HEVC» de Microsoft Store, o mírala en Steam.",
                    MediaKind.LocalVideo => "No se ha podido poner este vídeo (su formato no se puede ver aquí; prueba con .mp4 o .webm).",
                    _ => "No se ha podido poner el vídeo (¿sin conexión?).",
                };
                return;
            }
            if (json.RootElement.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.Number)
            {
                _trailerState = state.GetInt32();
                if (_trailerState == 0)
                    StopTrailer();
            }
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
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

    /// <summary>Quita el reproductor y deja el carrusel.</summary>
    void StopTrailer()
    {
        if (!_trailerOn)
            return;
        _trailerOn = false;
        _trailerState = 0;
        TrailerPlayer.CoreWebView2?.Navigate("about:blank");
        TrailerFrame.Visibility = Visibility.Collapsed;
    }

    /// <summary>«Ver en YouTube» o, con un vídeo tuyo o una grabación, abrir su carpeta.</summary>
    void OnTrailerExternalClick(object sender, RoutedEventArgs e)
    {
        switch (VideoCarousel.SelectedItem)
        {
            case { Kind: MediaKind.YouTube } item:
                OpenInBrowser($"https://www.youtube.com/watch?v={item.Source}");
                break;
            case { Kind: MediaKind.LocalVideo } item:
                OpenInBrowser(System.IO.Path.GetDirectoryName(item.Source)!);
                break;
            case { Kind: MediaKind.SteamClip } item:
                OpenInBrowser(item.Source);
                break;
        }
    }

    /// <summary>Las teclas de esta página (las de la banda sonora no llegan mientras se ve).</summary>
    bool HandleMediaKey(Key key)
    {
        if (ShotZoom.Visibility == Visibility.Visible)
        {
            switch (key)
            {
                case Key.Left: ShotCarousel.Move(-1); break;
                case Key.Right: ShotCarousel.Move(1); break;
                case Key.Escape or Key.Back or Key.Z or Key.Enter or Key.Space: ShotZoom.Visibility = Visibility.Collapsed; break;
                default: return false;
            }
            return true;
        }
        switch (key)
        {
            case Key.Left: ShotCarousel.Move(-1); break;
            case Key.Right: ShotCarousel.Move(1); break;
            case Key.Up: VideoCarousel.Move(-1); break;
            case Key.Down: VideoCarousel.Move(1); break;
            case Key.Z: ZoomShot(); break;
            case Key.G: NextGroup(screenshots: true); break;
            case Key.H: NextGroup(screenshots: false); break;
            case Key.Enter or Key.Space: PlayVideo(VideoCarousel.SelectedItem); break;
            case Key.PageUp or Key.Escape or Key.Back: LeaveMedia(); break;
            default: return false;
        }
        return true;
    }
}
