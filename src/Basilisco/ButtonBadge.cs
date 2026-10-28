using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Basilisco;

/// <summary>Cómo se dibuja un distintivo: una tecla, un botón del mando (con el color de su símbolo) o nada.</summary>
public sealed record BadgeLook(string Text, Color Fill, Color Ink, Color Edge);

/// <summary>
/// El distintivo de un botón de la barra de arriba: en su esquina superior derecha, su tecla (con el teclado) o su botón
/// del mando (con el símbolo y el color del mando que se está usando). Con el mando, el botón elegido en la barra se
/// rodea con un borde dorado. No recibe clics: los deja pasar al botón.
/// </summary>
public sealed class ButtonBadge : Adorner
{
    static readonly Pen FocusPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xE8, 0xB9, 0x23)), 2.5));
    static readonly Brush FocusGlow = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0xE8, 0xB9, 0x23)));
    static readonly Typeface Face = new(new FontFamily("Segoe UI Semibold, Segoe UI Symbol"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    BadgeLook? _look;
    bool _focused;

    public ButtonBadge(UIElement element) : base(element)
    {
        IsHitTestVisible = false;
    }

    public void Set(BadgeLook? look, bool focused)
    {
        if (_look == look && _focused == focused)
            return;
        _look = look;
        _focused = focused;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = AdornedElement.RenderSize;
        if (size.Width <= 0 || size.Height <= 0)
            return;
        if (_focused)
        {
            var ring = new Rect(-4, -4, size.Width + 8, size.Height + 8);
            dc.DrawRoundedRectangle(FocusGlow, FocusPen, ring, 9, 9);
        }
        if (_look is not { } look)
            return;
        var text = new FormattedText(look.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 11, new SolidColorBrush(look.Ink),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        const double height = 18;
        var width = Math.Max(height, text.Width + 10);
        var box = new Rect(size.Width - width + 7, -height / 2 + 1, width, height);
        dc.DrawRoundedRectangle(new SolidColorBrush(look.Fill), new Pen(new SolidColorBrush(look.Edge), 1), box, height / 2, height / 2);
        dc.DrawText(text, new Point(box.X + (box.Width - text.Width) / 2, box.Y + (box.Height - text.Height) / 2));
    }

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
