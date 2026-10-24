using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Basilisco.Carousel;
using Basilisco.Models;
using Basilisco.Services;

namespace Basilisco.Customization;

/// <summary>
/// Lector de manuales: el manual como un libro en 3D, con la hoja pasando al avanzar o volver (flechas, mando, rueda o un
/// clic en la mitad derecha o izquierda). Vale para los PDF y para los manuales de imágenes. Se lee como un libro abierto
/// (modo libreta, donde las imágenes dobles ocupan las dos páginas) o de página en página (que se pueden girar). Solo se tienen en memoria las
/// páginas que se ven y las de al lado (para que pasar sea instantáneo), al alto de la pantalla.
/// </summary>
public partial class ManualReaderPanel : UserControl
{
    /// <summary>Lo que va en una página: una página del manual entera, o una mitad (−1, 1) de una doble; −1 de página es una en blanco.</summary>
    readonly record struct Slot(int Page, int Half);

    /// <summary>Lo que se ve de una vez: las dos páginas del libro abierto, o (en una sola página) solo la derecha.</summary>
    sealed record View(Slot? Left, Slot? Right);

    // Las páginas ya dibujadas, solo las de cerca de donde estás (las demás se sueltan).
    readonly Dictionary<int, BitmapSource> _pages = [];
    ManualSource? _source;
    List<View> _views = [];
    int _pageCount;
    int _view;              // la vista actual (en el libro abierto, 0 es la portada sola)
    int _version;           // cambia al abrir otro manual, para descartar páginas de uno anterior
    bool _busy;

    public event Action? Closed;

    /// <summary>Se ha cambiado el formato o el giro de una página: hay que guardar el juego.</summary>
    public event Action? Edited;

    /// <summary>«Hacer con Pinterest»: elegir las imágenes del manual de este juego.</summary>
    public event Action<Game>? PinterestRequested;

    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>El juego del manual abierto (null si es un PDF suelto).</summary>
    public Game? Game { get; private set; }

    bool Single => SinglePageToggle.IsChecked == true;

    public ManualReaderPanel()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    public void Open(string path, Game? game, string title, bool padMode) =>
        Open(new PdfManual(path, game is null ? [] : game.ManualPdfRotations ??= []), game, title, padMode);

    public async void Open(ManualSource source, Game? game, string title, bool padMode)
    {
        var version = ++_version;
        _source = source;
        Game = game;
        _pages.Clear();
        _views = [];
        _view = 0;
        _pageCount = 0;
        TitleText.Text = title;
        HintText.Text = padMode
            ? "← →  Pasar página     ·     B  Cerrar"
            : "← →  Pasar página     ·     Ctrl + rueda o + −  Zoom (arrastra para moverte)     ·     P  Una página / libreta     ·     Esc  Cerrar";
        SetZoom(1);
        ExternalButton.Visibility = source.PdfPath is null ? Visibility.Collapsed : Visibility.Visible;
        PinterestButton.Visibility = game is null ? Visibility.Collapsed : Visibility.Visible;
        SinglePageToggle.IsChecked = game?.ManualSinglePage == true;
        Book.SinglePage = Single;
        RotateButton.Visibility = Single ? Visibility.Visible : Visibility.Collapsed;
        RotateButton.IsEnabled = false;
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
        // Las páginas del libro abierto, con la proporción de la portada (la mitad, si es doble).
        if (await PageAsync(0) is { } cover)
            Book.PageAspect = (double)cover.PixelWidth / cover.PixelHeight / (source.IsSpread(0) ? 2 : 1);
        if (version != _version)
            return;
        LoadingText.Visibility = Visibility.Collapsed;
        BuildViews();
        RotateButton.IsEnabled = true;
        await ShowViewAsync(direction: 0);
    }

    /// <summary>
    /// Reparte las páginas. En una sola página, una por vista. En el libro abierto, de dos en dos con la portada sola a la
    /// derecha; una imagen doble empieza siempre a la izquierda (si le toca a la derecha, esa se queda en blanco).
    /// </summary>
    void BuildViews()
    {
        var views = new List<View>();
        if (Single || _source is null)
        {
            for (var page = 0; page < _pageCount; page++)
                views.Add(new View(null, new Slot(page, 0)));
        }
        else
        {
            Slot? left = null;
            var onLeft = false;     // la primera (la portada) va a la derecha
            for (var page = 0; page < _pageCount; page++)
            {
                if (_source.IsSpread(page))
                {
                    if (!onLeft && left is not null)
                        views.Add(new View(left, new Slot(-1, 0)));
                    views.Add(new View(new Slot(page, -1), new Slot(page, 1)));
                    left = null;
                    onLeft = true;
                }
                else if (onLeft)
                {
                    left = new Slot(page, 0);
                    onLeft = false;
                }
                else
                {
                    views.Add(new View(left, new Slot(page, 0)));
                    left = null;
                    onLeft = true;
                }
            }
            if (left is not null)
                views.Add(new View(left, null));
        }
        _views = views;
    }

    /// <summary>La vista en la que está esa página del manual.</summary>
    int ViewOf(int page) => Math.Max(0, _views.FindIndex(view => view.Left?.Page == page || view.Right?.Page == page));

    /// <summary>La primera página de verdad de la vista actual.</summary>
    int CurrentPage
    {
        get
        {
            if (_views.Count == 0)
                return 0;
            var view = _views[_view];
            return view.Left is { Page: >= 0 } left ? left.Page : view.Right is { Page: >= 0 } right ? right.Page : view.Left?.Page ?? 0;
        }
    }

    async Task<PageFace?> FaceAsync(Slot? slot)
    {
        if (slot is not { } s)
            return null;
        if (s.Page < 0)
            return BookView.Blank;
        return await PageAsync(s.Page) is { } image
            ? new PageFace(image, s.Half, Single && _source is { } source ? source.Rotation(s.Page) : 0)
            : null;
    }

    static string Describe(View view, int count)
    {
        var pages = new[] { view.Left, view.Right }.OfType<Slot>().Where(slot => slot.Page >= 0).Select(slot => slot.Page).Distinct().ToList();
        if (pages.Count == 0)
            return "";
        if (pages is [0] && view.Left is null)
            return "Portada";
        if (pages.Count == 1)
            return $"Página {pages[0] + 1} de {count}";
        return $"Páginas {pages[0] + 1}–{pages[^1] + 1} de {count}";
    }

    /// <summary>Enseña la vista actual; con <paramref name="direction"/>, pasando la hoja desde la anterior.</summary>
    async Task ShowViewAsync(int direction)
    {
        if (_views.Count == 0)
            return;
        var version = _version;
        var view = _views[_view];
        PageText.Text = Describe(view, _pageCount);
        PreviousButton.IsEnabled = _view > 0;
        NextButton.IsEnabled = _view < _views.Count - 1;

        if (direction == 0)
        {
            var left = await FaceAsync(view.Left);
            var right = await FaceAsync(view.Right);
            if (version != _version)
                return;
            Book.Show(left, right);
        }
        else
        {
            var old = _views[_view - direction];
            // Hacia delante: la hoja es la derecha de antes y por detrás la nueva izquierda. Hacia atrás, al revés.
            var front = direction > 0 ? await FaceAsync(old.Right) : await FaceAsync(view.Right);
            var back = direction > 0 ? await FaceAsync(view.Left) : await FaceAsync(old.Left);
            var underLeft = direction > 0 ? await FaceAsync(old.Left) : await FaceAsync(view.Left);
            var underRight = direction > 0 ? await FaceAsync(view.Right) : await FaceAsync(old.Right);
            if (version != _version)
                return;
            Book.Turn(direction, front, back, underLeft, underRight);
        }

        // Se adelantan las de la vista siguiente y la anterior, y se sueltan las lejanas.
        var near = new HashSet<int>();
        for (var index = Math.Max(0, _view - 1); index <= Math.Min(_views.Count - 1, _view + 1); index++)
        {
            foreach (var slot in new[] { _views[index].Left, _views[index].Right }.OfType<Slot>().Where(slot => slot.Page >= 0))
                near.Add(slot.Page);
        }
        foreach (var far in _pages.Keys.Where(page => !near.Contains(page) && page != 0).ToList())
            _pages.Remove(far);
        foreach (var page in near)
        {
            await PageAsync(page);
            if (version != _version)
                return;
        }
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
        var next = Math.Clamp(_view + direction, 0, Math.Max(0, _views.Count - 1));
        if (next == _view || _views.Count == 0 || _busy)
            return;
        _busy = true;
        try
        {
            Book.Finish();
            var step = Math.Sign(next - _view);
            // Saltos largos (Inicio, Fin): directamente, sin pasar todas las hojas.
            var animate = Math.Abs(next - _view) == 1;
            _view = next;
            await ShowViewAsync(animate ? step : 0);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Cambia entre una sola página y el libro abierto, sin perder por dónde ibas; se recuerda en el juego.</summary>
    async void ToggleSinglePage()
    {
        var page = CurrentPage;
        Book.SinglePage = Single;
        RotateButton.Visibility = Single ? Visibility.Visible : Visibility.Collapsed;
        if (Game is { } game && game.ManualSinglePage != Single)
        {
            game.ManualSinglePage = Single;
            Edited?.Invoke();
        }
        if (_pageCount == 0)
            return;
        BuildViews();
        _view = ViewOf(page);
        await ShowViewAsync(direction: 0);
    }

    void OnSinglePageClick(object sender, RoutedEventArgs e) => ToggleSinglePage();

    /// <summary>Gira la página un cuarto de vuelta (en una sola página); se recuerda.</summary>
    async void Rotate()
    {
        if (!Single || _source is null || _views.Count == 0)
            return;
        var page = CurrentPage;
        _source.SetRotation(page, (_source.Rotation(page) + 90) % 360);
        Edited?.Invoke();
        await ShowViewAsync(direction: 0);
    }

    void OnRotateClick(object sender, RoutedEventArgs e) => Rotate();

    void OnPinterestClick(object sender, RoutedEventArgs e)
    {
        if (Game is { } game)
            PinterestRequested?.Invoke(game);
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
        _views = [];
        _source = null;
        Game = null;
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
            case Key.Home: Flip(-_views.Count); break;
            case Key.End: Flip(_views.Count); break;
            case Key.P: SinglePageToggle.IsChecked = !Single; ToggleSinglePage(); break;
            case Key.R: Rotate(); break;
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
                await ShowViewAsync(direction: 0);
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
