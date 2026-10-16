using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Basilisco.Services;

namespace Basilisco.Customization;

/// <summary>
/// Busca un juego en un servicio de internet (SteamGridDB, TheGamesDB…), enseña sus imágenes
/// y descarga la que elija el usuario.
/// </summary>
public partial class ArtSearchPanel : UserControl
{
    static readonly Brush ErrorBrush = Brushes.Firebrick;
    static readonly Brush InfoBrush = new SolidColorBrush(Color.FromRgb(0x5B, 0x65, 0x73));

    AppSettings _settings = new();
    ArtProvider? _provider;
    Action<string>? _onChosen;
    CancellationTokenSource? _work;

    public bool IsOpen => Visibility == Visibility.Visible;

    public ArtSearchPanel()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    /// <param name="onChosen">Recibe la ruta de la imagen descargada.</param>
    public void Open(ArtProvider provider, AppSettings settings, string searchTerm, Action<string> onChosen)
    {
        _provider = provider;
        _settings = settings;
        _onChosen = onChosen;

        TitleText.Text = $"Buscar {provider.ImageKind} en {provider.Name}";
        KeyHelpText.Text = $"Para buscar en {provider.Name} hace falta tu clave de su API. {provider.KeyHelp}";
        SearchBox.Text = searchTerm;
        GamesList.ItemsSource = null;
        ImagesList.ItemsSource = null;
        SetStatus("");
        Visibility = Visibility.Visible;

        if (string.IsNullOrWhiteSpace(provider.GetKey(settings)))
        {
            ShowKeyView(true);
        }
        else
        {
            ShowKeyView(false);
            _ = SearchAsync();
        }
    }

    void ShowKeyView(bool show)
    {
        KeyView.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SearchView.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        ChangeKeyButton.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        if (show)
            KeyBox.Text = _provider?.GetKey(_settings) ?? "";

        var focus = show ? KeyBox : SearchBox;
        _ = Dispatcher.BeginInvoke(() => { focus.Focus(); focus.SelectAll(); }, DispatcherPriority.Input);
    }

    /// <summary>Cancela lo que estuviera en marcha y devuelve un token para la operación nueva.</summary>
    CancellationToken Restart()
    {
        _work?.Cancel();
        _work = new CancellationTokenSource();
        return _work.Token;
    }

    async Task SearchAsync()
    {
        if (_provider is not { } provider || provider.GetKey(_settings) is not { } key)
            return;

        var term = SearchBox.Text.Trim();
        if (term.Length == 0)
            return;

        var cancellation = Restart();
        GamesList.ItemsSource = null;
        ImagesList.ItemsSource = null;
        SetStatus($"Buscando «{term}» en {provider.Name}…");

        try
        {
            var games = await provider.SearchAsync(term, key, cancellation);
            if (cancellation.IsCancellationRequested)
                return;

            GamesList.ItemsSource = games;
            if (games.Count == 0)
            {
                SetStatus("No se ha encontrado ningún juego con ese nombre. Prueba a escribirlo de otra forma (por ejemplo, en inglés).");
                return;
            }
            SetStatus("Elige el juego de la lista y después la imagen.");
            GamesList.SelectedIndex = 0;
        }
        catch (ArtProviderException ex)
        {
            SetStatus(ex.Message, error: true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    async Task LoadImagesAsync(ArtGame game)
    {
        if (_provider is not { } provider || provider.GetKey(_settings) is not { } key)
            return;

        var cancellation = Restart();
        ImagesList.ItemsSource = null;
        SetStatus($"Cargando imágenes de «{game.Name}»…");

        try
        {
            var images = await provider.GetImagesAsync(game, key, cancellation);
            if (cancellation.IsCancellationRequested)
                return;

            ImagesList.ItemsSource = images;
            SetStatus(images.Count == 0
                ? $"«{game.Name}» no tiene ninguna {provider.ImageKind} en {provider.Name}. Prueba con otro resultado de la lista."
                : $"{images.Count} imágenes. Haz clic en la que quieras usar.");
        }
        catch (ArtProviderException ex)
        {
            SetStatus(ex.Message, error: true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    async Task ChooseAsync(ArtImage image)
    {
        var cancellation = Restart();
        SetStatus("Descargando la imagen…");
        try
        {
            var path = await ArtProvider.DownloadAsync(image, cancellation);
            if (cancellation.IsCancellationRequested)
                return;

            var onChosen = _onChosen;
            Close();
            onChosen?.Invoke(path);
        }
        catch (ArtProviderException ex)
        {
            SetStatus(ex.Message, error: true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    void SaveKey()
    {
        if (_provider is null)
            return;

        var key = KeyBox.Text.Trim();
        if (key.Length == 0)
        {
            SetStatus("Pega tu clave en el campo de texto.", error: true);
            return;
        }

        _provider.SetKey(_settings, key);
        if (!_settings.Save())
            SetStatus("No se ha podido guardar la clave en basilisco-data/settings.json; solo se usará hasta que cierres Basilisco.", error: true);

        ShowKeyView(false);
        _ = SearchAsync();
    }

    void Close()
    {
        _work?.Cancel();
        _onChosen = null;
        GamesList.ItemsSource = null;
        ImagesList.ItemsSource = null;
        Visibility = Visibility.Collapsed;
    }

    void SetStatus(string text, bool error = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = error ? ErrorBrush : InfoBrush;
    }

    void OnGameSelected(object sender, SelectionChangedEventArgs e)
    {
        if (GamesList.SelectedItem is ArtGame game)
            _ = LoadImagesAsync(game);
    }

    void OnImageClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ArtImage image)
            _ = ChooseAsync(image);
    }

    void OnSearchClick(object sender, RoutedEventArgs e) => _ = SearchAsync();

    void OnSaveKeyClick(object sender, RoutedEventArgs e) => SaveKey();

    void OnChangeKeyClick(object sender, RoutedEventArgs e) => ShowKeyView(true);

    void OnGetKeyClick(object sender, RoutedEventArgs e)
    {
        if (_provider is not null)
            Process.Start(new ProcessStartInfo(_provider.KeyUrl) { UseShellExecute = true });
    }

    void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Enter when KeyView.Visibility == Visibility.Visible:
                SaveKey();
                break;
            case Key.Enter:
                _ = SearchAsync();
                break;
            default:
                return;
        }
        e.Handled = true;
    }
}
