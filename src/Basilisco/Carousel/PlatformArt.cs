using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Basilisco.Carousel;

/// <summary>
/// Estilo de caja de cada consola: la franja de la izquierda de la portada y el color del plástico
/// (cantos, lomo y contraportada). Son recreaciones dibujadas a mano con los colores y la disposición
/// de las cajas reales; no se usan los logotipos oficiales.
/// </summary>
public static class PlatformArt
{
    static readonly Color DefaultPlastic = Color.FromRgb(0x17, 0x18, 0x1C);

    /// <summary>Color del plástico de la caja de la consola.</summary>
    public static Color Plastic(string? platform) => platform switch
    {
        "ps1" => Color.FromRgb(0x12, 0x12, 0x14),
        "ps2" or "ps3" => Color.FromRgb(0x0E, 0x0E, 0x10),
        "psp" => Color.FromRgb(0x14, 0x14, 0x16),
        "xbox" => Color.FromRgb(0x10, 0x13, 0x10),
        "x360" => Color.FromRgb(0x5D, 0xAA, 0x2E),
        "gb" or "gba" => Color.FromRgb(0xE9, 0xE7, 0xE1),
        "n64" => Color.FromRgb(0x1A, 0x1A, 0x1C),
        "gc" => Color.FromRgb(0x14, 0x12, 0x1A),
        "wii" => Color.FromRgb(0xF3, 0xF3, 0xF3),
        "wiiu" => Color.FromRgb(0x2E, 0x9F, 0xD0),
        "nds" => Color.FromRgb(0xD8, 0xD9, 0xDB),
        "pc" => Color.FromRgb(0x18, 0x18, 0x1A),
        _ => DefaultPlastic,
    };

    /// <summary>True si el plástico es claro (entonces los textos encima van en oscuro).</summary>
    public static bool IsLight(Color color) => (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255 > 0.6;

    /// <summary>Color de texto que se lee bien sobre el plástico de la consola.</summary>
    public static Color TextOnPlastic(string? platform) =>
        IsLight(Plastic(platform)) ? Color.FromArgb(200, 0x33, 0x33, 0x33) : Color.FromArgb(170, 255, 255, 255);

    static readonly Dictionary<string, BitmapSource?> BandImages = [];

    /// <summary>
    /// Imagen de la franja de la consola en basilisco-data/borders/&lt;consola&gt;.png, si el usuario la tiene.
    /// Se carga una vez y se reutiliza para todas las cajas.
    /// </summary>
    public static BitmapSource? BandImage(string? platform)
    {
        if (platform is null)
            return null;
        if (BandImages.TryGetValue(platform, out var cached))
            return cached;

        var path = System.IO.Path.Combine(AppInfo.BordersDirectory, platform + ".png");
        var image = System.IO.File.Exists(path) ? CoverArt.LoadImage(path) : null;
        BandImages[platform] = image;
        return image;
    }

    static readonly Dictionary<string, BitmapSource?> TopBandImages = [];

    /// <summary>Franja de arriba para las cajas rectangulares, en basilisco-data/borders/&lt;consola&gt;-top.png.</summary>
    public static BitmapSource? TopBandImage(string? platform)
    {
        if (platform is null)
            return null;
        if (TopBandImages.TryGetValue(platform, out var cached))
            return cached;

        var path = System.IO.Path.Combine(AppInfo.BordersDirectory, platform + "-top.png");
        var image = System.IO.File.Exists(path) ? CoverArt.LoadImage(path) : null;
        TopBandImages[platform] = image;
        return image;
    }

    /// <summary>
    /// Franja de arriba de las cajas rectangulares, dibujada por Basilisco: el color de la consola y su nombre
    /// en horizontal a la izquierda, como en las cajas modernas.
    /// </summary>
    public static void DrawTopBand(DrawingContext dc, string? platform, Rect band)
    {
        var (background, ink, label, face) = platform switch
        {
            "ps1" => ((Brush)Solid(0x08, 0x08, 0x08), White, "PlayStation", Face("Segoe UI", FontWeights.SemiBold)),
            "ps2" => (Solid(0x05, 0x05, 0x06), White, "PlayStation®2", Face("Segoe UI", FontWeights.Normal)),
            "ps3" => (Gradient(0x00, 0x00, 0x00, 0x30, 0x30, 0x34), White, "PS3", Face("Segoe UI", FontWeights.Light)),
            "psp" => (Solid(0x0A, 0x0A, 0x0B), White, "PSP", Face("Segoe UI", FontWeights.Black, FontStyles.Italic)),
            "xbox" => (Solid(0x0A, 0x0C, 0x0A), new SolidColorBrush(Color.FromRgb(0x7F, 0xBA, 0x00)), "XBOX", Face("Segoe UI", FontWeights.Black)),
            "x360" => (Gradient(0x4C, 0xA8, 0x23, 0x9A, 0xD3, 0x5A), White, "XBOX 360", Face("Segoe UI", FontWeights.Light)),
            "gb" => (Solid(0xB4, 0xB4, 0xB8), new SolidColorBrush(Color.FromRgb(0x3B, 0x3F, 0xA8)), "GAME BOY", Face("Segoe UI", FontWeights.Black, FontStyles.Italic)),
            "gba" => (Gradient(0x3E, 0x39, 0x9E, 0x26, 0x22, 0x6E), White, "GAME BOY ADVANCE", Face("Segoe UI", FontWeights.Black, FontStyles.Italic)),
            "n64" => (Solid(0x10, 0x10, 0x12), White, "NINTENDO 64", Face("Segoe UI", FontWeights.Black)),
            "gc" => (Gradient(0x0A, 0x0A, 0x0C, 0x3B, 0x2C, 0x75), White, "NINTENDO GAMECUBE", Face("Segoe UI", FontWeights.Bold)),
            "wii" => (Solid(0xFF, 0xFF, 0xFF), Gray(0x8E), "Wii", Face("Segoe UI", FontWeights.SemiBold)),
            "wiiu" => (Gradient(0x00, 0xAA, 0xDA, 0x00, 0x84, 0xB4), White, "Wii U", Face("Segoe UI", FontWeights.SemiBold)),
            "nds" => (Solid(0xF0, 0xF1, 0xF2), Gray(0x3C), "NINTENDO DS", Face("Segoe UI", FontWeights.Black)),
            "pc" => (Solid(0x1A, 0x1A, 0x1C), White, "PC", Face("Segoe UI", FontWeights.Black)),
            _ => (Solid(0x0B, 0x0B, 0x0D), White, "", Face("Segoe UI", FontWeights.SemiBold)),
        };

        dc.DrawRectangle(background, null, band);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), null, new Rect(band.Left, band.Bottom, band.Width, 2));
        if (label.Length == 0)
            return;

        // Las letras ocupan el 55 % del alto de la franja, a la izquierda.
        const double probe = 100;
        var size = probe * band.Height * 0.55 / Math.Max(1, Text(label, probe, ink, face).Extent);
        var text = Text(label, size, ink, face);
        // En cajas estrechas (Game Boy Advance, DS…) los nombres largos se reducen hasta caber en el ancho.
        var room = band.Width - band.Height * 0.8;
        if (text.Width > room)
        {
            size *= room / text.Width;
            text = Text(label, size, ink, face);
        }
        var inkTop = text.Height + text.OverhangAfter - text.Extent;
        dc.DrawText(text, new Point(band.Left + band.Height * 0.4, band.Top + band.Height / 2 - (inkTop + text.Extent / 2)));
    }

    static Brush Solid(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));

    static Brush Gradient(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2) =>
        new LinearGradientBrush(Color.FromRgb(r1, g1, b1), Color.FromRgb(r2, g2, b2), 0);

    /// <summary>Pinta la franja de la izquierda de la portada, con el estilo de la consola.</summary>
    public static void DrawBand(DrawingContext dc, string? platform, Rect band)
    {
        switch (platform)
        {
            case "ps1": DrawPs1(dc, band); break;
            case "ps2": DrawPs2(dc, band); break;
            case "ps3": DrawPs3(dc, band); break;
            case "psp": DrawPsp(dc, band); break;
            case "xbox": DrawXbox(dc, band); break;
            case "x360": DrawXbox360(dc, band); break;
            case "gb": DrawGb(dc, band); break;
            case "gba": DrawGba(dc, band); break;
            case "n64": DrawN64(dc, band); break;
            case "gc": DrawGameCube(dc, band); break;
            case "wii": DrawWii(dc, band); break;
            case "wiiu": DrawWiiU(dc, band); break;
            case "nds": DrawNds(dc, band); break;
            case "pc": DrawPc(dc, band); break;
            default: DrawDefault(dc, band); break;
        }
    }

    static void DrawDefault(DrawingContext dc, Rect band)
    {
        Fill(dc, band, Color.FromRgb(0x0B, 0x0B, 0x0D));
        Separator(dc, band, Color.FromRgb(0x2B, 0x2D, 0x33));
    }

    // PS1: franja negra con el logo arriba y «PlayStation» en vertical, como las cajas americanas.
    static void DrawPs1(DrawingContext dc, Rect band)
    {
        Fill(dc, band, Color.FromRgb(0x08, 0x08, 0x08));
        var logo = LogoArea(band);
        DrawPsMark(dc, logo, colorful: true);
        VerticalText(dc, "PlayStation", TextArea(band, logo.Bottom + band.Width * 0.25, band.Width * 0.35), White, Face("Segoe UI", FontWeights.SemiBold));
        Separator(dc, band, Color.FromRgb(0x30, 0x30, 0x34));
    }

    // PS2: franja negra con el logo pequeño y «PlayStation 2» fino en vertical.
    static void DrawPs2(DrawingContext dc, Rect band)
    {
        Fill(dc, band, Color.FromRgb(0x05, 0x05, 0x06));
        var logo = LogoArea(band);
        DrawPsMark(dc, logo, colorful: true);
        VerticalText(dc, "PlayStation®2", TextArea(band, logo.Bottom + band.Width * 0.3, band.Width * 0.45), White, Face("Segoe UI", FontWeights.Normal), maxFill: 0.62);
        Separator(dc, band, Color.FromRgb(0x26, 0x26, 0x2A));
    }

    // PS3: franja negra con un brillo plateado arriba y «PLAYSTATION 3» espaciado.
    static void DrawPs3(DrawingContext dc, Rect band)
    {
        Fill(dc, band, Color.FromRgb(0x06, 0x06, 0x07));
        var shine = new LinearGradientBrush(Color.FromArgb(90, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(0, 0.12));
        dc.DrawRectangle(shine, null, band);
        VerticalText(dc, Spaced("PLAYSTATION 3"), TextArea(band, band.Width * 0.5, band.Width * 0.5), White, Face("Segoe UI", FontWeights.Light), maxFill: 0.55);
        Separator(dc, band, Color.FromRgb(0x3A, 0x3A, 0x3E));
    }

    // PSP: franja negra con «PSP» grande y en cursiva.
    static void DrawPsp(DrawingContext dc, Rect band)
    {
        Fill(dc, band, Color.FromRgb(0x0A, 0x0A, 0x0B));
        VerticalText(dc, "PSP", TextArea(band, band.Width * 0.4, band.Height * 0.55), White, Face("Segoe UI", FontWeights.Black, FontStyles.Italic), alignBottom: false);
        VerticalText(dc, Spaced("PlayStation Portable"), TextArea(band, band.Height * 0.5, band.Width * 0.5), Gray(0xB0), Face("Segoe UI", FontWeights.Normal), maxFill: 0.3);
        Separator(dc, band, Color.FromRgb(0x30, 0x30, 0x34));
    }

    // Xbox: franja negra con «XBOX» en verde y un filo verde.
    static void DrawXbox(DrawingContext dc, Rect band)
    {
        Fill(dc, band, Color.FromRgb(0x0A, 0x0C, 0x0A));
        var green = Color.FromRgb(0x7F, 0xBA, 0x00);
        var glow = new LinearGradientBrush(Color.FromArgb(0, green.R, green.G, green.B), Color.FromArgb(110, green.R, green.G, green.B), new Point(0, 0), new Point(1, 0));
        dc.DrawRectangle(glow, null, new Rect(band.Right - band.Width * 0.35, band.Top, band.Width * 0.35, band.Height));
        DrawXboxSphere(dc, LogoArea(band), dark: true);
        VerticalText(dc, "XBOX", TextArea(band, band.Width * 1.6, band.Width * 0.5), new SolidColorBrush(green), Face("Segoe UI", FontWeights.Black), maxFill: 0.75);
        Separator(dc, band, green);
    }

    // Xbox 360: degradado de verde a blanco, «XBOX 360» en gris y la esfera abajo.
    static void DrawXbox360(DrawingContext dc, Rect band)
    {
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x4C, 0xA8, 0x23), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x8C, 0xCB, 0x4E), 0.16));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0xFA, 0xFA, 0xFA), 0.38));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0xEE, 0xEE, 0xEE), 1));
        dc.DrawRectangle(gradient, null, band);

        // Rayas diagonales claras de la parte verde.
        var stripes = new Pen(new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)), band.Width * 0.12);
        for (var y = band.Top - band.Width; y < band.Top + band.Height * 0.3; y += band.Width * 0.45)
            dc.DrawLine(stripes, new Point(band.Left, y + band.Width), new Point(band.Right, y));

        var sphere = new Rect(band.Left + band.Width * 0.1, band.Bottom - band.Width * 0.95, band.Width * 0.8, band.Width * 0.8);
        DrawXboxSphere(dc, sphere, dark: false);
        var area = new Rect(band.Left, band.Top + band.Height * 0.33, band.Width, sphere.Top - band.Width * 0.3 - (band.Top + band.Height * 0.33));
        VerticalText(dc, "XBOX 360", area, Gray(0x6A), Face("Segoe UI", FontWeights.Light), maxFill: 0.62);
        Separator(dc, band, Color.FromRgb(0xC8, 0xC8, 0xC8));
    }

    // Game Boy: franja gris con «GAME BOY» en azul y en cursiva, como las cajas originales.
    static void DrawGb(DrawingContext dc, Rect band)
    {
        Fill(dc, band, Color.FromRgb(0xB4, 0xB4, 0xB8));
        VerticalText(dc, "GAME BOY", TextArea(band, band.Width * 0.5, band.Width * 0.5),
            new SolidColorBrush(Color.FromRgb(0x3B, 0x3F, 0xA8)), Face("Segoe UI", FontWeights.Black, FontStyles.Italic), maxFill: 0.72);
        Separator(dc, band, Color.FromRgb(0x8E, 0x8E, 0x94));
    }

    // Game Boy Advance: franja añil con «GAME BOY ADVANCE» en cursiva.
    static void DrawGba(DrawingContext dc, Rect band)
    {
        dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x3E, 0x39, 0x9E), Color.FromRgb(0x26, 0x22, 0x6E), 90), null, band);
        VerticalText(dc, "GAME BOY ADVANCE", TextArea(band, band.Width * 0.5, band.Width * 0.5), White, Face("Segoe UI", FontWeights.Black, FontStyles.Italic), maxFill: 0.72);
        Separator(dc, band, Color.FromRgb(0x1C, 0x19, 0x55));
    }

    // Nintendo 64: franja negra con «NINTENDO 64» y los cuatro colores del logo arriba.
    static void DrawN64(DrawingContext dc, Rect band)
    {
        Fill(dc, band, Color.FromRgb(0x10, 0x10, 0x12));
        var logo = LogoArea(band);
        var half = logo.Width / 2;
        Color[] colors = [Color.FromRgb(0xE4, 0x1F, 0x26), Color.FromRgb(0x00, 0x9B, 0x48), Color.FromRgb(0x00, 0x5B, 0xAC), Color.FromRgb(0xF6, 0xB1, 0x00)];
        for (var i = 0; i < 4; i++)
        {
            var cell = new Rect(logo.X + (i % 2) * half, logo.Y + (i / 2) * half, half - 3, half - 3);
            dc.DrawRoundedRectangle(new SolidColorBrush(colors[i]), null, cell, half * 0.12, half * 0.12);
        }
        VerticalText(dc, "NINTENDO 64", TextArea(band, logo.Bottom + band.Width * 0.3, band.Width * 0.5), White, Face("Segoe UI", FontWeights.Black), maxFill: 0.6);
        Separator(dc, band, Color.FromRgb(0x30, 0x30, 0x34));
    }

    // GameCube: franja negra que se vuelve morada abajo, con «GAMECUBE» y el cubo del logo arriba.
    static void DrawGameCube(DrawingContext dc, Rect band)
    {
        dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x0A, 0x0A, 0x0C), Color.FromRgb(0x3B, 0x2C, 0x75), 90), null, band);
        var logo = LogoArea(band);
        logo.Inflate(-logo.Width * 0.12, -logo.Height * 0.12);
        var purple = new SolidColorBrush(Color.FromRgb(0x6A, 0x5A, 0xCD));
        dc.DrawRectangle(null, new Pen(purple, logo.Width * 0.16), logo);
        var inner = logo;
        inner.Inflate(-logo.Width * 0.3, -logo.Height * 0.3);
        dc.DrawRectangle(purple, null, inner);
        VerticalText(dc, "GAMECUBE", TextArea(band, logo.Bottom + band.Width * 0.4, band.Width * 0.5), White, Face("Segoe UI", FontWeights.Bold), maxFill: 0.62);
        Separator(dc, band, Color.FromRgb(0x2A, 0x20, 0x55));
    }

    // Wii: franja blanca con «Wii» en gris.
    static void DrawWii(DrawingContext dc, Rect band)
    {
        dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xE6, 0xE6, 0xE6), 0), null, band);
        VerticalText(dc, "Wii", TextArea(band, band.Width * 0.6, band.Height * 0.6), Gray(0x8E), Face("Segoe UI", FontWeights.SemiBold), maxFill: 0.7, alignBottom: false);
        Separator(dc, band, Color.FromRgb(0xC8, 0xC8, 0xC8));
    }

    // Wii U: franja azul con «Wii U» en blanco.
    static void DrawWiiU(DrawingContext dc, Rect band)
    {
        dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x00, 0xAA, 0xDA), Color.FromRgb(0x00, 0x84, 0xB4), 0), null, band);
        VerticalText(dc, "Wii U", TextArea(band, band.Width * 0.6, band.Height * 0.55), White, Face("Segoe UI", FontWeights.SemiBold), maxFill: 0.7, alignBottom: false);
        Separator(dc, band, Color.FromRgb(0x00, 0x6E, 0x96));
    }

    // Nintendo DS: franja plateada con «NINTENDO DS» en gris oscuro.
    static void DrawNds(DrawingContext dc, Rect band)
    {
        dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0xF0, 0xF1, 0xF2), Color.FromRgb(0xC4, 0xC6, 0xC9), 0), null, band);
        VerticalText(dc, "NINTENDO DS", TextArea(band, band.Width * 0.5, band.Width * 0.5), Gray(0x3C), Face("Segoe UI", FontWeights.Black), maxFill: 0.72);
        Separator(dc, band, Color.FromRgb(0xA8, 0xAA, 0xAD));
    }

    // PC: franja gris muy oscura con la etiqueta «PC» arriba y «PC GAME» en vertical.
    static void DrawPc(DrawingContext dc, Rect band)
    {
        Fill(dc, band, Color.FromRgb(0x1A, 0x1A, 0x1C));
        var badge = LogoArea(band);
        badge.Inflate(-band.Width * 0.05, -band.Width * 0.18);
        dc.DrawRoundedRectangle(Brushes.White, null, badge, badge.Height * 0.18, badge.Height * 0.18);
        var label = Text("PC", badge.Height * 0.8, new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1C)), Face("Segoe UI", FontWeights.Black));
        dc.DrawText(label, new Point(badge.X + (badge.Width - label.Width) / 2, badge.Y + (badge.Height - label.Height) / 2));
        VerticalText(dc, Spaced("PC GAME"), TextArea(band, badge.Bottom + band.Width * 0.5, band.Width * 0.5), Gray(0xC8), Face("Segoe UI", FontWeights.SemiBold), maxFill: 0.55);
        Separator(dc, band, Color.FromRgb(0x33, 0x33, 0x37));
    }

    /// <summary>Marca de inspiración PlayStation: «PS» con las letras de colores.</summary>
    static void DrawPsMark(DrawingContext dc, Rect area, bool colorful)
    {
        var text = Text("PS", area.Height * 0.62, White, Face("Segoe UI", FontWeights.Black, FontStyles.Italic));
        if (colorful)
        {
            text.SetForegroundBrush(new SolidColorBrush(Color.FromRgb(0xE4, 0x1F, 0x26)), 0, 1);
            var s = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            s.GradientStops.Add(new GradientStop(Color.FromRgb(0xF6, 0xB1, 0x00), 0));
            s.GradientStops.Add(new GradientStop(Color.FromRgb(0x00, 0xA4, 0x50), 0.5));
            s.GradientStops.Add(new GradientStop(Color.FromRgb(0x00, 0x6C, 0xB7), 1));
            text.SetForegroundBrush(s, 1, 1);
        }
        dc.DrawText(text, new Point(area.X + (area.Width - text.Width) / 2, area.Y + (area.Height - text.Height) / 2));
    }

    /// <summary>Esfera de inspiración Xbox: un círculo con una «X» encima.</summary>
    static void DrawXboxSphere(DrawingContext dc, Rect area, bool dark)
    {
        var center = new Point(area.X + area.Width / 2, area.Y + area.Height / 2);
        var radius = Math.Min(area.Width, area.Height) * 0.42;
        var fill = new RadialGradientBrush(dark ? Color.FromRgb(0x3A, 0x3A, 0x3A) : Color.FromRgb(0xFF, 0xFF, 0xFF),
                                           dark ? Color.FromRgb(0x10, 0x10, 0x10) : Color.FromRgb(0xC9, 0xC9, 0xC9))
        {
            GradientOrigin = new Point(0.35, 0.3),
        };
        dc.DrawEllipse(fill, new Pen(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), radius * 0.04), center, radius, radius);

        var green = new Pen(new SolidColorBrush(Color.FromRgb(0x6A, 0xB8, 0x2E)), radius * 0.32) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var arm = radius * 0.55;
        dc.DrawLine(green, new Point(center.X - arm, center.Y - arm), new Point(center.X + arm, center.Y + arm));
        dc.DrawLine(green, new Point(center.X + arm, center.Y - arm), new Point(center.X - arm, center.Y + arm));
    }

    /// <summary>
    /// Texto en vertical, de abajo arriba (como en las franjas de las cajas), lo más grande que quepa:
    /// sus letras ocupan <paramref name="maxFill"/> del ancho de la franja sin pasarse del largo disponible.
    /// </summary>
    /// <param name="alignBottom">Apoyado abajo (como en las cajas reales) o centrado en su zona.</param>
    static void VerticalText(DrawingContext dc, string content, Rect area, Brush brush, Typeface face,
                             double maxFill = 0.8, bool alignBottom = true)
    {
        if (area.Width <= 0 || area.Height <= 0)
            return;

        // Se mide por la tinta de las letras (no por el alto del renglón, que incluye mucho aire).
        const double probe = 100;
        var sample = Text(content, probe, brush, face);
        var size = probe * area.Width * maxFill / Math.Max(1, sample.Extent);
        var text = Text(content, size, brush, face);
        if (text.Width > area.Height)
        {
            size *= area.Height / text.Width;
            text = Text(content, size, brush, face);
        }

        // Girado 90° a la izquierda: el renglón avanza hacia arriba y la tinta queda centrada en el ancho de la franja.
        var inkTop = text.Height + text.OverhangAfter - text.Extent;
        var x = area.X + area.Width / 2 - (inkTop + text.Extent / 2);
        var y = alignBottom ? area.Bottom : area.Bottom - (area.Height - text.Width) / 2;
        dc.PushTransform(new MatrixTransform(0, -1, 1, 0, x, y));
        dc.DrawText(text, new Point(0, 0));
        dc.Pop();
    }

    /// <summary>Zona del texto vertical: toda la franja menos lo reservado arriba y abajo.</summary>
    static Rect TextArea(Rect band, double top, double bottom) =>
        new(band.X, band.Top + top, band.Width, Math.Max(0, band.Height - top - bottom));

    static Rect LogoArea(Rect band) => new(band.X + band.Width * 0.08, band.Top + band.Width * 0.12, band.Width * 0.84, band.Width * 0.84);

    static void Fill(DrawingContext dc, Rect band, Color color) => dc.DrawRectangle(new SolidColorBrush(color), null, band);

    static void Separator(DrawingContext dc, Rect band, Color color) =>
        dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(band.Right, band.Top, 2, band.Height));

    static string Spaced(string text) => string.Join(' ', text.ToCharArray());

    static Typeface Face(string family, FontWeight weight, FontStyle? style = null) =>
        new(new FontFamily(family), style ?? FontStyles.Normal, weight, FontStretches.Normal);

    static FormattedText Text(string content, double size, Brush brush, Typeface face) =>
        new(content, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush, 1.0);

    static readonly Brush White = Brushes.White;

    static Brush Gray(byte level) => new SolidColorBrush(Color.FromRgb(level, level, level));
}
