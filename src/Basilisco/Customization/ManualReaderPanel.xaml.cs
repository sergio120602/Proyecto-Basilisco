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
/// Lector de manuales: el manual como un libro en 3D, con la hoja pasando al avanzar o volver (flechas, mando, rueda o un
/// clic en la mitad derecha o izquierda). Vale para los PDF y para los manuales de imágenes. Solo se tienen en memoria las
/// páginas que se ven y las de al lado (para que pasar sea instantáneo), al alto de la pantalla.
/// </summary>
public partial class ManualReaderPanel : UserControl
{
    // Las páginas ya dibujadas, solo las de cerca de donde estás (las demás se sueltan).
    readonly Dictionary<int, BitmapSource> _pages = [];
    ManualSource? _source;
    int _pageCount;
    int _spread;            // 0: solo la portada; n: las páginas 2n−1 y 2n
    int _version;           // cambia al abrir otro manual, para descartar páginas de uno anterior
    bool _busy;

    public event Action? Closed;

    public bool IsOpen => Visibility == Visibility.Visible;

    public ManualReaderPanel()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    public void Open(string path, string title, bool padMode) => Open(new PdfManual(path), title, padMode);

    public async void Open(ManualSource source, string title, bool padMode)
    {
        var version = ++_version;
        _source = source;
        _pages.Clear();
        _spread = 0;
        _pageCount = 0;
        TitleText.Text = title;
        HintText.Text = padMode
            ? "← →  Pasar página     ·     B  Cerrar"
            : "← →  Pasar página     ·     Ctrl + rueda o + −  Zoom (arrastra para moverte)     ·     Esc  Cerrar";
        SetZoom(1);
        ExternalButton.Visibility = source.PdfPath is null ? Visibility.Collapsed : Visibility.Visible;
        Book.Show(null, null);
        LoadingText.Text = "Abriendo el manual…";
        LoadingText.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(() => Focus(), DispatcherPriority.Input);

        _pageCount = await source.CountAsync();
        if (version != _version)
            return;
        if (_pageCount == 0)
        {
            LoadingText.Text = "No se ha podido abrir el manual.";
            return;
        }
        // Las páginas, con su proporción (la de la portada).
        if (await PageAsync(0) is { } cover)
            Book.PageAspect = (double)cover.PixelWidth / cover.PixelHeight;
        if (version != _version)
            return;
        LoadingText.Visibility = Visibility.Collapsed;
        await ShowSpreadAsync(direction: 0);
    }

    int SpreadCount => _pageCount / 2 + 1;

    (int? Left, int? Right) PagesOf(int spread) => spread == 0
        ? (null, 0)
        : (2 * spread - 1, 2 * spread < _pageCount ? 2 * spread : null);

    async Task<BitmapSource?> Page(int? index) => index is { } i ? await PageAsync(i) : null;

    /// <summary>Enseña la doble página actual; con <paramref name="direction"/>, pasando la hoja desde la anterior.</summary>
    async Task ShowSpreadAsync(int direction)
    {
        var version = _version;
        var (left, right) = PagesOf(_spread);
        PageText.Text = left is null ? "Portada"
            : right is null ? $"Página {left + 1} de {_pageCount}"
            : $"Páginas {left + 1}–{right + 1} de {_pageCount}";
        PreviousButton.IsEnabled = _spread > 0;
        NextButton.IsEnabled = _spread < SpreadCount - 1;

        if (direction == 0)
        {
            Book.Show(await Page(left), await Page(right));
        }
        else
        {
            var (oldLeft, oldRight) = PagesOf(_spread - direction);
            // Hacia delante: la hoja es la derecha de antes y por detrás la nueva izquierda. Hacia atrás, al revés.
            var front = direction > 0 ? await Page(oldRight) : await Page(right);
            var back = direction > 0 ? await Page(left) : await Page(oldLeft);
            var underLeft = direction > 0 ? await Page(oldLeft) : await Page(left);
            var underRight = direction > 0 ? await Page(right) : await Page(oldRight);
            if (version != _version)
                return;
            Book.Turn(direction, front, back, underLeft, underRight);
        }
        if (version != _version)
            return;

        // Se adelantan las de la siguiente y la anterior doble página, y se sueltan las lejanas.
        var near = new HashSet<int>();
        for (var spread = Math.Max(0, _spread - 1); spread <= Math.Min(SpreadCount - 1, _spread + 1); spread++)
        {
            var (l, r) = PagesOf(spread);
            foreach (var page in new[] { l, r }.OfType<int>())
                near.Add(page);
        }
        foreach (var far in _pages.Keys.Where(page => !near.Contains(page) && page != 0).ToList())
            _pages.Remove(far);
        foreach (var page in near)
            await PageAsync(page);
    }

    async Task<BitmapSource?> PageAsync(int index)
    {
        if (_pages.TryGetValue(index, out var cached))
            return cached;
        if (_source is not { } source || index < 0 || index >= _pageCount && _pageCount > 0)
            return null;
        var version = _version;
        // Al alto de la pantalla, en píxeles reales, para que el texto se lea nítido.
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleY;
        // Con zoom, más resolución (hasta 3200 px), para que el texto siga nítido de cerca.
        var height = (int)Math.Clamp(ActualHeight * dpi * Math.Min(Book.Zoom, 2), 800, 3200);
        var page = await source.PageAsync(index, height, CancellationToken.None);
        if (version != _version || page is null)
            return null;
        _pages[index] = page;
        return page;
    }

    public async void Flip(int direction)
    {
        var next = Math.Clamp(_spread + direction, 0, Math.Max(0, SpreadCount - 1));
        if (next == _spread || _pageCount == 0 || _busy)
            return;
        _busy = true;
        try
        {
            Book.Finish();
            var step = Math.Sign(next - _spread);
            // Saltos largos (Inicio, Fin): directamente, sin pasar todas las hojas.
            var animate = Math.Abs(next - _spread) == 1;
            _spread = next;
            await ShowSpreadAsync(animate ? step : 0);
        }
        finally
        {
            _busy = false;
        }
    }

    public void Close()
    {
        if (!IsOpen)
            return;
        _version++;
        Book.Finish();
        Book.Show(null, null);
        Visibility = Visibility.Collapsed;
        _pages.Clear();
        _source = null;
        LoadingText.Text = "Abriendo el manual…";
        Closed?.Invoke();
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Right or Key.PageDown or Key.Enter or Key.Space: Flip(1); break;
            case Key.Left or Key.PageUp: Flip(-1); break;
            case Key.OemPlus or Key.Add: SetZoom(Book.Zoom * ZoomStep); break;
            case Key.OemMinus or Key.Subtract: SetZoom(Book.Zoom / ZoomStep); break;
            case Key.D0 or Key.NumPad0: SetZoom(1); break;
            case Key.Home: Flip(-SpreadCount); break;
            case Key.End: Flip(SpreadCount); break;
            case Key.Escape or Key.Back: Close(); break;
            default: return;
        }
        e.Handled = true;
    }

    // ---- Zoom ----

    const double ZoomStep = 1.25;
    Point? _dragStart;
    Point _dragLast;
    bool _dragged;
    double _renderedZoom = 1;
    DispatcherTimer? _sharpen;

    /// <summary>
    /// Acerca o aleja el libro (del 100 % al 400 %), hacia el ratón si se da su posición. Al pararse, las páginas se
    /// vuelven a dibujar con más resolución para que el texto se lea nítido de cerca.
    /// </summary>
    void SetZoom(double zoom, Point? at = null)
    {
        zoom = Math.Clamp(zoom, Carousel.BookView.MinZoom, Carousel.BookView.MaxZoom);
        if (Math.Abs(zoom - 1) < 0.04)
            zoom = 1;
        if (at is { } point)
            Book.ZoomAt(zoom, point);
        else
            Book.Zoom = zoom;
        ZoomText.Text = $"{Book.Zoom * 100:0} %";
        FitButton.IsEnabled = Book.Zoom > 1;
        Book.Cursor = Book.Zoom > 1 ? Cursors.SizeAll : Cursors.Hand;

        if (Math.Abs(Math.Min(Book.Zoom, 2) - Math.Min(_renderedZoom, 2)) < 0.2 || _pageCount == 0)
            return;
        _sharpen ??= new DispatcherTimer(TimeSpan.FromMilliseconds(350), DispatcherPriority.Background, async (_, _) =>
        {
            _sharpen!.Stop();
            _renderedZoom = Book.Zoom;
            _pages.Clear();
            if (!Book.IsTurning && _pageCount > 0)
                await ShowSpreadAsync(direction: 0);
        }, Dispatcher);
        _sharpen.Stop();
        _sharpen.Start();
    }

    void OnZoomInClick(object sender, RoutedEventArgs e) => SetZoom(Book.Zoom * ZoomStep);

    void OnZoomOutClick(object sender, RoutedEventArgs e) => SetZoom(Book.Zoom / ZoomStep);

    void OnFitClick(object sender, RoutedEventArgs e) => SetZoom(1);

    // La rueda pasa página; con Ctrl, hace zoom hacia el ratón.
    void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            SetZoom(Book.Zoom * (e.Delta > 0 ? ZoomStep : 1 / ZoomStep), e.GetPosition(Book));
        else
            Flip(e.Delta < 0 ? 1 : -1);
        e.Handled = true;
    }

    void OnBookMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Doble clic: del libro entero al doble, y vuelta.
        if (e.ClickCount == 2)
        {
            SetZoom(Book.Zoom > 1 ? 1 : 2, e.GetPosition(Book));
            _dragStart = null;
            e.Handled = true;
            return;
        }
        _dragStart = _dragLast = e.GetPosition(Book);
        _dragged = false;
        Book.CaptureMouse();
    }

    void OnBookMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed)
            return;
        var now = e.GetPosition(Book);
        if (!_dragged && (now - start).Length < 6)
            return;
        _dragged = true;
        Book.PanBy(now - _dragLast);
        _dragLast = now;
    }

    void OnBookLostCapture(object sender, MouseEventArgs e) => _dragStart = null;

    // Un clic (sin arrastrar) en la mitad derecha pasa página; en la izquierda, vuelve.
    void OnBookClick(object sender, MouseButtonEventArgs e)
    {
        var wasDrag = _dragged || _dragStart is null;
        _dragStart = null;
        Book.ReleaseMouseCapture();
        if (!wasDrag)
            Flip(Book.IsLeftHalf(e.GetPosition(Book)) ? -1 : 1);
    }

    void OnPreviousClick(object sender, RoutedEventArgs e) => Flip(-1);

    void OnNextClick(object sender, RoutedEventArgs e) => Flip(1);

    void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    void OnOpenExternalClick(object sender, RoutedEventArgs e)
    {
        if (_source?.PdfPath is not { } path)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(Window.GetWindow(this), $"No se ha podido abrir el PDF.\n\n{ex.Message}", "Basilisco",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
