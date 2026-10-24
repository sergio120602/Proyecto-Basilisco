using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace Basilisco.Customization;

/// <summary>
/// Pinterest dentro de Basilisco, para hacer el manual de un juego con imágenes: se busca (por defecto «#juego #artbook»),
/// se eligen las imágenes con un clic (cada una lleva encima su número de orden) y «Guardar como manual» se las da a la
/// ventana principal, que las descarga. Es tu navegación normal con tu cuenta (el navegador Edge de Windows); la sesión
/// se guarda en basilisco-data/webview para no tener que entrar cada vez.
/// </summary>
public partial class PinterestPanel : UserControl
{
    /// <summary>
    /// Una imagen elegida: su parte de la dirección en i.pinimg.com, la grande, la miniatura y si ocupa las dos páginas del
    /// libro abierto (para las horizontales).
    /// </summary>
    sealed record Pick(string Key, string Url, string Thumb, bool Spread = false)
    {
        public static Pick FromKey(string key, bool spread = false) =>
            new(key, "https://i.pinimg.com/originals" + key, "https://i.pinimg.com/236x" + key, spread);
    }

    /// <summary>Una página del manual que se guarda: la imagen y si ocupa las dos páginas del libro abierto.</summary>
    public sealed record SavedPage(string Url, bool Spread);

    List<Pick> _picks = [];
    bool _ready;
    CoreWebView2Environment? _environment;
    Task? _starting;

    /// <summary>Se ha pulsado «Guardar como manual»: las imágenes elegidas, en su orden.</summary>
    public event Action<List<SavedPage>>? Saved;

    public event Action? Closed;

    public bool IsOpen => Visibility == Visibility.Visible;

    public PinterestPanel()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    /// <summary>Escala de la interfaz (solo las barras: el navegador no se puede escalar como el resto).</summary>
    public Transform UiScale
    {
        set
        {
            TopBar.LayoutTransform = value;
            BottomBar.LayoutTransform = value;
        }
    }

    /// <summary>Abre Pinterest para el manual del juego; las páginas que ya tenga salen elegidas (para cambiarlas).</summary>
    public async void Open(string gameName, IReadOnlyList<Models.ManualPage>? existing = null)
    {
        TitleText.Text = gameName;
        SearchBox.Text = $"#{Tag(gameName)} #artbook";
        _picks = [];
        foreach (var page in existing ?? [])
        {
            var match = System.Text.RegularExpressions.Regex.Match(page.Source ?? "", @"^https://i\.pinimg\.com/(?:\d+x|originals)(/.+)$");
            if (match.Success && _picks.All(pick => pick.Key != match.Groups[1].Value))
                _picks.Add(Pick.FromKey(match.Groups[1].Value, page.Spread));
        }
        SelectToggle.IsChecked = true;
        UpdateStrip();
        Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(() => SearchBox.Focus(), DispatcherPriority.Input);
        await StartAsync();
        Search();
    }

    /// <summary>El nombre como etiqueta de Pinterest: sin espacios ni signos («God of War III» → «GodofWarIII»).</summary>
    static string Tag(string name) => new(name.Where(char.IsLetterOrDigit).ToArray());

    Task StartAsync() => _starting ??= StartCoreAsync();

    async Task StartCoreAsync()
    {
        BrowserStatus.Text = "Abriendo Pinterest…";
        try
        {
            _environment = await Services.WebViews.EnvironmentAsync();
            await Browser.EnsureCoreWebView2Async(_environment);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or System.Runtime.InteropServices.COMException or InvalidOperationException or IOException
                                   or UnauthorizedAccessException)
        {
            BrowserStatus.Text = "No se ha podido abrir el navegador de Windows (WebView2). Instálalo desde microsoft.com/edge/webview2 y vuelve a intentarlo.";
            return;
        }

        var core = Browser.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        await core.AddScriptToExecuteOnDocumentCreatedAsync(SelectionScript);
        core.WebMessageReceived += OnWebMessage;
        // Cada página nueva empieza sin saber qué hay elegido: se le dice.
        core.NavigationCompleted += (_, _) => _ = PushStateAsync();
        core.NewWindowRequested += OnNewWindowRequested;
        _ready = true;
        BrowserStatus.Text = "";
    }

    /// <summary>
    /// Las ventanas de inicio de sesión (Google, Facebook, Apple…) se abren como ventanas de verdad, con la misma sesión:
    /// al terminar avisan a Pinterest y se cierran solas (cargadas aquí dentro se quedaban en blanco). Lo demás que pida
    /// otra pestaña (abrir un pin) se abre aquí mismo.
    /// </summary>
    async void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (!IsLoginWindow(e) || _environment is null)
        {
            e.Handled = true;
            Browser.CoreWebView2.Navigate(e.Uri);
            return;
        }

        var deferral = e.GetDeferral();
        var owner = Window.GetWindow(this);
        var features = e.WindowFeatures;
        var popup = new Window
        {
            Title = "Iniciar sesión · Pinterest",
            Width = features.HasSize ? Math.Max(420, features.Width) : 520,
            Height = features.HasSize ? Math.Max(560, features.Height) : 720,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Icon = owner?.Icon,
        };
        var view = new Microsoft.Web.WebView2.Wpf.WebView2();
        popup.Content = view;
        popup.Show();
        try
        {
            await view.EnsureCoreWebView2Async(_environment);
            e.NewWindow = view.CoreWebView2;
            e.Handled = true;
            view.CoreWebView2.WindowCloseRequested += (_, _) => popup.Close();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            popup.Close();
            e.Handled = true;
            Browser.CoreWebView2.Navigate(e.Uri);
        }
        finally
        {
            deferral.Complete();
        }
        popup.Closed += (_, _) =>
        {
            view.Dispose();
            _ = PushStateAsync();
        };
    }

    /// <summary>¿Es una ventana de inicio de sesión? Las piden con tamaño propio, o son de los servicios de cuentas.</summary>
    static bool IsLoginWindow(CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (e.WindowFeatures.HasSize)
            return true;
        var host = Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) ? uri.Host : "";
        return host.Contains("google.", StringComparison.OrdinalIgnoreCase) || host.Contains("facebook.", StringComparison.OrdinalIgnoreCase) ||
               host.Contains("apple.com", StringComparison.OrdinalIgnoreCase) || e.Uri.Contains("oauth", StringComparison.OrdinalIgnoreCase) ||
               e.Uri.Contains("login", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(e.Uri) || e.Uri == "about:blank";
    }

    void Search()
    {
        if (!_ready || SearchBox.Text.Trim() is not { Length: > 0 } query)
            return;
        Browser.CoreWebView2.Navigate($"https://www.pinterest.com/search/pins/?q={Uri.EscapeDataString(query)}");
    }

    async Task PushStateAsync()
    {
        if (!_ready)
            return;
        var keys = JsonSerializer.Serialize(_picks.Select(pick => pick.Key));
        var selecting = SelectToggle.IsChecked == true ? "true" : "false";
        try
        {
            await Browser.CoreWebView2.ExecuteScriptAsync($"window.__basilisco && window.__basilisco.load({keys}, {selecting});");
        }
        catch (InvalidOperationException)
        {
        }
    }

    void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var json = JsonDocument.Parse(e.TryGetWebMessageAsString());
            // Las que ya estaban elegidas conservan si son dobles.
            var spread = _picks.Where(pick => pick.Spread).Select(pick => pick.Key).ToHashSet();
            var picks = new List<Pick>();
            foreach (var item in json.RootElement.EnumerateArray())
            {
                if (item.GetString() is { Length: > 1 } key && key.StartsWith('/'))
                    picks.Add(Pick.FromKey(key, spread.Contains(key)));
            }
            _picks = picks;
            UpdateStrip();
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
        }
    }

    void UpdateStrip()
    {
        SelectedStrip.Children.Clear();
        for (var i = 0; i < _picks.Count; i++)
        {
            var pick = _picks[i];
            var thumb = new BitmapImage();
            thumb.BeginInit();
            thumb.UriSource = new Uri(pick.Thumb);
            thumb.DecodePixelHeight = 184;
            thumb.EndInit();
            // Las dobles, el doble de anchas: así se ve cómo quedarán en el libro abierto.
            var tile = new Grid { Height = 88, Width = pick.Spread ? 140 : 66, Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand, ToolTip = "Clic para quitarla" };
            tile.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(6), BorderBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xB9, 0x23)), BorderThickness = new Thickness(2),
                Background = new ImageBrush(thumb) { Stretch = Stretch.UniformToFill },
            });
            tile.Children.Add(new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xB9, 0x23)),
                Child = new TextBlock { Text = (i + 1).ToString(), FontWeight = FontWeights.Bold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            var key = pick.Key;
            // «↔ Doble»: la imagen ocupa las dos páginas del libro abierto (sobre todo para las horizontales).
            var gold = Color.FromRgb(0xE8, 0xB9, 0x23);
            var spreadButton = new Border
            {
                CornerRadius = new CornerRadius(5), Margin = new Thickness(4), Padding = new Thickness(6, 1, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Background = pick.Spread ? new SolidColorBrush(gold) : new SolidColorBrush(Color.FromArgb(0xD0, 0x0B, 0x0D, 0x11)),
                Child = new TextBlock
                {
                    Text = pick.Spread ? "↔ Doble" : "↔", FontWeight = FontWeights.Bold, FontSize = 12,
                    Foreground = pick.Spread ? Brushes.Black : Brushes.White,
                },
                ToolTip = pick.Spread
                    ? "Ocupa las dos páginas del libro abierto. Clic: que vuelva a ser una página"
                    : "Clic: que ocupe las dos páginas del libro abierto (para las imágenes horizontales). En una sola página se ve entera igual",
            };
            spreadButton.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                var index = _picks.FindIndex(p => p.Key == key);
                if (index >= 0)
                    _picks[index] = _picks[index] with { Spread = !_picks[index].Spread };
                UpdateStrip();
            };
            tile.Children.Add(spreadButton);
            tile.MouseLeftButtonUp += (_, _) =>
            {
                _picks.RemoveAll(p => p.Key == key);
                UpdateStrip();
                _ = PushStateAsync();
            };
            SelectedStrip.Children.Add(tile);
        }
        CountText.Text = _picks.Count switch
        {
            0 => "Pulsa las imágenes que quieras para el manual: cada una lleva su número de orden.",
            1 => "1 imagen elegida",
            var n => $"{n} imágenes elegidas · serán las páginas del manual en este orden (↔: que ocupe dos páginas)",
        };
        SaveButton.IsEnabled = ClearButton.IsEnabled = _picks.Count > 0;
    }

    void OnSearchClick(object sender, RoutedEventArgs e) => Search();

    void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Search();
    }

    void OnSelectToggleClick(object sender, RoutedEventArgs e) => _ = PushStateAsync();

    void OnClearClick(object sender, RoutedEventArgs e)
    {
        _picks = [];
        UpdateStrip();
        _ = PushStateAsync();
    }

    void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_picks.Count == 0)
            return;
        var pages = _picks.Select(pick => new SavedPage(pick.Url, pick.Spread)).ToList();
        Close();
        Saved?.Invoke(pages);
    }

    void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    public void Close()
    {
        if (!IsOpen)
            return;
        Visibility = Visibility.Collapsed;
        Closed?.Invoke();
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Se mete en cada página de Pinterest. Con «Seleccionar» encendido, un clic sobre una imagen la elige o la quita (sin
    /// abrir el pin) y se dibuja encima su número de orden con un borde dorado. Le pasa a Basilisco la lista elegida.
    /// </summary>
    const string SelectionScript = """
        (() => {
          if (window.__basilisco) return;
          const state = { selecting: true, keys: [] };
          const layer = document.createElement('div');
          layer.style.cssText = 'position:fixed;left:0;top:0;width:0;height:0;z-index:2147483647;pointer-events:none';
          const keyOf = url => { const m = /^https:\/\/i\.pinimg\.com\/(?:\d+x|originals)(\/.+)$/.exec(url || ''); return m ? m[1] : null; };
          const bestUrl = img => {
            if (img.srcset) { const parts = img.srcset.split(',').map(s => s.trim().split(' ')[0]).filter(Boolean); if (parts.length) return parts[parts.length - 1]; }
            return img.currentSrc || img.src;
          };
          const send = () => window.chrome && window.chrome.webview && window.chrome.webview.postMessage(JSON.stringify(state.keys));
          const draw = () => {
            if (document.body && !layer.isConnected) document.body.appendChild(layer);
            layer.innerHTML = '';
            if (!state.keys.length) return;
            for (const img of document.querySelectorAll('img')) {
              const key = keyOf(bestUrl(img));
              const index = key ? state.keys.indexOf(key) : -1;
              if (index < 0) continue;
              const r = img.getBoundingClientRect();
              if (r.width < 40 || r.bottom < 0 || r.top > innerHeight) continue;
              const frame = document.createElement('div');
              frame.style.cssText = `position:fixed;left:${r.left}px;top:${r.top}px;width:${r.width}px;height:${r.height}px;border:4px solid #E8B923;border-radius:16px;box-sizing:border-box`;
              const badge = document.createElement('div');
              badge.textContent = index + 1;
              badge.style.cssText = `position:fixed;left:${r.left + 10}px;top:${r.top + 10}px;min-width:34px;height:34px;padding:0 6px;border-radius:17px;background:#E8B923;color:#111;font:700 18px/34px Segoe UI,sans-serif;text-align:center;box-shadow:0 2px 8px rgba(0,0,0,.5);box-sizing:border-box`;
              layer.append(frame, badge);
            }
          };
          const pick = e => {
            if (!state.selecting) return;
            const img = document.elementsFromPoint(e.clientX, e.clientY).find(el => el.tagName === 'IMG' && keyOf(bestUrl(el)));
            if (!img) return;
            e.preventDefault(); e.stopPropagation(); e.stopImmediatePropagation();
            if (e.type !== 'click') return;
            const key = keyOf(bestUrl(img));
            const index = state.keys.indexOf(key);
            if (index >= 0) state.keys.splice(index, 1); else state.keys.push(key);
            send(); draw();
          };
          for (const type of ['click', 'auxclick', 'mousedown', 'pointerdown', 'mouseup']) document.addEventListener(type, pick, true);
          addEventListener('scroll', draw, true);
          addEventListener('resize', draw);
          setInterval(draw, 400);
          window.__basilisco = { load: (keys, selecting) => { state.keys = keys; state.selecting = selecting; draw(); } };
        })();
        """;
}
