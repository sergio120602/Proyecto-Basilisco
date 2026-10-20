using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Basilisco.Carousel;
using Basilisco.Models;
using Basilisco.Services;
using Microsoft.Win32;

namespace Basilisco.Customization;

/// <summary>Elegir la imagen de una consola para el menú de consolas: de LaunchBox, un archivo propio o ninguna.</summary>
public partial class ConsolePanel : UserControl
{
    Platform? _platform;

    /// <summary>Buscar la imagen en LaunchBox (lo abre la ventana principal, con su buscador de imágenes).</summary>
    public event Action<Platform>? LaunchBoxRequested;

    /// <summary>Ha cambiado la imagen de la consola.</summary>
    public event Action? Changed;

    public event Action? Closed;

    public bool IsOpen => Visibility == Visibility.Visible;

    public ConsolePanel()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    public void Open(Platform platform)
    {
        _platform = platform;
        TitleText.Text = platform.Name;
        StatusText.Text = "";
        UpdatePreview();
        Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(() => Focus(), DispatcherPriority.Input);
    }

    /// <summary>Pone la imagen elegida (se copia a basilisco-data/platforms al momento).</summary>
    public void SetImage(string path)
    {
        if (_platform is null)
            return;
        if (!ImageStore.IsImage(path) || CoverArt.LoadImage(path, 200) is null)
        {
            StatusText.Text = "No se ha podido abrir esa imagen. Prueba con un PNG, JPG o WebP.";
            return;
        }
        try
        {
            ImageStore.SetPlatformImage(_platform.Id, path);
            StatusText.Text = "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"No se ha podido guardar la imagen: {ex.Message}";
        }
        UpdatePreview();
        Changed?.Invoke();
    }

    void UpdatePreview()
    {
        if (_platform is null)
            return;
        var path = ImageStore.GetPlatformImagePath(_platform.Id);
        PreviewImage.Source = path is null ? null : CoverArt.LoadImage(path, 900);
        PreviewText.Text = PreviewImage.Source is null ? _platform.Name : "";
        RemoveButton.IsEnabled = path is not null;
    }

    void OnLaunchBoxClick(object sender, RoutedEventArgs e)
    {
        if (_platform is not null)
            LaunchBoxRequested?.Invoke(_platform);
    }

    void OnChooseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Elegir imagen de la consola", Filter = ImageStore.FileFilter };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            SetImage(dialog.FileName);
    }

    void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (_platform is null)
            return;
        ImageStore.RemovePlatformImage(_platform.Id);
        UpdatePreview();
        Changed?.Invoke();
    }

    public void Close()
    {
        if (!IsOpen)
            return;
        Visibility = Visibility.Collapsed;
        PreviewImage.Source = null;
        Closed?.Invoke();
    }

    void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    void OnBackdropMouseDown(object sender, MouseButtonEventArgs e) => Close();

    void OnCardMouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter)
        {
            Close();
            e.Handled = true;
        }
    }
}
