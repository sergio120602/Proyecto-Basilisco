using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Basilisco.Services;

namespace Basilisco.Customization;

/// <summary>
/// Lector de manuales en PDF dentro de Basilisco: se pasa de página con las flechas, el mando, la rueda o haciendo
/// clic en la página. Las páginas se dibujan con el lector de PDF de Windows a medida que hacen falta.
/// </summary>
public partial class ManualReaderPanel : UserControl
{
    readonly Dictionary<int, BitmapSource> _pages = [];
    string? _path;
    int _pageCount;
    int _spread;            // 0: solo la portada; n: las páginas 2n−1 y 2n
    int _version;           // cambia al abrir otro manual, para descartar páginas de uno anterior

    public event Action? Closed;

    public bool IsOpen => Visibility == Visibility.Visible;

    public ManualReaderPanel()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    public async void Open(string path, string title, bool padMode)
    {
        var version = ++_version;
        _path = path;
        _pages.Clear();
        _spread = 0;
        _pageCount = 0;
        TitleText.Text = title;
        HintText.Text = padMode ? "← →  Pasar página     ·     B  Cerrar" : "← →  Pasar página     ·     Esc  Cerrar";
        LeftPage.Source = RightPage.Source = null;
        LoadingText.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(() => Focus(), DispatcherPriority.Input);

        _pageCount = await PdfRenderer.PageCountAsync(path);
        if (version != _version)
            return;
        if (_pageCount == 0)
        {
            LoadingText.Text = "No se ha podido abrir el manual.";
            return;
        }
        await ShowSpreadAsync();
    }

    int SpreadCount => _pageCount / 2 + 1;

    (int? Left, int? Right) PagesOf(int spread) => spread == 0
        ? (null, 0)
        : (2 * spread - 1, 2 * spread < _pageCount ? 2 * spread : null);

    async Task ShowSpreadAsync()
    {
        var version = _version;
        var (left, right) = PagesOf(_spread);
        PageText.Text = left is null ? "Portada"
            : right is null ? $"Página {left + 1} de {_pageCount}"
            : $"Páginas {left + 1}–{right + 1} de {_pageCount}";
        PreviousButton.IsEnabled = _spread > 0;
        NextButton.IsEnabled = _spread < SpreadCount - 1;

        LeftPage.Source = left is { } l ? await PageAsync(l) : null;
        RightPage.Source = right is { } r ? await PageAsync(r) : null;
        if (version != _version)
            return;
        LoadingText.Visibility = Visibility.Collapsed;

        // Se adelanta la siguiente doble página para que pasar sea instantáneo.
        if (_spread + 1 < SpreadCount)
        {
            var (nextLeft, nextRight) = PagesOf(_spread + 1);
            foreach (var page in new[] { nextLeft, nextRight }.OfType<int>())
                await PageAsync(page);
        }
    }

    async Task<BitmapSource?> PageAsync(int index)
    {
        if (_pages.TryGetValue(index, out var cached))
            return cached;
        if (_path is not { } path)
            return null;
        var version = _version;
        // Al alto de la pantalla, en píxeles reales, para que el texto se lea nítido.
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleY;
        var height = (int)Math.Clamp(ActualHeight * dpi, 800, 2400);
        var rendered = await PdfRenderer.RenderAsync(path, height, [index]);
        if (version != _version || rendered.Count == 0)
            return null;
        _pages[index] = rendered[0];
        return rendered[0];
    }

    public void Flip(int direction)
    {
        var next = Math.Clamp(_spread + direction, 0, Math.Max(0, SpreadCount - 1));
        if (next == _spread || _pageCount == 0)
            return;
        _spread = next;
        _ = ShowSpreadAsync();
    }

    public void Close()
    {
        if (!IsOpen)
            return;
        _version++;
        Visibility = Visibility.Collapsed;
        LeftPage.Source = RightPage.Source = null;
        _pages.Clear();
        LoadingText.Text = "Abriendo el manual…";
        Closed?.Invoke();
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Right or Key.PageDown or Key.Enter or Key.Space: Flip(1); break;
            case Key.Left or Key.PageUp: Flip(-1); break;
            case Key.Home: Flip(-SpreadCount); break;
            case Key.End: Flip(SpreadCount); break;
            case Key.Escape or Key.Back: Close(); break;
            default: return;
        }
        e.Handled = true;
    }

    void OnMouseWheel(object sender, MouseWheelEventArgs e) => Flip(e.Delta < 0 ? 1 : -1);

    void OnLeftPageClick(object sender, MouseButtonEventArgs e) => Flip(-1);

    void OnRightPageClick(object sender, MouseButtonEventArgs e) => Flip(1);

    void OnPreviousClick(object sender, RoutedEventArgs e) => Flip(-1);

    void OnNextClick(object sender, RoutedEventArgs e) => Flip(1);

    void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    void OnOpenExternalClick(object sender, RoutedEventArgs e)
    {
        if (_path is null)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(_path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(Window.GetWindow(this), $"No se ha podido abrir el PDF.\n\n{ex.Message}", "Basilisco",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
