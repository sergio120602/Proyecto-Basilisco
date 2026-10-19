using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace Basilisco.Services;

/// <summary>
/// Escritor de PDF mínimo para los manuales: páginas con texto de verdad (se puede seleccionar y buscar), rectángulos
/// de color e imágenes JPEG. El texto usa Helvetica, que viene con cualquier lector de PDF; sus medidas son las mismas
/// que las de Arial, así que los renglones se cortan con las medidas de Arial que da Windows.
/// </summary>
public sealed class PdfWriter
{
    /// <summary>Tamaño A5 en puntos (148 × 210 mm), el de un librito de instrucciones.</summary>
    public const double PageWidth = 419.53, PageHeight = 595.28;

    readonly List<PdfPage> _pages = [];
    readonly List<(byte[] Jpeg, int Width, int Height)> _images = [];

    public string Title { get; set; } = "";

    public PdfPage AddPage()
    {
        var page = new PdfPage();
        _pages.Add(page);
        return page;
    }

    public int PageCount => _pages.Count;

    /// <summary>Añade una imagen JPEG (en RGB) y devuelve su nombre para dibujarla.</summary>
    public string AddJpeg(byte[] jpeg, int width, int height)
    {
        _images.Add((jpeg, width, height));
        return $"Im{_images.Count}";
    }

    public void Save(string path)
    {
        using var output = File.Create(path);
        var offsets = new List<long>();

        void Write(string text) => output.Write(Encoding.ASCII.GetBytes(text));
        void BeginObject(int number)
        {
            while (offsets.Count < number)
                offsets.Add(0);
            offsets[number - 1] = output.Position;
            Write($"{number} 0 obj\n");
        }

        // Números de objeto: 1 catálogo, 2 árbol de páginas, 3 y 4 fuentes, 5 información, luego imágenes y páginas.
        const int catalog = 1, pages = 2, regular = 3, bold = 4, info = 5;
        var firstImage = 6;
        var firstPage = firstImage + _images.Count;

        output.Write("%PDF-1.4\n%âãÏÓ\n"u8);

        BeginObject(catalog);
        Write($"<< /Type /Catalog /Pages {pages} 0 R >>\nendobj\n");

        BeginObject(pages);
        var kids = string.Join(" ", Enumerable.Range(0, _pages.Count).Select(i => $"{firstPage + i * 2} 0 R"));
        Write($"<< /Type /Pages /Kids [{kids}] /Count {_pages.Count} >>\nendobj\n");

        BeginObject(regular);
        Write("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");
        BeginObject(bold);
        Write("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n");

        BeginObject(info);
        Write("<< /Title ");
        output.Write(PdfText.Literal(Title));
        Write($" /Producer (Basilisco {AppInfo.Version}) /CreationDate (D:{DateTime.Now:yyyyMMddHHmmss}) >>\nendobj\n");

        for (var i = 0; i < _images.Count; i++)
        {
            var (jpeg, width, height) = _images[i];
            BeginObject(firstImage + i);
            Write($"<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceRGB " +
                  $"/BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
            output.Write(jpeg);
            Write("\nendstream\nendobj\n");
        }

        var imageResources = string.Join(" ", Enumerable.Range(0, _images.Count).Select(i => $"/Im{i + 1} {firstImage + i} 0 R"));
        for (var i = 0; i < _pages.Count; i++)
        {
            var pageNumber = firstPage + i * 2;
            var content = _pages[i].Content();
            BeginObject(pageNumber);
            Write($"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 {Num(PageWidth)} {Num(PageHeight)}] " +
                  $"/Resources << /Font << /F1 {regular} 0 R /F2 {bold} 0 R >> /XObject << {imageResources} >> >> " +
                  $"/Contents {pageNumber + 1} 0 R >>\nendobj\n");
            BeginObject(pageNumber + 1);
            Write($"<< /Length {content.Length} >>\nstream\n");
            output.Write(content);
            Write("\nendstream\nendobj\n");
        }

        var xref = output.Position;
        Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            Write($"{offset:D10} 00000 n \n");
        Write($"trailer\n<< /Size {offsets.Count + 1} /Root {catalog} 0 R /Info {info} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }

    internal static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>Una página: se dibuja con el origen arriba a la izquierda, en puntos.</summary>
public sealed class PdfPage
{
    readonly MemoryStream _content = new();

    void Write(string text) => _content.Write(Encoding.ASCII.GetBytes(text));

    static string Rgb(Color color) =>
        $"{PdfWriter.Num(color.R / 255.0)} {PdfWriter.Num(color.G / 255.0)} {PdfWriter.Num(color.B / 255.0)}";

    public void FillRect(double x, double y, double width, double height, Color color) =>
        Write($"{Rgb(color)} rg {PdfWriter.Num(x)} {PdfWriter.Num(PdfWriter.PageHeight - y - height)} {PdfWriter.Num(width)} {PdfWriter.Num(height)} re f\n");

    /// <summary>Escribe una línea de texto; <paramref name="y"/> es la línea base.</summary>
    public void Text(double x, double y, string text, double size, Color color, bool bold = false)
    {
        Write($"BT /{(bold ? "F2" : "F1")} {PdfWriter.Num(size)} Tf {Rgb(color)} rg {PdfWriter.Num(x)} {PdfWriter.Num(PdfWriter.PageHeight - y)} Td ");
        _content.Write(PdfText.Literal(text));
        Write(" Tj ET\n");
    }

    /// <summary>Dibuja una imagen añadida con <see cref="PdfWriter.AddJpeg"/>. Lo que se salga de la página no se ve.</summary>
    public void Image(string name, double x, double y, double width, double height) =>
        Write($"q {PdfWriter.Num(width)} 0 0 {PdfWriter.Num(height)} {PdfWriter.Num(x)} {PdfWriter.Num(PdfWriter.PageHeight - y - height)} cm /{name} Do Q\n");

    /// <summary>Lo mismo, pero recortado a un rectángulo (para llenar un hueco sin salirse).</summary>
    public void ImageClipped(string name, Rect clip, double x, double y, double width, double height)
    {
        Write($"q {PdfWriter.Num(clip.X)} {PdfWriter.Num(PdfWriter.PageHeight - clip.Y - clip.Height)} {PdfWriter.Num(clip.Width)} {PdfWriter.Num(clip.Height)} re W n ");
        Write($"{PdfWriter.Num(width)} 0 0 {PdfWriter.Num(height)} {PdfWriter.Num(x)} {PdfWriter.Num(PdfWriter.PageHeight - y - height)} cm /{name} Do Q\n");
    }

    internal byte[] Content() => _content.ToArray();
}

/// <summary>Texto para el PDF: en WinAnsi (como Windows-1252) y con las medidas de Helvetica para cortar renglones.</summary>
public static class PdfText
{
    static readonly Encoding WinAnsi = CreateEncoding();
    static readonly GlyphTypeface? Regular = Glyphs(FontWeights.Normal);
    static readonly GlyphTypeface? BoldFace = Glyphs(FontWeights.Bold);

    static Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252, new EncoderReplacementFallback("?"), new DecoderReplacementFallback("?"));
    }

    static GlyphTypeface? Glyphs(System.Windows.FontWeight weight) =>
        new Typeface(new FontFamily("Arial"), System.Windows.FontStyles.Normal, weight, System.Windows.FontStretches.Normal)
            .TryGetGlyphTypeface(out var glyphs) ? glyphs : null;

    /// <summary>Lo que no existe en WinAnsi (letras japonesas, emojis…) se cambia por algo parecido o por «?».</summary>
    public static string Clean(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormC))
        {
            builder.Append(c switch
            {
                '‘' or '’' or '‚' => c,
                ' ' or ' ' or ' ' => ' ',
                '‐' or '‑' or '‒' => '-',
                '\t' => ' ',
                _ => c,
            });
        }
        var bytes = WinAnsi.GetBytes(builder.ToString());
        return WinAnsi.GetString(bytes);
    }

    public static double Width(string text, double size, bool bold = false)
    {
        var glyphs = bold ? BoldFace : Regular;
        if (glyphs is null)
            return text.Length * size * 0.5;
        double width = 0;
        foreach (var c in text)
            width += glyphs.CharacterToGlyphMap.TryGetValue(c, out var glyph) ? glyphs.AdvanceWidths[glyph] : 0.5;
        return width * size;
    }

    /// <summary>Corta el texto en renglones que quepan en ese ancho (las palabras demasiado largas se parten).</summary>
    public static List<string> Wrap(string text, double size, double maxWidth, bool bold = false)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (Width(candidate, size, bold) <= maxWidth)
            {
                line = candidate;
                continue;
            }
            if (line.Length > 0)
                lines.Add(line);
            line = word;
            while (Width(line, size, bold) > maxWidth && line.Length > 1)
            {
                var cut = line.Length - 1;
                while (cut > 1 && Width(line[..cut], size, bold) > maxWidth)
                    cut--;
                lines.Add(line[..cut]);
                line = line[cut..];
            }
        }
        if (line.Length > 0)
            lines.Add(line);
        return lines;
    }

    /// <summary>El texto como cadena literal de PDF: (…) en WinAnsi, escapando paréntesis y barras.</summary>
    internal static byte[] Literal(string text)
    {
        var bytes = WinAnsi.GetBytes(Clean(text));
        var output = new List<byte>(bytes.Length + 8) { (byte)'(' };
        foreach (var b in bytes)
        {
            if (b is (byte)'(' or (byte)')' or (byte)'\\')
                output.Add((byte)'\\');
            output.Add(b);
        }
        output.Add((byte)')');
        return output.ToArray();
    }
}
