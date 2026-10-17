using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Basilisco.Carousel;
using Basilisco.Services;
using Microsoft.Win32;

namespace Basilisco.Customization;

/// <summary>Lo decidido en la ventana de Apariencia.</summary>
/// <param name="Background">Cambio del fondo general (imagen o vídeo).</param>
/// <param name="TextColor">Color general de las letras, o null para el de siempre.</param>
/// <param name="ClearOwn">Quitar el fondo y el color propios de cada juego, para que todos usen los generales.</param>
public sealed record ThemeResult(ImageChange Background, string? TextColor, bool ClearOwn);

/// <summary>Apariencia general de Basilisco: fondo (estático o dinámico) y color de las letras para todos los juegos.</summary>
public partial class ThemePanel : UserControl
{
    static readonly (string Name, string? Hex)[] Swatches =
    [
        ("Automático", null), ("Blanco", "#FFFFFF"), ("Negro", "#111418"), ("Gris", "#C8CDD5"),
        ("Amarillo", "#FFD54A"), ("Celeste", "#7FD3FF"), ("Rosa", "#FF8FB1"), ("Verde", "#8BE38B"),
    ];

    ImageChange _background = ImageChange.None;
    string? _savedBackground;
    string? _textColor;
    bool _clearOwn;
    int _ownCount;
    bool _syncing;

    public event Action<ThemeResult>? Saved;

    public bool IsOpen => Visibility == Visibility.Visible;

    public ThemePanel()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
        BuildSwatches();
    }

    /// <param name="savedBackground">Ruta del fondo general actual, si hay.</param>
    /// <param name="ownCount">Juegos que tienen fondo o color propios.</param>
    public void Open(string? savedBackground, string? textColor, int ownCount)
    {
        _background = ImageChange.None;
        _savedBackground = savedBackground;
        _textColor = textColor;
        _clearOwn = false;
        _ownCount = ownCount;
        SyncColor();
        UpdateOwn();
        UpdatePreview();
        Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(() => Focus(), DispatcherPriority.Input);
    }

    string? CurrentBackground => _background.NewPath ?? (_background.Remove ? null : _savedBackground);

    void UpdatePreview()
    {
        var path = CurrentBackground;
        PreviewVideo.Stop();
        PreviewVideo.Source = null;
        PreviewImage.Source = null;
        var video = path is not null && ImageStore.IsVideo(path);
        if (path is not null && File.Exists(path))
        {
            if (video)
            {
                PreviewVideo.Source = new Uri(path);
                PreviewVideo.Play();
            }
            else
            {
                PreviewImage.Source = CoverArt.LoadImage(path, 1200);
            }
        }
        PreviewVeil.Visibility = path is null ? Visibility.Collapsed : Visibility.Visible;
        BackgroundText.Text = path is null ? "Sin fondo: el gris claro de siempre."
            : $"{(video ? "Fondo dinámico (vídeo)" : "Fondo estático (imagen)")} · {Path.GetFileName(path)}";
        if (video && path is not null && !VideoConverter.IsPlayable(path))
            BackgroundText.Text = $"Fondo dinámico · {Path.GetFileName(path)} · se convertirá a un formato compatible al usarlo (unos segundos)";
        RemoveButton.IsEnabled = path is not null;

        var color = _textColor is { } hex ? (Color)ColorConverter.ConvertFromString(hex) : Color.FromRgb(0x1F, 0x26, 0x30);
        PreviewTitle.Foreground = PreviewTime.Foreground = new SolidColorBrush(color);
        PreviewGlow.Color = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255 > 0.6 ? Colors.Black : Colors.White;
    }

    void UpdateOwn()
    {
        OwnText.Text = _clearOwn ? "Se quitarán al guardar: todos usarán la apariencia general."
            : _ownCount == 0 ? "Ninguno: todos usan la apariencia general."
            : _ownCount == 1 ? "1 juego tiene fondo o color propios." : $"{_ownCount} juegos tienen fondo o color propios.";
        ClearOwnButton.IsEnabled = _ownCount > 0 && !_clearOwn;
    }

    void BuildSwatches()
    {
        foreach (var (name, hex) in Swatches)
        {
            var dot = new Border
            {
                Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 7, 0),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAF)), BorderThickness = new Thickness(1),
                Background = hex is null
                    ? new LinearGradientBrush(Color.FromRgb(0x1B, 0x22, 0x2C), Colors.White, 45)
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
            };
            var toggle = new ToggleButton
            {
                Tag = hex,
                Style = (Style)FindResource("FilterToggle"),
                Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center } } },
            };
            toggle.Click += (_, _) =>
            {
                _textColor = hex;
                SyncColor();
                UpdatePreview();
            };
            ColorSwatches.Children.Add(toggle);
        }
    }

    void SyncColor()
    {
        foreach (var toggle in ColorSwatches.Children.OfType<ToggleButton>())
            toggle.IsChecked = string.Equals(toggle.Tag as string, _textColor, StringComparison.OrdinalIgnoreCase);
        _syncing = true;
        ColorBox.Text = _textColor ?? "";
        _syncing = false;
    }

    void OnColorBoxChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing)
            return;
        var text = ColorBox.Text.Trim();
        if (text.Length == 0)
            _textColor = null;
        else if (Regex.IsMatch(text, "^#?[0-9a-fA-F]{6}$"))
            _textColor = "#" + text.TrimStart('#').ToUpperInvariant();
        else
            return;
        foreach (var toggle in ColorSwatches.Children.OfType<ToggleButton>())
            toggle.IsChecked = string.Equals(toggle.Tag as string, _textColor, StringComparison.OrdinalIgnoreCase);
        UpdatePreview();
    }

    void OnImageClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Fondo para todos los juegos", Filter = ImageStore.FileFilter };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true && CoverArt.LoadImage(dialog.FileName) is not null)
        {
            _background = new ImageChange(dialog.FileName, false);
            UpdatePreview();
        }
    }

    void OnVideoClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Vídeo de fondo para todos los juegos", Filter = ImageStore.VideoFilter };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            _background = new ImageChange(dialog.FileName, false);
            UpdatePreview();
        }
    }

    void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        _background = new ImageChange(null, true);
        UpdatePreview();
    }

    void OnClearOwnClick(object sender, RoutedEventArgs e)
    {
        _clearOwn = true;
        UpdateOwn();
    }

    void OnPreviewVideoEnded(object sender, RoutedEventArgs e)
    {
        PreviewVideo.Position = TimeSpan.Zero;
        PreviewVideo.Play();
    }

    void Save()
    {
        var result = new ThemeResult(_background, _textColor, _clearOwn);
        Close();
        Saved?.Invoke(result);
    }

    void Close()
    {
        PreviewVideo.Stop();
        PreviewVideo.Source = null;
        PreviewImage.Source = null;
        Visibility = Visibility.Collapsed;
    }

    void OnSaveClick(object sender, RoutedEventArgs e) => Save();

    void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when Keyboard.FocusedElement is not TextBox: Save(); break;
            case Key.Escape: Close(); break;
            default: return;
        }
        e.Handled = true;
    }
}
