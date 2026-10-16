using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Basilisco.Carousel;
using Basilisco.Models;
using Basilisco.Services;
using Microsoft.Win32;

namespace Basilisco.Customization;

/// <summary>Lo que el usuario ha decidido en el menú de personalización.</summary>
/// <param name="NewImagePath">Imagen nueva elegida (aún sin copiar), o null si no ha cambiado.</param>
/// <param name="RemoveImage">True si ha quitado la portada personalizada.</param>
public sealed record CustomizeResult(Game Game, string Name, string? NewImagePath, bool RemoveImage);

/// <summary>Menú para cambiar el nombre y la portada de un juego. Los cambios solo se aplican al guardar.</summary>
public partial class CustomizePanel : UserControl
{
    Game? _game;
    string? _newImagePath;
    bool _removeImage;

    public event Action<CustomizeResult>? Saved;
    public event Action? Closed;

    public bool IsOpen => Visibility == Visibility.Visible;

    public CustomizePanel()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    public void Open(Game game)
    {
        _game = game;
        _newImagePath = null;
        _removeImage = false;
        ErrorText.Visibility = Visibility.Collapsed;
        PathText.Text = game.Path;
        PathText.ToolTip = game.Path;
        NameBox.Text = game.Name;

        Visibility = Visibility.Visible;
        UpdatePreview();
        Dispatcher.BeginInvoke(() =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        });
    }

    /// <summary>Usa la imagen dada como nueva portada (desde el selector o arrastrándola).</summary>
    public void SetImage(string path)
    {
        if (!CoverStore.IsImage(path) || CoverArt.LoadImage(path) is null)
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
        _newImagePath ?? (_removeImage || _game is null ? null : CoverStore.GetPath(_game));

    void UpdatePreview()
    {
        if (_game is null)
            return;

        var image = CurrentImagePath;
        PreviewImage.Source = CoverArt.RenderCover(new CarouselItem(NameBox.Text, IsAddCard: false, image));
        RemoveImageButton.IsEnabled = image is not null;
    }

    void Save()
    {
        if (_game is null)
            return;

        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            ShowError("El nombre no puede estar vacío.");
            NameBox.Focus();
            return;
        }

        var result = new CustomizeResult(_game, name, _newImagePath, _removeImage);
        Close();
        Saved?.Invoke(result);
    }

    void Close()
    {
        _game = null;
        PreviewImage.Source = null;
        Visibility = Visibility.Collapsed;
        Closed?.Invoke();
    }

    void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    void OnChooseImageClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Elegir portada", Filter = CoverStore.FileFilter };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            SetImage(dialog.FileName);
    }

    void OnRemoveImageClick(object sender, RoutedEventArgs e)
    {
        _newImagePath = null;
        _removeImage = true;
        UpdatePreview();
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
