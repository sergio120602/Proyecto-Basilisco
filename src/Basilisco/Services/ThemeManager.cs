using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Basilisco.Services;

/// <summary>Modo de color de la interfaz.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ColorMode>))]
public enum ColorMode { System, Light, Dark }

/// <summary>
/// Modo claro y modo nocturno. Los colores de la interfaz son recursos con nombre («CardBrush», «TextBrush»…) que
/// toda la interfaz usa con DynamicResource; cambiar de modo es cambiar esos recursos, y todo se repinta solo.
/// </summary>
public static class ThemeManager
{
    sealed record Palette(
        Color Window, Color Card, Color Text, Color Muted, Color Line, Color Pill, Color PillEdge, Color Toggle,
        Color FieldEdge, Color Field, Color Chip, Color Error,
        Color[] Scene, Color[] Floor, Color[] Preview);

    static readonly Palette Light = new(
        Window: Hex("#D3D8E0"), Card: Hex("#F4F6F9"), Text: Hex("#1F2630"), Muted: Hex("#5B6573"), Line: Hex("#DCE0E7"),
        Pill: Hex("#EEF1F5"), PillEdge: Colors.White, Toggle: Hex("#E3E7ED"), FieldEdge: Hex("#C9CFD8"), Field: Colors.White,
        Chip: Hex("#8D97A6"), Error: Hex("#C0392B"),
        Scene: [Hex("#BFC6D1"), Hex("#DCE0E7"), Hex("#EDEFF3")],
        Floor: [Hex("#00C2C9D3"), Hex("#C2C9D3"), Hex("#D6DAE1"), Hex("#E3E6EB")],
        Preview: [Hex("#CDD3DC"), Hex("#E6E9EE"), Hex("#D3D8E0"), Hex("#E3E6EB")]);

    static readonly Palette Dark = new(
        Window: Hex("#12151A"), Card: Hex("#1C2129"), Text: Hex("#E6EAF0"), Muted: Hex("#9AA4B2"), Line: Hex("#313844"),
        Pill: Hex("#2A303A"), PillEdge: Hex("#3A4250"), Toggle: Hex("#262C35"), FieldEdge: Hex("#3A4250"), Field: Hex("#14181E"),
        Chip: Hex("#3A4352"), Error: Hex("#FF7A6B"),
        Scene: [Hex("#0B0D11"), Hex("#151920"), Hex("#1B2028")],
        Floor: [Hex("#00151920"), Hex("#151920"), Hex("#1A1F27"), Hex("#20252E")],
        Preview: [Hex("#161A20"), Hex("#22272F"), Hex("#191D23"), Hex("#20252D")]);

    /// <summary>True si ahora mismo se ve el modo nocturno.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>Color de las letras «automático» (el de siempre) en el modo actual.</summary>
    public static Color DefaultText => (IsDark ? Dark : Light).Text;

    public static event Action? Changed;

    /// <summary>True si Windows está en modo oscuro (para «Como Windows»).</summary>
    public static bool SystemIsDark()
    {
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return false;
        }
    }

    public static bool Resolve(ColorMode mode) => mode switch
    {
        ColorMode.Dark => true,
        ColorMode.Light => false,
        _ => SystemIsDark(),
    };

    /// <summary>Pone los colores del modo pedido en los recursos de la aplicación.</summary>
    public static void Apply(ColorMode mode)
    {
        var dark = Resolve(mode);
        var palette = dark ? Dark : Light;
        var resources = Application.Current.Resources;

        void Brush(string key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[key] = brush;
        }

        Brush("WindowBrush", palette.Window);
        Brush("CardBrush", palette.Card);
        Brush("TextBrush", palette.Text);
        Brush("MutedBrush", palette.Muted);
        Brush("LineBrush", palette.Line);
        Brush("PillBrush", palette.Pill);
        Brush("PillEdgeBrush", palette.PillEdge);
        Brush("ToggleBrush", palette.Toggle);
        Brush("FieldEdgeBrush", palette.FieldEdge);
        Brush("FieldBrush", palette.Field);
        Brush("ChipBrush", palette.Chip);
        Brush("ErrorBrush", palette.Error);
        resources["SceneBrush"] = Gradient(palette.Scene, [0, 0.4, 0.75]);
        resources["FloorBrush"] = Gradient(palette.Floor, [0, 0.12, 0.5, 1]);
        resources["PreviewBrush"] = Gradient(palette.Preview, [0, 0.79, 0.80, 1]);

        var changed = dark != IsDark;
        IsDark = dark;
        foreach (Window window in Application.Current.Windows)
            SetTitleBar(window, dark);
        if (changed)
            Changed?.Invoke();
    }

    static LinearGradientBrush Gradient(Color[] colors, double[] offsets)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        for (var i = 0; i < colors.Length; i++)
            brush.GradientStops.Add(new GradientStop(colors[i], offsets[i]));
        brush.Freeze();
        return brush;
    }

    /// <summary>La barra de título de Windows en oscuro o en claro, a juego.</summary>
    public static void SetTitleBar(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;
        var value = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, ImmersiveDarkMode, ref value, sizeof(int));
    }

    const int ImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
