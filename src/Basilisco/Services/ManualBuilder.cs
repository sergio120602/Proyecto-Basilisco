using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Basilisco.Models;

namespace Basilisco.Services;

/// <summary>
/// Escribe el manual de un juego en PDF (A5, como un librito de instrucciones): portada, ficha, «Sobre el juego»,
/// requisitos del sistema, capturas y contraportada, con lo que se encuentre en Steam y en LaunchBox.
/// </summary>
public static class ManualBuilder
{
    const double W = PdfWriter.PageWidth, H = PdfWriter.PageHeight;
    const double Margin = 40, Top = 62, Bottom = H - 50;
    const double ContentWidth = W - 2 * Margin;

    static readonly Color Ink = Color.FromRgb(0x1F, 0x26, 0x30);
    static readonly Color Muted = Color.FromRgb(0x5B, 0x65, 0x73);
    static readonly Color Accent = Color.FromRgb(0x3E, 0x6F, 0xD8);
    static readonly Color Paper = Color.FromRgb(0xF4, 0xF1, 0xEA);
    static readonly Color Night = Color.FromRgb(0x12, 0x15, 0x1B);
    static readonly Color Line = Color.FromRgb(0xD5, 0xD0, 0xC4);

    /// <param name="frontPath">La portada del juego (de su caja en Basilisco), para la portada del manual.</param>
    /// <param name="backPath">La contraportada, para la última página.</param>
    /// <returns>La ruta del PDF, en la carpeta temporal.</returns>
    public static async Task<string> CreateAsync(Game game, string? frontPath, string? backPath, IProgress<string> status, CancellationToken cancellation)
    {
        var content = await ManualSources.GatherAsync(game, status, cancellation);

        status.Report("Descargando las capturas…");
        var screenshots = new List<byte[]>();
        foreach (var url in content.Screenshots)
        {
            if (await ManualSources.DownloadAsync(url, cancellation) is { } bytes)
                screenshots.Add(bytes);
        }

        status.Report("Escribiendo el manual…");
        var folder = Path.Combine(Path.GetTempPath(), "Basilisco");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{Guid.NewGuid():N}.pdf");
        // Las imágenes se preparan con WPF, que necesita un hilo STA.
        await RunStaAsync(() => Write(content, frontPath, backPath, screenshots, path));
        return path;
    }

    static Task RunStaAsync(Action action)
    {
        var done = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                action();
                done.SetResult();
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return done.Task;
    }

    static void Write(ManualContent content, string? frontPath, string? backPath, List<byte[]> screenshots, string path)
    {
        var pdf = new PdfWriter { Title = $"{content.Title} · Manual" };
        var layout = new Layout(pdf, content.Title);

        Cover(pdf, content, frontPath);

        // Ficha del juego.
        layout.NewPage();
        layout.Heading("Ficha del juego");
        // La entradilla de Steam suele ser el principio de «Sobre el juego»: entonces no se repite.
        if (content.ShortDescription is { } brief && !StartsAlike(brief, content.About.FirstOrDefault()?.Text))
            layout.Lead(brief);
        layout.Facts(new (string, string?)[]
        {
            ("Consola", content.Platform),
            ("Desarrolladora", content.Developer),
            ("Editora", content.Publisher),
            ("Lanzamiento", content.Released),
            ("Género", content.Genres),
            ("Jugadores", content.Players),
            ("Mando", content.Controller),
            ("Valoración", content.Rating),
            ("Idiomas", content.Languages),
            ("Web", content.Website),
        });

        if (content.About.Count > 0)
        {
            layout.Gap(10);
            layout.Heading("Sobre el juego");
            layout.Blocks(content.About);
        }

        if (content.Requirements.Count > 0)
        {
            layout.Gap(10);
            layout.Heading("Requisitos del sistema");
            layout.Blocks(content.Requirements);
        }

        if (screenshots.Count > 0)
        {
            layout.NewPage();
            layout.Heading("Capturas");
            foreach (var bytes in screenshots)
            {
                if (Jpeg(bytes, 1100) is { } image)
                    layout.Image(pdf.AddJpeg(image.Bytes, image.Width, image.Height), image.Width, image.Height);
            }
        }

        BackCover(pdf, content, backPath);
        pdf.Save(path);
    }

    static bool StartsAlike(string a, string? b)
    {
        if (b is null)
            return false;
        var length = Math.Min(60, Math.Min(a.Length, b.Length));
        return string.Compare(a, 0, b, 0, length, StringComparison.OrdinalIgnoreCase) == 0;
    }

    static void Cover(PdfWriter pdf, ManualContent content, string? frontPath)
    {
        var page = pdf.AddPage();
        page.FillRect(0, 0, W, H, Night);
        var band = 128.0;
        if (frontPath is not null && Jpeg(File.ReadAllBytes(frontPath), 1400) is { } cover)
        {
            // La portada entera, lo más grande que quepa encima de la franja del título.
            var box = new Rect(22, 22, W - 44, H - band - 34);
            var scale = Math.Min(box.Width / cover.Width, box.Height / cover.Height);
            var (width, height) = (cover.Width * scale, cover.Height * scale);
            page.Image(pdf.AddJpeg(cover.Bytes, cover.Width, cover.Height), box.X + (box.Width - width) / 2, box.Y + (box.Height - height) / 2, width, height);
        }

        page.FillRect(0, H - band, W, band, Color.FromRgb(0x0B, 0x0D, 0x11));
        page.FillRect(Margin, H - band + 24, 34, 3, Accent);
        var y = H - band + 52;
        foreach (var line in PdfText.Wrap(PdfText.Clean(content.Title), 21, ContentWidth, bold: true).Take(2))
        {
            page.Text(Margin, y, line, 21, Colors.White, bold: true);
            y += 25;
        }
        var subtitle = content.Platform is { } platform ? $"MANUAL DE INSTRUCCIONES  ·  {platform.ToUpperInvariant()}" : "MANUAL DE INSTRUCCIONES";
        page.Text(Margin, y + 2, subtitle, 8.5, Color.FromRgb(0xA9, 0xB1, 0xBD), bold: true);
    }

    static void BackCover(PdfWriter pdf, ManualContent content, string? backPath)
    {
        var page = pdf.AddPage();
        page.FillRect(0, 0, W, H, Night);
        if (backPath is not null && Jpeg(File.ReadAllBytes(backPath), 1400) is { } back)
        {
            var box = new Rect(22, 22, W - 44, H - 100);
            var scale = Math.Min(box.Width / back.Width, box.Height / back.Height);
            var (width, height) = (back.Width * scale, back.Height * scale);
            page.Image(pdf.AddJpeg(back.Bytes, back.Width, back.Height), box.X + (box.Width - width) / 2, box.Y + (box.Height - height) / 2, width, height);
        }
        var sources = content.Sources.Count > 0 ? $"con datos de {string.Join(" y ", content.Sources.Distinct())}" : "";
        var note = $"Manual creado por Basilisco {sources}".Trim();
        page.Text((W - PdfText.Width(note, 8)) / 2, H - 40, note, 8, Color.FromRgb(0x8C, 0x95, 0xA3));
    }

    /// <summary>Vuelve a guardar la imagen como JPEG en color, reducida si es muy grande (así el PDF pesa poco).</summary>
    static (byte[] Bytes, int Width, int Height)? Jpeg(byte[] data, int maxSide)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = new MemoryStream(data);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.EndInit();
            BitmapSource source = image;
            var scale = Math.Min(1.0, (double)maxSide / Math.Max(source.PixelWidth, source.PixelHeight));
            if (scale < 1)
                source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            // Las transparencias (PNG) se ponen sobre negro, como se verían en la caja.
            if (source.Format.Masks.Count == 4 || source.Format == PixelFormats.Pbgra32 || source.Format == PixelFormats.Bgra32)
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
                    dc.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
                }
                var flat = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
                flat.Render(visual);
                source = flat;
            }
            source = new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);

            var encoder = new JpegBitmapEncoder { QualityLevel = 86 };
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var output = new MemoryStream();
            encoder.Save(output);
            return (output.ToArray(), source.PixelWidth, source.PixelHeight);
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// <summary>Coloca el texto y las imágenes página a página, pasando a la siguiente cuando no cabe.</summary>
    sealed class Layout(PdfWriter pdf, string title)
    {
        PdfPage? _page;
        double _y;
        int _number = 1;

        public void NewPage()
        {
            _page = pdf.AddPage();
            _number++;
            _page.FillRect(0, 0, W, H, Paper);
            // Cabecera con el nombre del juego y pie con el número de página.
            var header = PdfText.Wrap(PdfText.Clean(title), 7.5, ContentWidth - 40, bold: true).FirstOrDefault() ?? "";
            _page.Text(Margin, 30, header.ToUpperInvariant(), 7.5, Muted, bold: true);
            _page.FillRect(Margin, 36, ContentWidth, 0.6, Line);
            var number = _number.ToString();
            _page.Text((W - PdfText.Width(number, 8.5, true)) / 2, H - 24, number, 8.5, Muted, bold: true);
            _y = Top;
        }

        PdfPage Page => _page ?? throw new InvalidOperationException();

        void Need(double height)
        {
            if (_page is null || _y + height > Bottom)
                NewPage();
        }

        public void Gap(double height) => _y += height;

        public void Heading(string text)
        {
            // El título nunca se queda solo al pie de la página: con él caben al menos tres renglones.
            Need(48 + 46);
            Page.FillRect(Margin, _y, 22, 3, Accent);
            _y += 22;
            foreach (var line in PdfText.Wrap(PdfText.Clean(text), 15, ContentWidth, bold: true))
            {
                Page.Text(Margin, _y, line, 15, Ink, bold: true);
                _y += 19;
            }
            _y += 4;
        }

        void SubHeading(string text)
        {
            Need(30 + 30);
            _y += 6;
            foreach (var line in PdfText.Wrap(PdfText.Clean(text), 11, ContentWidth, bold: true))
            {
                Page.Text(Margin, _y, line, 11, Accent, bold: true);
                _y += 15;
            }
            _y += 1;
        }

        public void Lead(string text)
        {
            foreach (var line in PdfText.Wrap(PdfText.Clean(text), 11.5, ContentWidth))
            {
                Need(16);
                Page.Text(Margin, _y, line, 11.5, Muted);
                _y += 16;
            }
            _y += 10;
        }

        public void Paragraph(string text, double indent = 0, string? bullet = null)
        {
            var lines = PdfText.Wrap(PdfText.Clean(text), 10, ContentWidth - indent);
            for (var i = 0; i < lines.Count; i++)
            {
                Need(14);
                if (i == 0 && bullet is not null)
                    Page.Text(Margin + indent - 10, _y, bullet, 10, Accent, bold: true);
                Page.Text(Margin + indent, _y, lines[i], 10, Ink);
                _y += 14;
            }
            _y += bullet is null ? 6 : 2;
        }

        public void Blocks(IEnumerable<ManualBlock> blocks)
        {
            foreach (var block in blocks)
            {
                switch (block.Kind)
                {
                    case ManualBlockKind.Heading: SubHeading(block.Text); break;
                    case ManualBlockKind.Bullet: Paragraph(block.Text, indent: 12, bullet: "•"); break;
                    default: Paragraph(block.Text); break;
                }
            }
        }

        /// <summary>Tabla de datos: el nombre en pequeño a la izquierda y el valor a la derecha.</summary>
        public void Facts(IEnumerable<(string Label, string? Value)> facts)
        {
            const double labelWidth = 92;
            foreach (var (label, value) in facts)
            {
                if (string.IsNullOrWhiteSpace(value))
                    continue;
                var lines = PdfText.Wrap(PdfText.Clean(value), 10, ContentWidth - labelWidth);
                Need(lines.Count * 13 + 10);
                Page.Text(Margin, _y, label.ToUpperInvariant(), 7.5, Muted, bold: true);
                foreach (var line in lines)
                {
                    Page.Text(Margin + labelWidth, _y, line, 10, Ink);
                    _y += 13;
                }
                _y += 4;
                Page.FillRect(Margin, _y - 2, ContentWidth, 0.5, Line);
                _y += 7;
            }
        }

        public void Image(string name, int pixelWidth, int pixelHeight)
        {
            var width = ContentWidth;
            var height = width * pixelHeight / pixelWidth;
            var maxHeight = Bottom - Top - 10;
            if (height > maxHeight)
            {
                height = maxHeight;
                width = height * pixelWidth / pixelHeight;
            }
            Need(height + 14);
            Page.Image(name, Margin + (ContentWidth - width) / 2, _y, width, height);
            _y += height + 14;
        }
    }
}
