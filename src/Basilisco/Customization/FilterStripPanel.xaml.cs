using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Basilisco.Carousel;

namespace Basilisco.Customization;

/// <summary>Una casilla del menú de filtros.</summary>
/// <param name="ImagePath">Logotipo del filtro; sin él se escribe el nombre en blanco.</param>
/// <param name="Caption">Texto pequeño debajo (el nombre y cuántos juegos tiene).</param>
/// <param name="Selected">Es el filtro que se está viendo.</param>
/// <param name="Choose">Lo que pasa al elegirla.</param>
/// <param name="Edit">Lo que pasa al elegirla para editar (null: no se edita, como «Todos los filtros»).</param>
/// <param name="IsAction">Una acción («Nuevo filtro», «Editar filtro») en lugar de un filtro.</param>
/// <param name="KeepOpen">Al elegirla, el menú sigue abierto (como «Editar filtro», que pasa a elegir qué filtro editar).</param>
public sealed record FilterTile(string Title, string? ImagePath, string? Caption, bool Selected, Action Choose, Action? Edit = null,
                                bool IsAction = false, bool KeepOpen = false);

/// <summary>
/// Menú de filtros en una franja negra horizontal bajo la barra de arriba: cada filtro con su logotipo en grande (o su
/// nombre en blanco si aún no tiene), un borde dorado al pasar por encima, y «Nuevo filtro» y «Editar filtro» al final.
/// Se maneja con el ratón (y la rueda), las flechas o el mando.
/// </summary>
public partial class FilterStripPanel : UserControl
{
    static readonly Brush Gold = Frozen(Color.FromRgb(0xE8, 0xB9, 0x23));
    static readonly Brush TileBack = Frozen(Color.FromRgb(0x13, 0x15, 0x1A));
    static readonly Brush TileEdge = Frozen(Color.FromRgb(0x2A, 0x2E, 0x36));

    readonly List<(FilterTile Tile, Border Box)> _tiles = [];
    int _current;
    bool _editing;
    bool _padMode;
    string _title = "FILTROS", _editTitle = "";

    public event Action? Closed;

    public bool IsOpen => Visibility == Visibility.Visible;

    public FilterStripPanel()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    /// <param name="top">Dónde empieza la franja (justo debajo de la barra de arriba), en las unidades de este panel.</param>
    /// <param name="title">Título de la franja («FILTROS», «CONSOLAS»).</param>
    /// <param name="editTitle">Título mientras se elige qué editar.</param>
    public void Open(IReadOnlyList<FilterTile> tiles, double top, bool padMode, string title = "FILTROS",
                     string editTitle = "ELIGE EL FILTRO QUE QUIERES EDITAR")
    {
        _title = title;
        _editTitle = editTitle;
        Strip.Margin = new Thickness(0, top, 0, 0);
        _padMode = padMode;
        _editing = false;
        Build(tiles);
        Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(() =>
        {
            Focus();
            _tiles[_current].Box.BringIntoView();
        }, DispatcherPriority.Input);
    }

    void Build(IReadOnlyList<FilterTile> tiles)
    {
        Tiles.Children.Clear();
        _tiles.Clear();
        foreach (var tile in tiles)
        {
            var box = CreateTile(tile);
            Tiles.Children.Add(box);
            _tiles.Add((tile, box));
        }
        _current = Math.Max(0, tiles.ToList().FindIndex(tile => tile.Selected));
        UpdateHint();
        Highlight();
    }

    Border CreateTile(FilterTile tile)
    {
        // El logotipo, lo más grande que quepa; sin logotipo, el nombre en blanco.
        UIElement content;
        if (tile.ImagePath is not null && CoverArt.LoadImage(tile.ImagePath, 640) is { } logo)
        {
            content = new Image { Source = logo, Stretch = Stretch.Uniform, Margin = new Thickness(10), SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(content, BitmapScalingMode.HighQuality);
        }
        else
        {
            content = new TextBlock
            {
                Text = tile.Title,
                Foreground = tile.IsAction ? Gold : Brushes.White,
                FontSize = tile.IsAction ? 22 : 26,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(14),
                MaxHeight = 110,
            };
        }

        var face = new Border
        {
            Width = 300,
            Height = 150,
            CornerRadius = new CornerRadius(14),
            Background = TileBack,
            BorderBrush = TileEdge,
            BorderThickness = new Thickness(3),
            Child = content,
        };
        var caption = new TextBlock
        {
            Text = tile.Caption ?? "",
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xB2)),
            FontSize = 14,
            Margin = new Thickness(4, 8, 4, 0),
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 300,
        };
        var stack = new StackPanel();
        stack.Children.Add(face);
        stack.Children.Add(caption);

        var box = new Border { Child = stack, Margin = new Thickness(0, 0, 18, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent, Tag = face };
        box.MouseEnter += (_, _) =>
        {
            _current = _tiles.FindIndex(entry => entry.Box == box);
            Highlight();
        };
        box.MouseLeftButtonUp += (_, e) =>
        {
            _current = _tiles.FindIndex(entry => entry.Box == box);
            Choose();
            e.Handled = true;
        };
        // Clic derecho en un filtro: editarlo directamente.
        box.MouseRightButtonUp += (_, e) =>
        {
            if (tile.Edit is { } edit)
            {
                Close();
                edit();
            }
            e.Handled = true;
        };
        return box;
    }

    /// <summary>Borde dorado (con brillo) en la casilla actual; el filtro que se está viendo lleva el borde dorado fino.</summary>
    void Highlight()
    {
        for (var i = 0; i < _tiles.Count; i++)
        {
            var (tile, box) = _tiles[i];
            var face = (Border)box.Tag;
            var current = i == _current;
            var dimmed = _editing && tile.Edit is null;
            face.BorderBrush = current ? Gold : tile.Selected ? new SolidColorBrush(Color.FromArgb(150, 0xE8, 0xB9, 0x23)) : TileEdge;
            face.Effect = current ? new DropShadowEffect { Color = Color.FromRgb(0xE8, 0xB9, 0x23), BlurRadius = 26, ShadowDepth = 0, Opacity = 0.75 } : null;
            box.Opacity = dimmed ? 0.35 : 1;
        }
    }

    void UpdateHint()
    {
        TitleText.Text = _editing ? _editTitle : _title;
        HintText.Text = _padMode
            ? "← →  Elegir     ·     A  Aceptar     ·     B  Cerrar"
            : "← →  Elegir     ·     Enter  Aceptar     ·     Clic derecho  Editar     ·     Esc  Cerrar";
    }

    public void Move(int direction)
    {
        if (_tiles.Count == 0)
            return;
        var next = _current;
        // En modo editar se saltan las casillas que no se pueden editar.
        do
            next = Math.Clamp(next + direction, 0, _tiles.Count - 1);
        while (_editing && _tiles[next].Tile.Edit is null && next > 0 && next < _tiles.Count - 1);
        _current = next;
        Highlight();
        _tiles[_current].Box.BringIntoView();
    }

    public void Choose()
    {
        if (_current < 0 || _current >= _tiles.Count)
            return;
        var tile = _tiles[_current].Tile;
        if (_editing)
        {
            if (tile.Edit is not { } edit)
                return;
            Close();
            edit();
            return;
        }
        if (!tile.KeepOpen)
            Close();
        tile.Choose();
    }

    /// <summary>«Editar filtro»: las casillas siguientes abren el editor del filtro elegido en vez de seleccionarlo.</summary>
    public void StartEditing()
    {
        _editing = true;
        _current = Math.Max(0, _tiles.FindIndex(entry => entry.Tile.Edit is not null));
        UpdateHint();
        Highlight();
        _tiles[_current].Box.BringIntoView();
    }

    public void Close()
    {
        if (!IsOpen)
            return;
        Visibility = Visibility.Collapsed;
        Tiles.Children.Clear();
        _tiles.Clear();
        Closed?.Invoke();
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: Move(-1); break;
            case Key.Right: Move(1); break;
            case Key.Home: Move(-_tiles.Count); break;
            case Key.End: Move(_tiles.Count); break;
            case Key.Enter or Key.Space: Choose(); break;
            case Key.Escape or Key.Back:
                if (_editing)
                {
                    _editing = false;
                    UpdateHint();
                    Highlight();
                }
                else
                {
                    Close();
                }
                break;
            default: return;
        }
        e.Handled = true;
    }

    // La rueda del ratón pasa las casillas de lado.
    void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    // Clic fuera de la franja: se cierra.
    void OnBackdropMouseDown(object sender, MouseButtonEventArgs e) => Close();

    void OnStripMouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
