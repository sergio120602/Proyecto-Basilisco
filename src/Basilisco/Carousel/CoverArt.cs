using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Basilisco.Carousel;

/// <summary>
/// Genera las texturas de las portadas. De momento son cajas negras genéricas;
/// aquí es donde entrarán las carátulas reales más adelante.
/// </summary>
public static class CoverArt
{
    const int Width = 576;
    const int Height = 500;
    const int ReflectionWidth = 288;
    const int ReflectionHeight = 250;

    static readonly Typeface TitleFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public static (BitmapSource Cover, BitmapSource Reflection) Render(CarouselItem item)
    {
        var cover = Rasterize(Width, Height, dc =>
        {
            if (item.IsAddCard)
                DrawAddCard(dc);
            else
                DrawBlankCase(dc, item.Title);
        });

        var reflection = Rasterize(ReflectionWidth, ReflectionHeight, dc =>
        {
            var fade = new LinearGradientBrush(Color.FromArgb(95, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), new Point(0, 0), new Point(0, 0.5));
            dc.PushOpacityMask(fade);
            dc.PushTransform(new MatrixTransform(1, 0, 0, -1, 0, ReflectionHeight));
            dc.DrawImage(cover, new Rect(0, 0, ReflectionWidth, ReflectionHeight));
            dc.Pop();
            dc.Pop();
        });

        return (cover, reflection);
    }

    static void DrawBlankCase(DrawingContext dc, string title)
    {
        var bounds = new Rect(0, 0, Width, Height);
        const double spine = Width * 0.085;

        dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x17, 0x18, 0x1C), Color.FromRgb(0x05, 0x05, 0x07), 45), null, bounds);

        // Lomo de la caja.
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0D)), null, new Rect(0, 0, spine, Height));
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2B, 0x2D, 0x33)), null, new Rect(spine, 0, 2, Height));

        // Brillo del plástico.
        var gloss = new LinearGradientBrush(Color.FromArgb(30, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(0.6, 0.6));
        dc.DrawRectangle(gloss, null, bounds);

        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0x3C, 0x3F, 0x47)), 4), new Rect(2, 2, Width - 4, Height - 4));

        var text = CreateText(title, 30, Color.FromArgb(150, 255, 255, 255), Width - spine - 56, TextAlignment.Left);
        dc.DrawText(text, new Point(spine + 28, Height - 28 - text.Height));
    }

    static void DrawAddCard(DrawingContext dc)
    {
        var bounds = new Rect(0, 0, Width, Height);
        dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x24, 0x29, 0x32), Color.FromRgb(0x12, 0x14, 0x19), 90), null, bounds);

        var dashed = new Pen(new SolidColorBrush(Color.FromRgb(0x6B, 0x75, 0x85)), 4) { DashStyle = new DashStyle([4, 3], 0) };
        dc.DrawRoundedRectangle(null, dashed, new Rect(22, 22, Width - 44, Height - 44), 18, 18);

        var plus = new SolidColorBrush(Color.FromRgb(0xC9, 0xD1, 0xDC));
        const double cx = Width / 2.0, cy = Height / 2.0 - 40, arm = 50, thickness = 10;
        dc.DrawRoundedRectangle(plus, null, new Rect(cx - arm, cy - thickness / 2, arm * 2, thickness), 5, 5);
        dc.DrawRoundedRectangle(plus, null, new Rect(cx - thickness / 2, cy - arm, thickness, arm * 2), 5, 5);

        var text = CreateText("Importar juego", 36, Color.FromRgb(0xC9, 0xD1, 0xDC), Width, TextAlignment.Center);
        dc.DrawText(text, new Point(0, cy + arm + 30));
    }

    static FormattedText CreateText(string text, double size, Color color, double maxWidth, TextAlignment alignment) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, TitleFace, size, new SolidColorBrush(color), 1.0)
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = 2,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = alignment,
        };

    static BitmapSource Rasterize(int width, int height, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            draw(dc);

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
