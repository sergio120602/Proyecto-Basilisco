using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Basilisco.Carousel;
using Basilisco.Models;
using Basilisco.Services;
using Microsoft.Win32;

namespace Basilisco.Customization;

/// <summary>Lo que el usuario ha decidido en el editor de filtros.</summary>
/// <param name="Filter">El filtro editado, o null si es uno nuevo.</param>
/// <param name="NewImagePath">Imagen nueva elegida (aún sin copiar), o null si no ha cambiado.</param>
/// <param name="RemoveImage">True si ha quitado la imagen del filtro.</param>
public sealed record FilterResult(GameFilter? Filter, string Name, string? NewImagePath, bool RemoveImage);

/// <summary>Editor para crear un filtro o cambiar su nombre e imagen. Los cambios solo se aplican al guardar.</summary>
public partial class FilterPanel : UserControl
{
    const int IconDecodeWidth = 480;

    GameFilter? _filter;
    string? _newImagePath;
    bool _removeImage;

    public event Action<FilterResult>? Saved;
    public event Action<GameFilter>? DeleteRequested;

    /// <summary>Buscar un logotipo en SteamGridDB (con el nombre del filtro); lo abre la ventana principal.</summary>
    public event Action<string>? LogoSearchRequested;

    void OnSearchLogoClick(object sender, RoutedEventArgs e) => LogoSearchRequested?.Invoke(NameBox.Text.Trim());

    public bool IsOpen => Visibility == Visibility.Visible;

    public FilterPanel()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    public void OpenNew() => Open(null, "", 0);

    public void OpenEdit(GameFilter filter, int gameCount) => Open(filter, filter.Name, gameCount);

    void Open(GameFilter? filter, string name, int gameCount)
    {
        _filter = filter;
        _newImagePath = null;
        _removeImage = false;
        ErrorText.Visibility = Visibility.Collapsed;

        HeaderText.Text = filter is null ? "Nueva saga" : "Editar saga";
        SubtitleText.Text = filter is null
            ? "Agrupa tus juegos como quieras. Después, asígnalos desde Personalizar (E) en cada juego."
            : gameCount switch
            {
                0 => "Aún no tiene juegos. Asígnalos desde Personalizar (E) en cada juego.",
                1 => "1 juego en esta saga.",
                _ => $"{gameCount} juegos en esta saga.",
            };
        DeleteButton.Visibility = filter is null ? Visibility.Collapsed : Visibility.Visible;
        NameBox.Text = name;

        Visibility = Visibility.Visible;
        UpdatePreview();
        // Prioridad Input: el panel ya se ha maquetado y es visible, así que el foco sí se queda.
        Dispatcher.BeginInvoke(() =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    /// <summary>Usa la imagen dada para el filtro (desde el selector o arrastrándola).</summary>
    public void SetImage(string path)
    {
        if (!ImageStore.IsImage(path) || CoverArt.LoadImage(path, IconDecodeWidth) is null)
        {
            ShowError("No se ha podido abrir esa imagen. Prueba con un PNG, JPG, BMP, GIF o WebP.");
            return;
        }

        _newImagePath = path;
        _removeImage = false;
        ErrorText.Visibility = Visibility.Collapsed;
        UpdatePreview();
    }

    string? CurrentImagePath =>
        _newImagePath ?? (_removeImage || _filter is null ? null : ImageStore.GetFilterImagePath(_filter));

    void UpdatePreview()
    {
        var name = NameBox.Text.Trim();
        PreviewLabel.Text = name.Length == 0 ? "Nueva saga" : name;

        var path = CurrentImagePath;
        var preview = path is null ? null : CoverArt.LoadImage(path, 440);
        PreviewImage.Source = preview;
        PreviewIcon.Source = path is null ? null : CoverArt.LoadImage(path, IconDecodeWidth);
        // Igual que en la barra de arriba: con imagen se ve solo el logo, sin el nombre.
        var hasLogo = preview is not null;
        PreviewIcon.Visibility = hasLogo ? Visibility.Visible : Visibility.Collapsed;
        PreviewLabel.Visibility = hasLogo ? Visibility.Collapsed : Visibility.Visible;
        PreviewPill.Padding = hasLogo ? new Thickness(16, 4, 16, 4) : new Thickness(16, 7, 16, 7);
        PreviewPill.CornerRadius = new CornerRadius(hasLogo ? 24 : 19);
        PreviewInitial.Text = preview is null && name.Length > 0 ? name[..1].ToUpperInvariant() : "";
        RemoveImageButton.IsEnabled = path is not null;
    }

    void Save()
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            ShowError("La saga necesita un nombre.");
            NameBox.Focus();
            return;
        }

        var result = new FilterResult(_filter, name, _newImagePath, _removeImage);
        Close();
        Saved?.Invoke(result);
    }

    void Close()
    {
        _filter = null;
        PreviewImage.Source = null;
        PreviewIcon.Source = null;
        Visibility = Visibility.Collapsed;
    }

    void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    void OnChooseImageClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Elegir imagen de la saga", Filter = ImageStore.FileFilter };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            SetImage(dialog.FileName);
    }

    void OnRemoveImageClick(object sender, RoutedEventArgs e)
    {
        _newImagePath = null;
        _removeImage = true;
        UpdatePreview();
    }

    void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_filter is not { } filter)
            return;

        Close();
        DeleteRequested?.Invoke(filter);
    }

    void OnNameChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    void OnSaveClick(object sender, RoutedEventArgs e) => Save();

    void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    // Clic fuera de la tarjeta: cerrar sin guardar.
    void OnBackdropMouseDown(object sender, MouseButtonEventArgs e) => Close();

    void OnCardMouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter: Save(); break;
            case Key.Escape: Close(); break;
            default: return;
        }
        e.Handled = true;
    }
}
