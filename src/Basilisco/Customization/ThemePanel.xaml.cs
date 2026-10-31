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
/// <param name="InsideOpacity">Opacidad general de la imagen del interior de la caja (de 0 a 1).</param>
/// <param name="ClearOwnOpacity">Quitar la opacidad del interior propia de cada juego, para que todos usen la general.</param>
public sealed record ThemeResult(ImageChange Background, string? TextColor, bool ClearOwn, double InsideOpacity, bool ClearOwnOpacity);

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

    /// <summary>Han cambiado las opciones de rendimiento (calidad, reflejos, fondos en vídeo); se aplican al momento.</summary>
    public event Action<BoxQuality, bool, bool>? PerformanceChanged;

    /// <summary>Quiere optimizar la carpeta de datos (lo hace la ventana principal, que tiene la biblioteca).</summary>
    public event Action? OptimizeRequested;

    BoxQuality _quality;

    /// <summary>Pone las opciones de rendimiento guardadas (antes de abrir).</summary>
    public void SetPerformance(BoxQuality quality, bool reflections, bool animated)
    {
        _quality = quality;
        SyncQuality();
        ReflectionsToggle.IsChecked = reflections;
        AnimatedToggle.IsChecked = animated;
    }

    void SyncQuality()
    {
        QualityHigh.IsChecked = _quality == BoxQuality.High;
        QualityMedium.IsChecked = _quality == BoxQuality.Medium;
        QualityLow.IsChecked = _quality == BoxQuality.Low;
    }

    void OnQualityClick(object sender, RoutedEventArgs e)
    {
        _quality = sender == QualityLow ? BoxQuality.Low : sender == QualityMedium ? BoxQuality.Medium : BoxQuality.High;
        SyncQuality();
        OnPerformanceClick(sender, e);
    }

    void OnPerformanceClick(object sender, RoutedEventArgs e) =>
        PerformanceChanged?.Invoke(_quality, ReflectionsToggle.IsChecked == true, AnimatedToggle.IsChecked == true);

    void OnOptimizeClick(object sender, RoutedEventArgs e) => OptimizeRequested?.Invoke();

    /// <summary>Se ha elegido otro modo (claro, nocturno o como Windows); se aplica al momento.</summary>
    public event Action<ColorMode>? ModeChanged;

    public bool IsOpen => Visibility == Visibility.Visible;

    public ThemePanel()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
        BuildSwatches();
    }

    /// <param name="savedBackground">Ruta del fondo general actual, si hay.</param>
    /// <param name="ownCount">Juegos que tienen fondo o color propios.</param>
    /// <param name="insideOpacity">Opacidad general de la imagen del interior de la caja.</param>
    /// <param name="ownOpacityCount">Juegos que tienen su propia opacidad del interior.</param>
    public void Open(string? savedBackground, string? textColor, int ownCount, ColorMode mode, double insideOpacity, int ownOpacityCount)
    {
        SyncMode(mode);
        _background = ImageChange.None;
        _savedBackground = savedBackground;
        _textColor = textColor;
        _clearOwn = false;
        _ownCount = ownCount;
        _clearOwnOpacity = false;
        _ownOpacityCount = ownOpacityCount;
        InsideOpacitySlider.Value = insideOpacity;
        SyncColor();
        UpdateOwn();
        UpdateOpacity();
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
        BackgroundText.Text = path is null ? "Sin fondo: el de siempre."
            : $"{(video ? "Fondo dinámico (vídeo)" : "Fondo estático (imagen)")} · {Path.GetFileName(path)}";
        if (video && path is not null && !VideoConverter.IsPlayable(path))
            BackgroundText.Text = $"Fondo dinámico · {Path.GetFileName(path)} · se convertirá a un formato compatible al usarlo (unos segundos)";
        RemoveButton.IsEnabled = path is not null;

        var color = _textColor is { } hex ? (Color)ColorConverter.ConvertFromString(hex) : ThemeManager.DefaultText;
        PreviewTitle.Foreground = PreviewTime.Foreground = new SolidColorBrush(color);
        PreviewGlow.Color = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255 > 0.6 ? Colors.Black : Colors.White;
    }

    void SyncMode(ColorMode mode)
    {
        LightToggle.IsChecked = mode == ColorMode.Light;
        DarkToggle.IsChecked = mode == ColorMode.Dark;
        SystemToggle.IsChecked = mode == ColorMode.System;
    }

    void OnModeClick(object sender, RoutedEventArgs e)
    {
        var mode = sender == LightToggle ? ColorMode.Light : sender == DarkToggle ? ColorMode.Dark : ColorMode.System;
        SyncMode(mode);
        ModeChanged?.Invoke(mode);
        UpdatePreview();
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

    bool _clearOwnOpacity;
    int _ownOpacityCount;

    void UpdateOpacity()
    {
        InsideOpacityText.Text = $"{InsideOpacitySlider.Value * 100:0} %";
        OwnOpacityText.Text = _clearOwnOpacity ? "Se quitarán al guardar: todos usarán esta."
            : _ownOpacityCount == 0 ? "Todos los juegos usan esta."
            : _ownOpacityCount == 1 ? "1 juego tiene la suya (Personalizar → Interior → Ajustar…)."
            : $"{_ownOpacityCount} juegos tienen la suya (Personalizar → Interior → Ajustar…).";
        ClearOwnOpacityButton.IsEnabled = _ownOpacityCount > 0 && !_clearOwnOpacity;
    }

    void OnInsideOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OwnOpacityText is not null) // Al crear el panel, la barra se carga antes que el texto.
            UpdateOpacity();
    }

    void OnClearOwnOpacityClick(object sender, RoutedEventArgs e)
    {
        _clearOwnOpacity = true;
        UpdateOpacity();
    }

    void OnPreviewVideoEnded(object sender, RoutedEventArgs e)
    {
        PreviewVideo.Position = TimeSpan.Zero;
        PreviewVideo.Play();
    }

    void Save()
    {
        var result = new ThemeResult(_background, _textColor, _clearOwn, Math.Round(InsideOpacitySlider.Value, 2), _clearOwnOpacity);
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
