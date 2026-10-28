using System.Reflection;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using Basilisco.Services;

namespace Basilisco;

/// <summary>
/// La barra de arriba con el mando (y con las flechas): ▲ sube del carrusel a la barra, ◀ ▶ recorren sus botones (el
/// elegido lleva un borde dorado), ▲ ▼ pasan de una fila a otra, Ⓐ pulsa y Ⓑ (o ▼ desde la última fila) vuelve al
/// carrusel. Cada botón lleva en su esquina su tecla o, con el mando, su botón: los que tienen uno directo (Buscar R3,
/// Orden L3, Formato View) lo enseñan siempre y el elegido enseña Ⓐ, con los símbolos y colores del mando que se usa.
/// </summary>
public partial class MainWindow
{
    /// <summary>Un botón de la barra: su tecla, su botón directo del mando (si tiene) y lo que hace.</summary>
    sealed record BarSlot(FrameworkElement Element, string? Key, PadAction? Pad, Action Activate);

    List<BarSlot[]>? _barRows;
    readonly Dictionary<FrameworkElement, ButtonBadge> _badges = [];
    int _barRow = -1;
    FrameworkElement? _barFocus;

    bool InBar => _barRow >= 0;

    /// <summary>Las filas de la barra, de arriba abajo: la del buscador, la de las vistas y acciones y la de los controles del carrusel.</summary>
    List<BarSlot[]> BarRows() => _barRows ??=
    [
        [
            new(SearchField, "Ctrl+F", PadAction.Search, FocusSearch),
            new(EditButton, "Ctrl+E", null, () => Press(EditButton)),
            new(ShortcutsButton, "F1", null, () => Press(ShortcutsButton)),
            new(UpdateButton, "U", null, () => Press(UpdateButton)),
            new(NightButton, "N", null, () => Press(NightButton)),
            new(ThemeButton, "A", null, () => Press(ThemeButton)),
        ],
        [
            new(AllTab, "1", null, () => Press(AllTab)),
            new(ConsolesTab, "2", null, () => Press(ConsolesTab)),
            new(FiltersTab, "3", null, () => Press(FiltersTab)),
            new(PlayersTab, "4", null, () => Press(PlayersTab)),
            new(FamilyTab, "5", null, () => Press(FamilyTab)),
            new(SortTab, "O", PadAction.Sort, () => Press(SortTab)),
            new(FormatButton, "F", PadAction.Format, () => Press(FormatButton)),
            new(ImportMenuButton, "S", null, () => Press(ImportMenuButton)),
            new(ImportFolderButton, "C", null, () => Press(ImportFolderButton)),
            new(ImportGameButton, "I", null, () => Press(ImportGameButton)),
        ],
        [
            new(AlignToggle, "L", null, () => Press(AlignToggle)),
            new(AutoShiftToggle, null, null, () => Press(AutoShiftToggle)),
            new(MetadataButton, "M", null, () => Press(MetadataButton)),
        ],
    ];

    /// <summary>Los botones de una fila que se pueden elegir ahora (los que se ven y están activos).</summary>
    List<BarSlot> Row(int row) => BarRows()[row].Where(slot => slot.Element.IsVisible && slot.Element.IsEnabled).ToList();

    BarSlot? FocusedSlot => _barFocus is null ? null : BarRows().SelectMany(row => row).FirstOrDefault(slot => slot.Element == _barFocus);

    /// <summary>Pulsa un botón como si se hiciera clic (los de dos estados cambian de estado y avisan, como con el ratón).</summary>
    static void Press(ButtonBase button)
    {
        if (!button.IsVisible || !button.IsEnabled)
            return;
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    }

    /// <summary>Del carrusel a la barra: a la fila de justo encima, al botón más cerca del centro.</summary>
    void EnterBar()
    {
        for (var row = BarRows().Count - 1; row >= 0; row--)
        {
            if (Row(row).Count == 0)
                continue;
            _barRow = row;
            FocusBar(Nearest(Row(row), ActualWidth / 2));
            return;
        }
    }

    void ExitBar()
    {
        if (!InBar)
            return;
        _barRow = -1;
        _barFocus = null;
        RefreshButtonBadges();
    }

    void MoveBar(int direction)
    {
        var row = Row(_barRow);
        if (row.Count == 0)
            return;
        var index = row.FindIndex(slot => slot.Element == _barFocus);
        FocusBar(row[Math.Clamp(index < 0 ? 0 : index + direction, 0, row.Count - 1)]);
    }

    /// <summary>A la fila de arriba o de abajo, al botón que quede más cerca; desde la última, abajo vuelve al carrusel.</summary>
    void MoveBarRow(int direction)
    {
        var x = _barFocus is { } focused ? CenterX(focused) : ActualWidth / 2;
        for (var row = _barRow + direction; row >= 0; row += direction)
        {
            if (row >= BarRows().Count)
            {
                ExitBar();
                return;
            }
            if (Row(row).Count == 0)
                continue;
            _barRow = row;
            FocusBar(Nearest(Row(row), x));
            return;
        }
    }

    void ActivateBar()
    {
        if (FocusedSlot is not { } slot)
            return;
        // Al escribir en el buscador ya no se está en la barra.
        if (slot.Element == SearchField)
            ExitBar();
        slot.Activate();
    }

    void FocusBar(BarSlot slot)
    {
        _barFocus = slot.Element;
        RefreshButtonBadges();
    }

    BarSlot Nearest(List<BarSlot> row, double x) => row.MinBy(slot => Math.Abs(CenterX(slot.Element) - x))!;

    double CenterX(FrameworkElement element) =>
        element.IsVisible ? element.TranslatePoint(new Point(element.ActualWidth / 2, 0), this).X : 0;

    // ---- Los distintivos ----

    static readonly Color KeyFill = Color.FromArgb(0xEE, 0xF2, 0xF3, 0xF5), KeyInk = Color.FromRgb(0x1C, 0x1E, 0x22), KeyEdge = Color.FromArgb(0x60, 0, 0, 0);
    static readonly Color PadFill = Color.FromArgb(0xF0, 0x16, 0x18, 0x1D), PadEdge = Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF);

    /// <summary>
    /// Pone a cada botón su distintivo: con el teclado, su tecla; con el mando, su botón directo o, en el elegido de la barra,
    /// el de aceptar (con el símbolo y el color de ese mando: Ⓐ verde en Xbox, ✕ azul en PlayStation…). Se llama al cambiar
    /// entre mando y teclado (o de un mando a otro) y al moverse por la barra.
    /// </summary>
    void RefreshButtonBadges()
    {
        if (!IsLoaded)
            return;
        var style = _gamepad.Style;
        foreach (var slot in BarRows().SelectMany(row => row))
        {
            if (!_badges.TryGetValue(slot.Element, out var badge))
            {
                if (AdornerLayer.GetAdornerLayer(slot.Element) is not { } layer)
                    continue;
                badge = new ButtonBadge(slot.Element);
                layer.Add(badge);
                _badges[slot.Element] = badge;
                slot.Element.IsVisibleChanged += (_, _) => RefreshButtonBadges();
            }
            var focused = InBar && slot.Element == _barFocus;
            BadgeLook? look = null;
            if (!_padMode)
                look = slot.Key is { } key ? new BadgeLook(key, KeyFill, KeyInk, KeyEdge) : null;
            else if (focused)
                look = PadLook(PadAction.Confirm, style);
            else if (slot.Pad is { } pad)
                look = PadLook(pad, style);
            badge.Visibility = slot.Element.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            badge.Set(look, focused);
        }
    }

    /// <summary>El botón del mando con su color: los cuatro de la derecha con el de su símbolo, el resto en blanco.</summary>
    static BadgeLook PadLook(PadAction action, PadStyle style)
    {
        var text = GamepadService.Label(action, style);
        var ink = (action, style) switch
        {
            (PadAction.Confirm, PadStyle.PlayStation) => Color.FromRgb(0x8C, 0xB4, 0xF0),   // ✕ azul
            (PadAction.Back, PadStyle.PlayStation) => Color.FromRgb(0xF0, 0x7C, 0x7C),      // ○ rojo
            (PadAction.Rotate, PadStyle.PlayStation) => Color.FromRgb(0xE8, 0x9A, 0xD6),    // □ rosa
            (PadAction.Customize, PadStyle.PlayStation) => Color.FromRgb(0x5E, 0xD6, 0xB0), // △ verde
            (PadAction.Confirm, PadStyle.Xbox) => Color.FromRgb(0x6C, 0xD0, 0x5A),          // A verde
            (PadAction.Back, PadStyle.Xbox) => Color.FromRgb(0xF0, 0x6A, 0x60),             // B rojo
            (PadAction.Rotate, PadStyle.Xbox) => Color.FromRgb(0x6A, 0xA6, 0xF8),           // X azul
            (PadAction.Customize, PadStyle.Xbox) => Color.FromRgb(0xF6, 0xD2, 0x4A),        // Y amarillo
            _ => Colors.White,
        };
        return new BadgeLook(text, PadFill, ink, PadEdge);
    }
}
