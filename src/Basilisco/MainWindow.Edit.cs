using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Basilisco;

/// <summary>
/// Modo edición («Editar», arriba a la derecha, o Ctrl+E): los botones, el buscador, el título del juego y sus datos (los
/// cilindros grises) se arrastran con el ratón a donde quieras. Cada uno se mueve por su cuenta, se guarda (en
/// settings.json) y se ve igual al volver a abrir Basilisco. Clic derecho en uno lo devuelve a su sitio; «Restablecer
/// todo», todos. Mientras se edita, los botones no hacen nada al pulsarlos: solo se mueven.
/// </summary>
public partial class MainWindow
{
    bool _editing;
    FrameworkElement? _moving;
    Point _moveStart;
    Vector _moveFrom;
    readonly Dictionary<FrameworkElement, EditFrame> _editFrames = [];

    /// <summary>Lo que se puede mover, por su nombre (así se guarda).</summary>
    IEnumerable<FrameworkElement> Movables() =>
    [
        LogoPanel, SearchField, EditButton, ShortcutsButton, UpdateButton, NightButton, ThemeButton,
        AllTab, ConsolesTab, FiltersTab, PlayersTab, FamilyTab, SortTab, FormatButton, ImportMenuButton, ImportFolderButton, ImportGameButton,
        AlignToggle, ShiftGroup, AutoShiftToggle, MetadataButton, ZoomGroup,
        TitleText, Chips, AchievementsPanel,
    ];

    /// <summary>Al abrir: cada cosa en el sitio donde la dejaste, y el arrastre preparado (solo hace algo en modo edición).</summary>
    void InitEditMode()
    {
        foreach (var element in Movables())
        {
            if (_settings.UiOffsets.TryGetValue(element.Name, out var offset) && offset.Length == 2)
                Offset(element, new Vector(offset[0], offset[1]));
            element.PreviewMouseLeftButtonDown += OnMovableMouseDown;
            element.PreviewMouseMove += OnMovableMouseMove;
            element.PreviewMouseLeftButtonUp += OnMovableMouseUp;
            element.PreviewMouseRightButtonUp += OnMovableRightClick;
        }
    }

    void OnEditClick(object sender, RoutedEventArgs e) => SetEditMode(EditButton.IsChecked == true);

    void OnEditDoneClick(object sender, RoutedEventArgs e) => SetEditMode(false);

    void OnEditResetClick(object sender, RoutedEventArgs e)
    {
        foreach (var element in Movables())
            Offset(element, default);
        _settings.UiOffsets.Clear();
        _settings.Save();
        ShowToast("Todo ha vuelto a su sitio");
    }

    void SetEditMode(bool on)
    {
        _editing = on;
        EditButton.IsChecked = on;
        EditBanner.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        ExitBar();
        // El título y sus datos no reciben el ratón normalmente (para no tapar las cajas); al editar, sí.
        TitlePanel.IsHitTestVisible = on;
        foreach (var element in Movables())
        {
            if (on && !_editFrames.ContainsKey(element) && AdornerLayer.GetAdornerLayer(element) is { } layer)
            {
                var frame = new EditFrame(element);
                layer.Add(frame);
                _editFrames[element] = frame;
            }
            if (_editFrames.TryGetValue(element, out var existing))
                existing.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            element.Cursor = on ? Cursors.SizeAll : null;
        }
        // El cursor de mano de la barra de logros vuelve al terminar.
        if (!on)
            AchievementsPanel.Cursor = Cursors.Hand;
    }

    static Vector OffsetOf(FrameworkElement element) =>
        element.RenderTransform is TranslateTransform move ? new Vector(move.X, move.Y) : default;

    static void Offset(FrameworkElement element, Vector offset) =>
        element.RenderTransform = offset == default ? Transform.Identity : new TranslateTransform(offset.X, offset.Y);

    /// <summary>Las coordenadas en las que se mueve (las de su contenedor, antes de su desplazamiento).</summary>
    static IInputElement Space(FrameworkElement element) => VisualTreeHelper.GetParent(element) as IInputElement ?? element;

    void OnMovableMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_editing || sender is not FrameworkElement element)
            return;
        // Al editar, pulsar no hace lo del botón: empieza a moverlo.
        e.Handled = true;
        _moving = element;
        _moveStart = e.GetPosition(Space(element));
        _moveFrom = OffsetOf(element);
        element.CaptureMouse();
    }

    void OnMovableMouseMove(object sender, MouseEventArgs e)
    {
        if (_moving is not { } element || sender != element)
            return;
        Offset(element, _moveFrom + (e.GetPosition(Space(element)) - _moveStart));
        e.Handled = true;
    }

    void OnMovableMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_editing)
            return;
        e.Handled = true;
        if (_moving is not { } element)
            return;
        _moving = null;
        element.ReleaseMouseCapture();
        Remember(element);
    }

    void OnMovableRightClick(object sender, MouseButtonEventArgs e)
    {
        if (!_editing || sender is not FrameworkElement element)
            return;
        e.Handled = true;
        Offset(element, default);
        Remember(element);
    }

    void Remember(FrameworkElement element)
    {
        var offset = OffsetOf(element);
        if (offset == default)
            _settings.UiOffsets.Remove(element.Name);
        else
            _settings.UiOffsets[element.Name] = [Math.Round(offset.X, 1), Math.Round(offset.Y, 1)];
        _settings.Save();
    }

    /// <summary>En el modo edición, el contorno discontinuo dorado de lo que se puede mover.</summary>
    sealed class EditFrame : Adorner
    {
        static readonly Pen Dashed = CreatePen();
        static readonly Brush Tint = Frozen(new SolidColorBrush(Color.FromArgb(0x22, 0xE8, 0xB9, 0x23)));

        public EditFrame(UIElement element) : base(element)
        {
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var size = AdornedElement.RenderSize;
            if (size.Width > 0 && size.Height > 0)
                dc.DrawRoundedRectangle(Tint, Dashed, new Rect(-3, -3, size.Width + 6, size.Height + 6), 7, 7);
        }

        static Pen CreatePen()
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0xE8, 0xB9, 0x23)), 1.5) { DashStyle = new DashStyle([4, 3], 0) };
            pen.Freeze();
            return pen;
        }

        static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
