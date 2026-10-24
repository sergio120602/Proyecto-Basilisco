using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Basilisco.Carousel;
using Basilisco.Models;
using Basilisco.Services;
using Microsoft.Web.WebView2.Core;

namespace Basilisco.Customization;

/// <summary>
/// La banda sonora en la caja abierta: con la flecha derecha la hoja de plástico del disco pasa como la de un libro y
/// aparece un papel con «Banda Sonora» y las canciones (a la izquierda) y el disco de la banda sonora (a la derecha).
/// Un clic en el disco hace sonar la canción elegida y lo pone a girar; otro clic la para. Las flechas izquierda y derecha
/// retroceden y avanzan en la canción, y debajo del disco están la barra de la canción y la del volumen.
/// Las canciones suenan desde tus archivos importados o, si no los tienes, desde el reproductor oficial de YouTube, que
/// se ve debajo del disco mientras suena (así lo piden las condiciones de YouTube; por eso se para al salir de aquí).
/// </summary>
public partial class OpenCasePanel
{
    readonly MediaPlayer _player = new();
    readonly DispatcherTimer _progress = new() { Interval = TimeSpan.FromMilliseconds(300) };
    bool _soundtrackShown, _turning, _seeking, _musicPlaying, _padMode;
    SoundtrackTrack? _current;
    bool _playerReady;

    // YouTube: si la canción que suena va por ahí, por dónde va y cuánto dura (se pregunta al reproductor).
    bool _youTube;
    double _ytTime, _ytDuration;
    Task? _ytStarting;
    SoundtrackTrack? _picking;

    /// <summary>Ha cambiado el vídeo de YouTube de alguna canción (hay que guardar la biblioteca).</summary>
    public event Action<Game>? SoundtrackEdited;

    /// <summary>Clave de la API de YouTube (para buscar sola el vídeo de cada canción); la guarda la ventana principal.</summary>
    public string? YouTubeKey { get; set; }

    public event Action<string?>? YouTubeKeyChanged;

    void ShowGameHint() => HintText.Text = _padMode
        ? "A  Jugar     ·     X  Manual     ·     →  Banda sonora     ·     B  Volver"
        : "Enter  Jugar     ·     M  Manual     ·     →  Banda sonora     ·     Esc  Volver";

    void EnsurePlayer()
    {
        if (_playerReady)
            return;
        _playerReady = true;
        _player.MediaEnded += (_, _) => PlayNext();
        _player.MediaFailed += (_, _) =>
        {
            NowPlaying.Text = "No se ha podido abrir este archivo de música";
            StopMusic();
        };
        _player.MediaOpened += (_, _) => UpdateProgress();
        CaseViewport.SizeChanged += (_, _) =>
        {
            if (_soundtrackShown || _turning)
                PlaceSoundtrack();
        };
        _progress.Tick += async (_, _) =>
        {
            if (_youTube)
                await ReadYouTubeAsync();
            UpdateProgress();
        };
    }

    void OnSoundtrackClick(object sender, RoutedEventArgs e) => ShowSoundtrack();

    void OnBackToGameClick(object sender, RoutedEventArgs e) => LeaveSoundtrack();

    /// <summary>Pasa la hoja del disco y enseña la banda sonora.</summary>
    void ShowSoundtrack()
    {
        if (_soundtrackShown || _turning || _game is not { } game || _closing)
            return;
        EnsurePlayer();
        _turning = true;
        FillSoundtrack(game);
        // La banda sonora ya está ahí, debajo de la hoja: al pasarla se descubre el librito, y por detrás de la hoja se
        // ve su otra mitad (el disco). Al acabar pasa delante, igual que se veía, para poder usarla.
        PlaceSoundtrack();
        SoundtrackView.BeginAnimation(OpacityProperty, null);
        SoundtrackView.Opacity = 1;
        Panel.SetZIndex(SoundtrackView, -1);
        SoundtrackView.Visibility = Visibility.Visible;
        SoundtrackView.UpdateLayout();
        _scene.SetTrayBack(SoundtrackLeftHalf());
        _scene.TurnTray(true, () =>
        {
            _turning = false;
            _soundtrackShown = true;
            PlaceSoundtrack();
            Panel.SetZIndex(SoundtrackView, 1);
            _scene.SetTrayBack(null);
        });
        GameButtons.Visibility = Visibility.Collapsed;
        SoundtrackButtons.Visibility = Visibility.Visible;
        HintText.Text = _padMode
            ? "↑ ↓  Canción     ·     A  Reproducir / parar     ·     ← →  Retroceder / avanzar     ·     B  Volver al juego"
            : "↑ ↓  Canción     ·     Enter o clic en el disco  Reproducir / parar     ·     ← →  Retroceder / avanzar     ·     S  Buscar banda sonora     ·     Esc  Volver al juego";
    }

    /// <summary>
    /// Vuelve al juego: la hoja del disco regresa a su sitio. Tu música sigue sonando hasta que cierres la caja; la de
    /// YouTube se para, porque su reproductor dejaría de verse.
    /// </summary>
    void LeaveSoundtrack(bool immediate = false)
    {
        ClosePicker();
        if (_youTube)
            StopMusic();
        GameButtons.Visibility = Visibility.Visible;
        SoundtrackButtons.Visibility = Visibility.Collapsed;
        ShowGameHint();
        if (immediate || !_soundtrackShown)
        {
            HideSoundtrackView();
            if (immediate)
            {
                _soundtrackShown = _turning = false;
                StopMusic();
            }
            return;
        }
        _soundtrackShown = false;
        _turning = true;
        // Al revés: la banda sonora vuelve debajo y la hoja la tapa al volver a su sitio (por detrás, el disco).
        Panel.SetZIndex(SoundtrackView, -1);
        SoundtrackView.UpdateLayout();
        _scene.SetTrayBack(SoundtrackLeftHalf());
        _scene.TurnTray(false, () =>
        {
            _turning = false;
            HideSoundtrackView();
        });
    }

    void HideSoundtrackView()
    {
        SoundtrackView.BeginAnimation(OpacityProperty, null);
        SoundtrackView.Opacity = 0;
        SoundtrackView.Visibility = Visibility.Collapsed;
        Panel.SetZIndex(SoundtrackView, 1);
        _scene.SetTrayBack(null);
    }

    /// <summary>
    /// La mitad izquierda de la banda sonora (el disco y los controles), en vivo, para la otra cara de la hoja. El pincel
    /// la dibuja con su desplazamiento dentro de la caja (el margen con el que se coloca), así que se recorta desde ahí.
    /// </summary>
    VisualBrush SoundtrackLeftHalf()
    {
        var offset = VisualTreeHelper.GetOffset(SoundtrackView);
        return new VisualBrush(SoundtrackView)
        {
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(offset.X, offset.Y, Math.Max(1, SoundtrackView.ActualWidth / 2), Math.Max(1, SoundtrackView.ActualHeight)),
            Stretch = Stretch.Fill,
        };
    }

    /// <summary>
    /// La banda sonora es otra doble página de la misma caja: se pone justo encima de la caja abierta, del mismo tamaño, y
    /// el disco y el librito se ajustan a ella (el disco, más pequeño cuando debajo se ve el reproductor de YouTube).
    /// </summary>
    void PlaceSoundtrack()
    {
        var bounds = _scene.OpenSpreadBounds();
        if (bounds.IsEmpty || bounds.Width < 100)
            return;
        SoundtrackView.Margin = new Thickness(bounds.X, bounds.Y, 0, 0);
        SoundtrackView.Width = bounds.Width;
        SoundtrackView.Height = bounds.Height;
        var half = bounds.Width / 2;
        var controls = Math.Min(380, half * 0.86);
        SoundtrackControls.Width = controls;
        var video = YouTubeHost.Visibility == Visibility.Visible ? YouTubeHost.Height + 12 : 0;
        var disc = Math.Max(140, Math.Min(half * 0.62, bounds.Height - 150 - video - 40));
        SoundtrackDiscHost.Width = SoundtrackDiscHost.Height = disc;
    }

    void FillSoundtrack(Game game)
    {
        var soundtrack = game.Soundtrack;
        // De fondo, la de la carátula: la imagen del interior de la caja (o, si no tiene, la portada), bajo el plástico.
        SoundtrackBackground.ImageSource = _inside ?? (_item?.ImagePath is { } cover ? CoverArt.LoadImage(cover, 1400) : null);
        SoundtrackPlastic.Background = new SolidColorBrush(PlatformArt.Plastic(game.Platform));
        SoundtrackTitle.Text = soundtrack is null ? game.Name : soundtrack.Artist is { Length: > 0 } artist ? $"{soundtrack.Title} · {artist}" : soundtrack.Title;
        SoundtrackDisc.Source = OpenCaseArt.RenderDisc(ImageStore.GetSoundtrackDiscPath(game), _item?.ImagePath, soundtrack?.DiscFrame);
        TrackList.Items.Clear();
        var withMusic = 0;
        var muted = new SolidColorBrush(Color.FromRgb(0x7A, 0x71, 0x66));
        foreach (var track in soundtrack?.Tracks ?? [])
        {
            var hasFile = Soundtracks.PathOf(game, track) is not null;
            withMusic += hasFile ? 1 : 0;
            var row = new DockPanel { Margin = new Thickness(2, 3, 6, 3) };
            var length = new TextBlock { Text = Soundtracks.Length(track.LengthMs), FontSize = 14, Foreground = muted, Margin = new Thickness(12, 0, 0, 0) };
            DockPanel.SetDock(length, Dock.Right);
            row.Children.Add(length);
            if (!hasFile && track.YouTubeId is not null)
            {
                var tag = new TextBlock { Text = "YouTube", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xC4, 0x30, 0x2B)), Margin = new Thickness(10, 3, 0, 0) };
                DockPanel.SetDock(tag, Dock.Right);
                row.Children.Add(tag);
            }
            row.Children.Add(new TextBlock { Text = $"{track.Number}.", Width = 34, FontSize = 15, Foreground = new SolidColorBrush(Color.FromRgb(0x8C, 0x82, 0x76)) });
            row.Children.Add(new TextBlock { Text = track.Title, FontSize = 15, FontFamily = new FontFamily("Georgia"), Foreground = new SolidColorBrush(Color.FromRgb(0x2A, 0x24, 0x20)), TextTrimming = TextTrimming.CharacterEllipsis });
            var item = new ListBoxItem
            {
                Content = row, Tag = track, Cursor = Cursors.Hand,
                Opacity = hasFile || track.YouTubeId is not null ? 1 : 0.62,
                ToolTip = hasFile ? "Doble clic para escucharla"
                    : track.YouTubeId is not null ? "Suena desde YouTube (clic derecho para elegir otro vídeo)"
                    : "Sin archivo: doble clic para escucharla desde YouTube",
                ContextMenu = TrackMenu(game, track),
            };
            TrackList.Items.Add(item);
        }
        SoundtrackSearchButton.Content = soundtrack is null ? "Buscar banda sonora" : "Buscar otra banda sonora";
        SoundtrackEmpty.Text = soundtrack is null
            ? "Este juego aún no tiene banda sonora. Búscala con el botón de arriba, o importa tu música en Personalizar (E) → Banda sonora."
            : withMusic < soundtrack.Tracks.Count
                ? $"{withMusic} de {soundtrack.Tracks.Count} con tu música; las demás suenan desde YouTube (el reproductor se ve debajo del disco)."
                : "";
        var selected = TrackList.Items.OfType<ListBoxItem>().FirstOrDefault(item => item.Tag == _current) ?? TrackList.Items.OfType<ListBoxItem>().FirstOrDefault();
        TrackList.SelectedItem = selected;
        NowPlaying.Text = _current is { } current && _musicPlaying ? current.Title : (selected?.Tag as SoundtrackTrack)?.Title ?? "";
        UpdateProgress();
    }

    CancellationTokenSource? _soundtrackSearch;

    /// <summary>
    /// «Buscar banda sonora»: busca el disco en MusicBrainz con el nombre del juego y deja elegir entre los que haya. La
    /// música y los vídeos que ya tenías se quedan en las canciones que se llaman igual.
    /// </summary>
    async void SearchSoundtrack()
    {
        if (_game is not { } game)
            return;
        _soundtrackSearch?.Cancel();
        var cancellation = (_soundtrackSearch = new CancellationTokenSource()).Token;
        SoundtrackSearchButton.IsEnabled = false;
        SoundtrackEmpty.Text = "Buscando la banda sonora en MusicBrainz…";
        try
        {
            var releases = await Soundtracks.SearchAsync(game.Name, cancellation);
            if (cancellation.IsCancellationRequested || _game != game)
                return;
            if (releases.Count == 0)
            {
                SoundtrackEmpty.Text = "No se ha encontrado en MusicBrainz. Prueba en Personalizar (E) → Banda sonora con otro nombre, o importa tu música: formará la lista.";
                return;
            }
            var menu = new ContextMenu { PlacementTarget = SoundtrackSearchButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var release in releases.Take(12))
            {
                var chosen = release;
                var item = new MenuItem { Header = $"{release.Title}   ·   {release.Detail}" };
                item.Click += async (_, _) =>
                {
                    SoundtrackEmpty.Text = "Leyendo las canciones…";
                    if (await Soundtracks.LoadAsync(chosen, cancellation) is not { } loaded || _game != game)
                    {
                        if (_game == game)
                            SoundtrackEmpty.Text = "No se han podido leer las canciones de ese disco. Prueba con otro.";
                        return;
                    }
                    if (_musicPlaying)
                        StopMusic();
                    Soundtracks.Apply(game, loaded.Soundtrack, loaded.CoverPath);
                    SoundtrackEdited?.Invoke(game);
                    FillSoundtrack(game);
                };
                menu.Items.Add(item);
            }
            menu.Closed += (_, _) => { if (_game == game && SoundtrackEmpty.Text.StartsWith("Elige")) FillSoundtrack(game); };
            menu.IsOpen = true;
            SoundtrackEmpty.Text = "Elige el disco de la lista.";
        }
        finally
        {
            SoundtrackSearchButton.IsEnabled = true;
        }
    }

    void OnSoundtrackSearchClick(object sender, RoutedEventArgs e) => SearchSoundtrack();

    ContextMenu TrackMenu(Game game, SoundtrackTrack track)
    {
        var menu = new ContextMenu();
        var choose = new MenuItem { Header = track.YouTubeId is null ? "Elegir vídeo de YouTube…" : "Elegir otro vídeo de YouTube…" };
        choose.Click += (_, _) => OpenPicker(track);
        menu.Items.Add(choose);
        if (track.YouTubeId is not null)
        {
            var remove = new MenuItem { Header = "Quitar vídeo de YouTube" };
            remove.Click += (_, _) =>
            {
                if (_current == track && _youTube)
                    StopMusic();
                track.YouTubeId = null;
                SoundtrackEdited?.Invoke(game);
                FillSoundtrack(game);
            };
            menu.Items.Add(remove);
        }
        return menu;
    }

    void SelectTrack(int direction)
    {
        var count = TrackList.Items.Count;
        if (count == 0)
            return;
        TrackList.SelectedIndex = Math.Clamp(TrackList.SelectedIndex + direction, 0, count - 1);
        TrackList.ScrollIntoView(TrackList.SelectedItem);
        if (!_musicPlaying && TrackList.SelectedItem is ListBoxItem { Tag: SoundtrackTrack track })
            NowPlaying.Text = track.Title;
    }

    void OnTrackDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TrackList.SelectedItem is ListBoxItem { Tag: SoundtrackTrack track })
            _ = PlayTrackAsync(track);
    }

    void OnSoundtrackDiscClick(object sender, MouseButtonEventArgs e) => ToggleMusic();

    /// <summary>El disco: si suena, se para; si no, suena la canción elegida (o sigue donde iba).</summary>
    void ToggleMusic()
    {
        var selected = (TrackList.SelectedItem as ListBoxItem)?.Tag as SoundtrackTrack;
        var same = selected is null || selected == _current;
        if (_current is not null && same)
        {
            if (_musicPlaying)
                Pause();
            else
                Resume();
            return;
        }
        if (selected is not null)
            _ = PlayTrackAsync(selected);
    }

    void Pause()
    {
        if (_youTube)
            _ = YouTubeScriptAsync("bas.pause()");
        else
            _player.Pause();
        SetPlaying(false);
    }

    void Resume()
    {
        if (_youTube)
            _ = YouTubeScriptAsync("bas.play()");
        else
            _player.Play();
        SetPlaying(true);
    }

    /// <summary>Suena la canción: desde tu archivo si lo tienes; si no, desde YouTube (eligiendo el vídeo si aún no tiene).</summary>
    async Task PlayTrackAsync(SoundtrackTrack track)
    {
        if (_game is not { } game)
            return;
        EnsurePlayer();
        if (Soundtracks.PathOf(game, track) is { } path)
        {
            StopYouTube();
            _current = track;
            _player.Open(new Uri(path));
            _player.Volume = VolumeSlider.Value;
            _player.Play();
            Playing(track);
            return;
        }

        if (track.YouTubeId is null && YouTubeKey is { Length: > 0 } key)
        {
            NowPlaying.Text = "Buscando la canción en YouTube…";
            if (await YouTubeMusic.SearchAsync($"{game.Name} {track.Title} soundtrack", key, CancellationToken.None) is { } found && _game == game)
            {
                track.YouTubeId = found;
                SoundtrackEdited?.Invoke(game);
                FillSoundtrack(game);
            }
        }
        if (track.YouTubeId is not { } video)
        {
            OpenPicker(track);
            return;
        }

        if (_playerReady)
        {
            _player.Stop();
            _player.Close();
        }
        _current = track;
        _youTube = true;
        _ytTime = _ytDuration = 0;
        YouTubeHost.Visibility = Visibility.Visible;
        PlaceSoundtrack();
        if (await StartYouTubeAsync() is false)
        {
            NowPlaying.Text = "No se ha podido abrir el reproductor de YouTube (navegador de Windows WebView2).";
            StopMusic();
            return;
        }
        YouTubePlayer.CoreWebView2.Navigate(YouTubeMusic.PlayerUrl(video, VolumeSlider.Value));
        Playing(track);
    }

    void Playing(SoundtrackTrack track)
    {
        NowPlaying.Text = track.Title;
        TrackList.SelectedItem = TrackList.Items.OfType<ListBoxItem>().FirstOrDefault(item => item.Tag == track);
        SetPlaying(true);
    }

    async Task<bool> StartYouTubeAsync()
    {
        try
        {
            await (_ytStarting ??= StartYouTubeCoreAsync());
            return YouTubePlayer.CoreWebView2 is not null;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or WebView2RuntimeNotFoundException)
        {
            _ytStarting = null;
            return false;
        }
    }

    async Task StartYouTubeCoreAsync()
    {
        var environment = await WebViews.EnvironmentAsync();
        await YouTubePlayer.EnsureCoreWebView2Async(environment);
        var core = YouTubePlayer.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.SetVirtualHostNameToFolderMapping(YouTubeMusic.Host, YouTubeMusic.PlayerFolder(), CoreWebView2HostResourceAccessKind.Allow);
        core.WebMessageReceived += OnYouTubeMessage;
        // Los enlaces del reproductor (el logotipo, «Ver en YouTube») se abren en tu navegador.
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenInBrowser(e.Uri);
        };
    }

    /// <summary>Lo que cuenta el reproductor: suena (1), parado (2), se ha acabado (0) o no se puede poner aquí.</summary>
    void OnYouTubeMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var json = JsonDocument.Parse(e.TryGetWebMessageAsString());
            if (json.RootElement.TryGetProperty("error", out _))
            {
                NowPlaying.Text = "Ese vídeo no se puede poner fuera de YouTube: clic derecho en la canción para elegir otro";
                SetPlaying(false);
                return;
            }
            switch (json.RootElement.GetProperty("state").GetInt32())
            {
                case 0: PlayNext(); break;
                case 1: if (!_musicPlaying) SetPlaying(true); break;
                case 2: if (_musicPlaying) SetPlaying(false); break;
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
        }
    }

    async Task<string?> YouTubeScriptAsync(string script)
    {
        if (!_youTube || YouTubePlayer.CoreWebView2 is null)
            return null;
        try
        {
            return await YouTubePlayer.CoreWebView2.ExecuteScriptAsync($"window.bas ? {script} : null");
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    async Task ReadYouTubeAsync()
    {
        if (await YouTubeScriptAsync("bas.info()") is not { } result || result == "null")
            return;
        try
        {
            // Llega como texto JSON dentro de otro texto JSON.
            using var outer = JsonDocument.Parse(result);
            using var json = JsonDocument.Parse(outer.RootElement.GetString() ?? "{}");
            _ytTime = json.RootElement.TryGetProperty("t", out var t) ? t.GetDouble() : 0;
            _ytDuration = json.RootElement.TryGetProperty("d", out var d) ? d.GetDouble() : 0;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
        }
    }

    /// <summary>Al acabar una canción, la siguiente que pueda sonar (con archivo o con vídeo de YouTube ya elegido).</summary>
    void PlayNext()
    {
        var tracks = _game?.Soundtrack?.Tracks ?? [];
        var index = _current is null ? -1 : tracks.IndexOf(_current);
        var next = tracks.Skip(index + 1).FirstOrDefault(track => _game is { } g && (Soundtracks.PathOf(g, track) is not null || track.YouTubeId is not null));
        if (next is not null)
            _ = PlayTrackAsync(next);
        else
            StopMusic();
    }

    void Seek(double seconds)
    {
        if (_current is null)
            return;
        if (_youTube)
        {
            _ = YouTubeScriptAsync($"bas.seek({seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
            _ytTime = Math.Clamp(_ytTime + seconds, 0, Math.Max(0, _ytDuration));
            UpdateProgress();
            return;
        }
        if (!_player.NaturalDuration.HasTimeSpan)
            return;
        var target = _player.Position + TimeSpan.FromSeconds(seconds);
        _player.Position = target < TimeSpan.Zero ? TimeSpan.Zero : target > _player.NaturalDuration.TimeSpan ? _player.NaturalDuration.TimeSpan : target;
        UpdateProgress();
    }

    void StopYouTube()
    {
        if (!_youTube)
            return;
        _youTube = false;
        YouTubePlayer.CoreWebView2?.Navigate("about:blank");
        YouTubeHost.Visibility = Visibility.Collapsed;
        PlaceSoundtrack();
    }

    void StopMusic()
    {
        StopYouTube();
        if (_playerReady)
        {
            _player.Stop();
            _player.Close();
        }
        _current = null;
        SetPlaying(false);
        UpdateProgress();
    }

    /// <summary>El disco gira mientras suena (y se queda donde estaba al parar).</summary>
    void SetPlaying(bool playing)
    {
        _musicPlaying = playing;
        var angle = SoundtrackSpin.Angle;
        SoundtrackSpin.BeginAnimation(RotateTransform.AngleProperty, null);
        SoundtrackSpin.Angle = angle % 360;
        if (playing)
        {
            SoundtrackSpin.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(angle % 360, angle % 360 + 360, TimeSpan.FromSeconds(1.8)) { RepeatBehavior = RepeatBehavior.Forever });
            _progress.Start();
        }
        else if (!_youTube)
        {
            _progress.Stop();
        }
    }

    void UpdateProgress()
    {
        if (_seeking)
            return;
        TimeSpan duration, position;
        if (_youTube)
        {
            duration = TimeSpan.FromSeconds(Math.Max(0, _ytDuration));
            position = TimeSpan.FromSeconds(Math.Max(0, _ytTime));
        }
        else
        {
            duration = _current is not null && _player.NaturalDuration.HasTimeSpan ? _player.NaturalDuration.TimeSpan : TimeSpan.Zero;
            position = _current is not null ? _player.Position : TimeSpan.Zero;
        }
        SeekSlider.Maximum = Math.Max(1, duration.TotalSeconds);
        SeekSlider.Value = Math.Min(SeekSlider.Maximum, position.TotalSeconds);
        ElapsedText.Text = Time(position);
        DurationText.Text = duration > TimeSpan.Zero ? Time(duration) : "";
        VolumeText.Text = $"{VolumeSlider.Value * 100:0} %";
    }

    static string Time(TimeSpan time) => $"{(int)time.TotalMinutes}:{time.Seconds:00}";

    void OnSeekDragStart(object sender, MouseButtonEventArgs e) => _seeking = true;

    void OnSeekDragEnd(object sender, MouseButtonEventArgs e)
    {
        _seeking = false;
        if (_current is not null)
        {
            if (_youTube)
            {
                _ytTime = SeekSlider.Value;
                _ = YouTubeScriptAsync($"bas.seekTo({SeekSlider.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
            }
            else
            {
                _player.Position = TimeSpan.FromSeconds(SeekSlider.Value);
            }
        }
        UpdateProgress();
    }

    void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_playerReady)
            _player.Volume = e.NewValue;
        _ = YouTubeScriptAsync($"bas.volume({(int)Math.Round(e.NewValue * 100)})");
        if (VolumeText is not null)
            VolumeText.Text = $"{e.NewValue * 100:0} %";
        VolumeChanged?.Invoke(e.NewValue);
    }

    // ---- Elegir el vídeo de YouTube de una canción ----

    /// <summary>Abre la búsqueda de YouTube para la canción; el vídeo que abras se queda para ella y empieza a sonar.</summary>
    async void OpenPicker(SoundtrackTrack track)
    {
        if (_game is not { } game)
            return;
        if (_youTube && _musicPlaying)
            Pause();
        _picking = track;
        PickerTitle.Text = $"YouTube · «{track.Title}»";
        YouTubeKeyBox.Text = YouTubeKey ?? "";
        // Un reproductor por encima de otro no se ve bien: el pequeño se esconde mientras se elige.
        YouTubeHost.Visibility = Visibility.Collapsed;
        YouTubePicker.Visibility = Visibility.Visible;
        try
        {
            var environment = await WebViews.EnvironmentAsync();
            if (YouTubeSearch.CoreWebView2 is null)
            {
                await YouTubeSearch.EnsureCoreWebView2Async(environment);
                YouTubeSearch.CoreWebView2.Settings.AreDevToolsEnabled = false;
                // YouTube cambia de página sin recargarla: se mira la dirección cada vez que cambia.
                YouTubeSearch.CoreWebView2.SourceChanged += (_, _) => OnPickerSourceChanged();
                YouTubeSearch.CoreWebView2.NewWindowRequested += (_, e) =>
                {
                    e.Handled = true;
                    YouTubeSearch.CoreWebView2.Navigate(e.Uri);
                };
            }
            var query = $"{game.Soundtrack?.Title ?? game.Name} {track.Title} soundtrack";
            YouTubeSearch.CoreWebView2.Navigate($"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or WebView2RuntimeNotFoundException)
        {
            ClosePicker();
            NowPlaying.Text = "No se ha podido abrir YouTube (navegador de Windows WebView2).";
        }
    }

    void OnPickerSourceChanged()
    {
        if (_picking is not { } track || _game is not { } game || YouTubeMusic.VideoIdOf(YouTubeSearch.CoreWebView2?.Source) is not { } video)
            return;
        track.YouTubeId = video;
        SoundtrackEdited?.Invoke(game);
        ClosePicker();
        FillSoundtrack(game);
        _ = PlayTrackAsync(track);
    }

    void ClosePicker()
    {
        if (YouTubePicker.Visibility != Visibility.Visible)
            return;
        _picking = null;
        YouTubeSearch.CoreWebView2?.Navigate("about:blank");
        YouTubePicker.Visibility = Visibility.Collapsed;
        if (_youTube)
            YouTubeHost.Visibility = Visibility.Visible;
    }

    void OnPickerCancelClick(object sender, RoutedEventArgs e) => ClosePicker();

    void OnSaveYouTubeKeyClick(object sender, RoutedEventArgs e)
    {
        var key = YouTubeKeyBox.Text.Trim();
        YouTubeKey = key.Length > 0 ? key : null;
        YouTubeKeyChanged?.Invoke(YouTubeKey);
        // Con la clave, la canción que se estaba eligiendo se busca sola.
        if (_picking is { } track && YouTubeKey is not null)
        {
            ClosePicker();
            _ = PlayTrackAsync(track);
        }
    }

    void OnYouTubeKeyHelpClick(object sender, RoutedEventArgs e) => OpenInBrowser(YouTubeMusic.KeyUrl);

    static void OpenInBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }
}
