using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Basilisco.Services;

namespace Basilisco.Customization;

/// <summary>
/// Carrusel de capturas o vídeos de la caja abierta: la elegida grande en el centro (como una foto, o con ▶ si es un vídeo)
/// y las de los lados más pequeñas y apagadas, que se deslizan al pasar. Un clic en una de los lados la trae al centro; en
/// la del centro, <see cref="Activated"/>. Debajo, el nombre y el número de la elegida.
/// </summary>
public sealed class MediaCarousel : Grid
{
    /// <summary>Por dónde va, en posiciones (se anima hacia la elegida, así el paso es un deslizamiento).</summary>
    static readonly DependencyProperty PositionProperty = DependencyProperty.Register(nameof(Position), typeof(double), typeof(MediaCarousel),
        new PropertyMetadata(0.0, (d, _) => ((MediaCarousel)d).Arrange()));

    double Position
    {
        get => (double)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    readonly Canvas _canvas = new() { ClipToBounds = true };
    readonly TextBlock _caption = new()
    {
        FontSize = 14, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = new Thickness(10, 6, 10, 0),
    };
    readonly Dictionary<int, Border> _cards = [];
    readonly Dictionary<int, ImageSource?> _posters = [];
    IReadOnlyList<MediaItem> _items = [];
    int _version;

    /// <summary>Las capturas van con el borde blanco de una foto; los vídeos, con ▶ encima.</summary>
    public bool Photo { get; init; }

    /// <summary>Carga la imagen de una (la descarga si hace falta). La pone quien usa el carrusel.</summary>
    public Func<MediaItem, Task<ImageSource?>>? LoadPoster { get; set; }

    public int SelectedIndex { get; private set; }

    public MediaItem? SelectedItem => SelectedIndex >= 0 && SelectedIndex < _items.Count ? _items[SelectedIndex] : null;

    /// <summary>Ha cambiado la elegida.</summary>
    public event Action<int>? SelectionChanged;

    /// <summary>Clic (o Enter) en la del centro: verla en grande o poner el vídeo.</summary>
    public event Action<MediaItem>? Activated;

    public MediaCarousel()
    {
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(_canvas);
        SetRow(_caption, 1);
        Children.Add(_caption);
        _canvas.SizeChanged += (_, _) => Arrange();
    }

    /// <summary>El rectángulo de la del centro, dentro del carrusel (para poner el reproductor encima).</summary>
    public Rect CenterRect
    {
        get
        {
            var (width, height) = CardSize();
            return new Rect((_canvas.ActualWidth - width) / 2, (_canvas.ActualHeight - height) / 2, width, height);
        }
    }

    public void SetItems(IReadOnlyList<MediaItem> items, int selected = 0)
    {
        _version++;
        _items = items;
        _cards.Clear();
        _posters.Clear();
        _canvas.Children.Clear();
        SelectedIndex = items.Count == 0 ? -1 : Math.Clamp(selected, 0, items.Count - 1);
        BeginAnimation(PositionProperty, null);
        Position = Math.Max(0, SelectedIndex);
        Arrange();
        UpdateCaption();
        SelectionChanged?.Invoke(SelectedIndex);
    }

    /// <summary>Pasa a la siguiente (1) o a la anterior (−1), dando la vuelta al final.</summary>
    public void Move(int direction)
    {
        if (_items.Count > 1)
            Select((SelectedIndex + direction + _items.Count) % _items.Count);
    }

    public void Select(int index)
    {
        if (index < 0 || index >= _items.Count || index == SelectedIndex)
            return;
        SelectedIndex = index;
        // Al dar la vuelta (de la última a la primera) no se cruzan todas: se salta directo.
        if (Math.Abs(index - Position) > 3)
            Position = index;
        BeginAnimation(PositionProperty, new DoubleAnimation(index, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        UpdateCaption();
        SelectionChanged?.Invoke(index);
    }

    public void Activate()
    {
        if (SelectedItem is { } item)
            Activated?.Invoke(item);
    }

    void UpdateCaption()
    {
        if (SelectedItem is not { } item)
        {
            _caption.Text = "";
            return;
        }
        var detail = item.Detail is { Length: > 0 } text ? $"  ·  {text}" : "";
        _caption.Text = $"{item.Title}{detail}     {SelectedIndex + 1} / {_items.Count}";
    }

    (double Width, double Height) CardSize()
    {
        double width = _canvas.ActualWidth, height = _canvas.ActualHeight;
        // 16:9, lo más grande que quepa dejando ver un trozo de las de los lados.
        var cardHeight = Math.Max(40, Math.Min(height - 16, width * 0.72 * 9 / 16));
        return (cardHeight * 16 / 9, cardHeight);
    }

    /// <summary>Coloca las de alrededor de la posición actual (dos a cada lado); las demás se quitan.</summary>
    void Arrange()
    {
        if (_canvas.ActualWidth < 10 || _items.Count == 0)
            return;
        var (width, height) = CardSize();
        var spacing = width * 0.62;
        var position = Position;
        var first = Math.Max(0, (int)Math.Floor(position - 2.6));
        var last = Math.Min(_items.Count - 1, (int)Math.Ceiling(position + 2.6));
        foreach (var index in _cards.Keys.Where(index => index < first || index > last).ToList())
        {
            _canvas.Children.Remove(_cards[index]);
            _cards.Remove(index);
        }
        for (var index = first; index <= last; index++)
        {
            var card = _cards.TryGetValue(index, out var existing) ? existing : CreateCard(index);
            card.Width = width;
            card.Height = height;
            var distance = index - position;
            var far = Math.Min(Math.Abs(distance), 2);
            var scale = 1 - 0.26 * far;
            ((ScaleTransform)card.RenderTransform).ScaleX = ((ScaleTransform)card.RenderTransform).ScaleY = scale;
            Canvas.SetLeft(card, _canvas.ActualWidth / 2 + distance * spacing - width / 2);
            Canvas.SetTop(card, (_canvas.ActualHeight - height) / 2);
            card.Opacity = 1 - 0.38 * far;
            Panel.SetZIndex(card, 100 - (int)(far * 20));
            if (card.Child is Grid { Children.Count: > 2 } content)
                content.Children[2].Visibility = Math.Abs(distance) < 0.5 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    Border CreateCard(int index)
    {
        var item = _items[index];
        var image = new Image { Stretch = Stretch.Uniform };
        var content = new Grid { Background = Brushes.Black };
        content.Children.Add(image);
        // Mientras se carga, su nombre.
        content.Children.Add(new TextBlock
        {
            Text = item.Title, Foreground = new SolidColorBrush(Color.FromRgb(0x8C, 0x95, 0xA3)), FontSize = 13, TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12),
        });
        if (item.IsVideo)
        {
            content.Children.Add(new Border
            {
                Width = 74, Height = 74, CornerRadius = new CornerRadius(37), Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0B, 0x0D, 0x11)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
                Child = new TextBlock { Text = "▶", FontSize = 30, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 4) },
            });
        }
        var card = new Border
        {
            Child = content, Cursor = Cursors.Hand, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(),
            Background = Photo ? new SolidColorBrush(Color.FromRgb(0xF4, 0xF1, 0xEA)) : Brushes.Black,
            Padding = Photo ? new Thickness(7) : new Thickness(0), CornerRadius = new CornerRadius(Photo ? 2 : 6),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 5, Direction = 270, Opacity = 0.55 },
        };
        card.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (index == SelectedIndex)
                Activated?.Invoke(item);
            else
                Select(index);
        };
        _cards[index] = card;
        _canvas.Children.Add(card);
        if (_posters.TryGetValue(index, out var cached))
            image.Source = cached;
        else
            _ = LoadAsync(index, item, image, content);
        return card;
    }

    async Task LoadAsync(int index, MediaItem item, Image image, Grid content)
    {
        var version = _version;
        var poster = LoadPoster is null ? null : await LoadPoster(item);
        if (version != _version)
            return;
        _posters[index] = poster;
        image.Source = poster;
        if (poster is not null && content.Children.Count > 1)
            content.Children[1].Visibility = Visibility.Collapsed;
    }
}
