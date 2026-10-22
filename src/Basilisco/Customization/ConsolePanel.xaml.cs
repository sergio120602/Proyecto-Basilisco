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

/// <summary>
/// Elegir la imagen de una casilla de los menús en franja: la de una consola (de LaunchBox, un archivo propio o ninguna)
/// o la de un miembro de la familia de Steam (su foto de perfil, un archivo propio o ninguna).
/// </summary>
public partial class ConsolePanel : UserControl
{
    string _name = "";
    string _folder = AppInfo.PlatformImagesDirectory;
    string _id = "";
    Action? _source;

    /// <summary>Buscar la imagen en LaunchBox (lo abre la ventana principal, con su buscador de imágenes).</summary>
    public event Action<Platform>? LaunchBoxRequested;

    /// <summary>Ha cambiado la imagen.</summary>
    public event Action? Changed;

    public event Action? Closed;

    public bool IsOpen => Visibility == Visibility.Visible;

    public ConsolePanel()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    public void Open(Platform platform) =>
        Show(platform.Name, AppInfo.PlatformImagesDirectory, platform.Id,
            "La imagen que se ve en el menú de consolas. Sin imagen, se escribe su nombre.",
            "Buscar en LaunchBox", "La foto de la consola en la base de datos de LaunchBox", () => LaunchBoxRequested?.Invoke(platform));

    /// <param name="downloadAvatar">Descarga su foto de perfil de Steam y devuelve la ruta (null si no tiene o falla).</param>
    public void OpenMember(FamilyMember member, Func<Task<string?>>? downloadAvatar) =>
        Show(member.Name, AppInfo.FamilyImagesDirectory, member.Key,
            "La imagen que se ve en el menú de Steam familiar. Sin imagen, se escribe su nombre.",
            downloadAvatar is null ? null : "Foto de perfil de Steam", "Su foto de perfil de Steam, en grande",
            downloadAvatar is null ? null : () => _ = UseAvatarAsync(downloadAvatar));

    void Show(string name, string folder, string id, string help, string? sourceLabel, string? sourceTip, Action? source)
    {
        _name = name;
        _folder = folder;
        _id = id;
        _source = source;
        TitleText.Text = name;
        HelpText.Text = help;
        SourceButton.Content = sourceLabel;
        SourceButton.ToolTip = sourceTip;
        SourceButton.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
        StatusText.Text = "";
        UpdatePreview();
        Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(() => Focus(), DispatcherPriority.Input);
    }

    async Task UseAvatarAsync(Func<Task<string?>> download)
    {
        StatusText.Text = "Descargando la foto de perfil…";
        var id = _id;
        var path = await download();
        if (!IsOpen || _id != id)
            return;
        if (path is null)
            StatusText.Text = "No se ha podido descargar la foto de perfil. Comprueba tu conexión a internet.";
        else
            SetImage(path);
    }

    /// <summary>Pone la imagen elegida (se copia a basilisco-data al momento).</summary>
    public void SetImage(string path)
    {
        if (_id.Length == 0)
            return;
        if (!ImageStore.IsImage(path) || CoverArt.LoadImage(path, 200) is null)
        {
            StatusText.Text = "No se ha podido abrir esa imagen. Prueba con un PNG, JPG o WebP.";
            return;
        }
        try
        {
            ImageStore.SetMenuImage(_folder, _id, path);
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
        var path = ImageStore.GetMenuImagePath(_folder, _id);
        PreviewImage.Source = path is null ? null : CoverArt.LoadImage(path, 900);
        PreviewText.Text = PreviewImage.Source is null ? _name : "";
        RemoveButton.IsEnabled = path is not null;
    }

    void OnSourceClick(object sender, RoutedEventArgs e) => _source?.Invoke();

    void OnChooseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = $"Elegir imagen de «{_name}»", Filter = ImageStore.FileFilter };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            SetImage(dialog.FileName);
    }

    void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (_id.Length == 0)
            return;
        ImageStore.RemoveMenuImage(_folder, _id);
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
