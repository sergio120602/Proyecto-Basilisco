using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Basilisco.Carousel;
using Basilisco.Models;
using Basilisco.Services;

namespace Basilisco.Customization;

/// <summary>
/// La banda sonora en la caja abierta: con la flecha derecha la hoja de plástico del disco pasa como la de un libro y
/// aparece un papel con «Banda Sonora» y las canciones (a la izquierda) y el disco de la banda sonora (a la derecha).
/// Un clic en el disco hace sonar la canción elegida y lo pone a girar; otro clic la para. Las flechas izquierda y derecha
/// retroceden y avanzan en la canción, y debajo del disco están la barra de la canción y la del volumen.
/// </summary>
public partial class OpenCasePanel
{
    readonly MediaPlayer _player = new();
    readonly DispatcherTimer _progress = new() { Interval = TimeSpan.FromMilliseconds(250) };
    bool _soundtrackShown, _turning, _seeking, _musicPlaying, _padMode;
    SoundtrackTrack? _current;
    bool _playerReady;

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
        _progress.Tick += (_, _) => UpdateProgress();
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
        _scene.TurnTray(true, () =>
        {
            _turning = false;
            _soundtrackShown = true;
            SoundtrackView.Visibility = Visibility.Visible;
            SoundtrackView.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
        });
        GameButtons.Visibility = Visibility.Collapsed;
        SoundtrackButtons.Visibility = Visibility.Visible;
        HintText.Text = _padMode
            ? "↑ ↓  Canción     ·     A  Reproducir / parar     ·     ← →  Retroceder / avanzar     ·     B  Volver al juego"
            : "↑ ↓  Canción     ·     Enter o clic en el disco  Reproducir / parar     ·     ← →  Retroceder / avanzar     ·     Esc  Volver al juego";
    }

    /// <summary>Vuelve al juego: la hoja del disco regresa a su sitio. La música sigue sonando hasta que cierres la caja.</summary>
    void LeaveSoundtrack(bool immediate = false)
    {
        SoundtrackView.BeginAnimation(OpacityProperty, null);
        SoundtrackView.Opacity = 0;
        SoundtrackView.Visibility = Visibility.Collapsed;
        GameButtons.Visibility = Visibility.Visible;
        SoundtrackButtons.Visibility = Visibility.Collapsed;
        ShowGameHint();
        if (immediate)
        {
            _soundtrackShown = _turning = false;
            StopMusic();
            return;
        }
        if (!_soundtrackShown)
            return;
        _soundtrackShown = false;
        _turning = true;
        _scene.TurnTray(false, () => _turning = false);
    }

    void FillSoundtrack(Game game)
    {
        var soundtrack = game.Soundtrack;
        SoundtrackTitle.Text = soundtrack is null ? game.Name : soundtrack.Artist is { Length: > 0 } artist ? $"{soundtrack.Title} · {artist}" : soundtrack.Title;
        SoundtrackDisc.Source = OpenCaseArt.RenderDisc(ImageStore.GetSoundtrackDiscPath(game), _item?.ImagePath, soundtrack?.DiscFrame);
        TrackList.Items.Clear();
        var withMusic = 0;
        foreach (var track in soundtrack?.Tracks ?? [])
        {
            var hasMusic = Soundtracks.PathOf(game, track) is not null;
            withMusic += hasMusic ? 1 : 0;
            var row = new DockPanel { Margin = new Thickness(2, 3, 6, 3) };
            var length = new TextBlock { Text = Soundtracks.Length(track.LengthMs), FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x71, 0x66)), Margin = new Thickness(12, 0, 0, 0) };
            DockPanel.SetDock(length, Dock.Right);
            row.Children.Add(length);
            row.Children.Add(new TextBlock { Text = $"{track.Number}.", Width = 34, FontSize = 15, Foreground = new SolidColorBrush(Color.FromRgb(0x8C, 0x82, 0x76)) });
            row.Children.Add(new TextBlock { Text = track.Title, FontSize = 15, FontFamily = new FontFamily("Georgia"), Foreground = new SolidColorBrush(Color.FromRgb(0x2A, 0x24, 0x20)), TextTrimming = TextTrimming.CharacterEllipsis });
            TrackList.Items.Add(new ListBoxItem
            {
                Content = row, Tag = track, Opacity = hasMusic ? 1 : 0.45, Cursor = hasMusic ? Cursors.Hand : Cursors.Arrow,
                ToolTip = hasMusic ? "Doble clic para escucharla" : "Sin música: impórtala en Personalizar → Banda sonora",
            });
        }
        SoundtrackEmpty.Text = soundtrack is null
            ? "Este juego aún no tiene banda sonora. Búscala en Personalizar (E) → Banda sonora e importa tus archivos de música."
            : withMusic == 0
                ? "Ninguna canción tiene música todavía: impórtala en Personalizar (E) → Banda sonora. Se registran las que se llamen como en la lista."
                : withMusic < soundtrack.Tracks.Count ? $"{withMusic} de {soundtrack.Tracks.Count} canciones con música." : "";
        // Elegida, la que suena o la primera con música.
        var selected = TrackList.Items.OfType<ListBoxItem>().FirstOrDefault(item => item.Tag == _current)
                       ?? TrackList.Items.OfType<ListBoxItem>().FirstOrDefault(item => item.Opacity == 1);
        TrackList.SelectedItem = selected;
        NowPlaying.Text = _current is { } current && _musicPlaying ? current.Title : (selected?.Tag as SoundtrackTrack)?.Title ?? "";
        UpdateProgress();
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
            PlayTrack(track);
    }

    void OnSoundtrackDiscClick(object sender, MouseButtonEventArgs e) => ToggleMusic();

    /// <summary>El disco: si suena, se para; si no, suena la canción elegida (o sigue donde iba).</summary>
    void ToggleMusic()
    {
        var selected = (TrackList.SelectedItem as ListBoxItem)?.Tag as SoundtrackTrack;
        if (_musicPlaying)
        {
            if (selected is null || selected == _current)
            {
                _player.Pause();
                SetPlaying(false);
                return;
            }
        }
        else if (_current is not null && (selected is null || selected == _current) && _player.Source is not null)
        {
            _player.Play();
            SetPlaying(true);
            return;
        }
        if (selected is not null)
            PlayTrack(selected);
    }

    void PlayTrack(SoundtrackTrack track)
    {
        if (_game is not { } game || Soundtracks.PathOf(game, track) is not { } path)
        {
            NowPlaying.Text = "Esta canción no tiene música: impórtala en Personalizar → Banda sonora";
            return;
        }
        EnsurePlayer();
        _current = track;
        _player.Open(new Uri(path));
        _player.Volume = VolumeSlider.Value;
        _player.Play();
        NowPlaying.Text = track.Title;
        TrackList.SelectedItem = TrackList.Items.OfType<ListBoxItem>().FirstOrDefault(item => item.Tag == track);
        SetPlaying(true);
    }

    /// <summary>Al acabar una canción, la siguiente que tenga música.</summary>
    void PlayNext()
    {
        var tracks = _game?.Soundtrack?.Tracks ?? [];
        var index = _current is null ? -1 : tracks.IndexOf(_current);
        var next = tracks.Skip(index + 1).FirstOrDefault(track => _game is { } g && Soundtracks.PathOf(g, track) is not null);
        if (next is not null)
            PlayTrack(next);
        else
            StopMusic();
    }

    void Seek(double seconds)
    {
        if (_current is null || !_player.NaturalDuration.HasTimeSpan)
            return;
        var target = _player.Position + TimeSpan.FromSeconds(seconds);
        _player.Position = target < TimeSpan.Zero ? TimeSpan.Zero : target > _player.NaturalDuration.TimeSpan ? _player.NaturalDuration.TimeSpan : target;
        UpdateProgress();
    }

    void StopMusic()
    {
        if (!_playerReady)
            return;
        _player.Stop();
        _player.Close();
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
        else
        {
            _progress.Stop();
        }
    }

    void UpdateProgress()
    {
        if (_seeking)
            return;
        var duration = _current is not null && _player.NaturalDuration.HasTimeSpan ? _player.NaturalDuration.TimeSpan : TimeSpan.Zero;
        var position = _current is not null ? _player.Position : TimeSpan.Zero;
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
            _player.Position = TimeSpan.FromSeconds(SeekSlider.Value);
        UpdateProgress();
    }

    void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_playerReady)
            _player.Volume = e.NewValue;
        if (VolumeText is not null)
            VolumeText.Text = $"{e.NewValue * 100:0} %";
        VolumeChanged?.Invoke(e.NewValue);
    }
}
