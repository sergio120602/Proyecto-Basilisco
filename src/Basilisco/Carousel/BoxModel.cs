using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace Basilisco.Carousel;

/// <summary>
/// Caja de juego en 3D: portada delante, contraportada detrás, lomo a la izquierda y cantos,
/// con su grosor real. Incluye su reflejo en el suelo, que es la misma caja en espejo,
/// así que también gira con ella.
/// </summary>
public sealed class BoxModel
{
    /// <summary>Alto de referencia (el de la caja cuadrada y el de una caja de DVD); las demás miden según su consola.</summary>
    public const double Height = 1.0;

    /// <summary>Las seis caras de una caja de un ancho dado; se comparten entre todas las cajas del mismo formato.</summary>
    sealed record Meshes(MeshGeometry3D Front, MeshGeometry3D Back, MeshGeometry3D Spine,
                         MeshGeometry3D Edge, MeshGeometry3D Top, MeshGeometry3D Bottom)
    {
        // Cada cara se define con sus esquinas abajo-izquierda, abajo-derecha, arriba-derecha y arriba-izquierda
        // vistas desde fuera, para que la textura quede derecha y la cara mire hacia fuera.
        public static Meshes Create(BoxShape shape)
        {
            double w = shape.Width / 2, h = shape.Height, d = shape.Depth / 2;
            return new(
                Quad(new(-w, 0, d), new(w, 0, d), new(w, h, d), new(-w, h, d)),
                Quad(new(w, 0, -d), new(-w, 0, -d), new(-w, h, -d), new(w, h, -d)),
                Quad(new(-w, 0, -d), new(-w, 0, d), new(-w, h, d), new(-w, h, -d)),
                Quad(new(w, 0, d), new(w, 0, -d), new(w, h, -d), new(w, h, d)),
                Quad(new(-w, h, d), new(w, h, d), new(w, h, -d), new(-w, h, -d)),
                Quad(new(-w, 0, -d), new(w, 0, -d), new(w, 0, d), new(-w, 0, d)));
        }
    }

    static readonly Dictionary<BoxShape, Meshes> MeshesByShape = [];

    static Meshes MeshesFor(BoxShape shape)
    {
        if (!MeshesByShape.TryGetValue(shape, out var meshes))
            MeshesByShape[shape] = meshes = Meshes.Create(shape);
        return meshes;
    }

    /// <summary>Ancho de la caja en la escena (para colocar las cajas en el carrusel).</summary>
    public static double WidthOf(CarouselItem item) => BoxShape.Of(item).Width;

    static readonly Color Plastic = Color.FromRgb(0x17, 0x18, 0x1C);
    static readonly BitmapSource Placeholder = CreatePlaceholder();

    readonly ImageBrush _front = new();
    readonly ImageBrush _back = new(Placeholder);
    readonly ImageBrush _spine = new();
    readonly ImageBrush _frontReflection = new();
    readonly ImageBrush _backReflection = new();
    readonly ImageBrush _spineReflection = new();
    readonly SolidColorBrush _edge = new(Plastic);
    readonly LinearGradientBrush _edgeReflection =
        new(Color.FromArgb(95, Plastic.R, Plastic.G, Plastic.B), Color.FromArgb(0, Plastic.R, Plastic.G, Plastic.B), new Point(0, 1), new Point(0, 0.5));
    readonly Brush[] _brushes;

    readonly AxisAngleRotation3D _yaw = new(new Vector3D(0, 1, 0), 0);
    readonly AxisAngleRotation3D _pitch = new(new Vector3D(1, 0, 0), 0);
    readonly TranslateTransform3D _lift = new();
    readonly List<GeometryModel3D> _faces = [];

    CarouselItem _item;
    bool _backRendered;
    BoxShape? _shape;
    readonly RotateTransform3D _yawTransform;
    readonly RotateTransform3D _pitchTransform;

    // Caras en el mismo orden en que se crean: caja (delante, detrás, lomo, canto, arriba, abajo) y reflejo (las cuatro primeras).
    readonly List<GeometryModel3D> _boxFaces = [];
    readonly List<GeometryModel3D> _reflectionFaces = [];

    public Model3DGroup Box { get; } = new();
    public Model3DGroup Reflection { get; } = new();

    /// <summary>Giro de la caja en el carrusel (las laterales miran hacia el centro).</summary>
    public AxisAngleRotation3D SideRotation { get; } = new(new Vector3D(0, 1, 0), 0);

    /// <summary>Posición de la caja en el carrusel.</summary>
    public TranslateTransform3D Position { get; } = new();

    /// <summary>Todas las caras (caja y reflejo), para saber qué caja hay bajo el ratón.</summary>
    public IReadOnlyList<GeometryModel3D> Faces => _faces;

    public BoxModel(CarouselItem item, bool renderBackNow = false)
    {
        _item = item;
        _brushes = [_front, _back, _spine, _frontReflection, _backReflection, _spineReflection, _edge, _edgeReflection];

        // Primero el giro de inspección alrededor del centro de la caja, después su sitio en el carrusel.
        // El centro del giro se ajusta en SetArt a la mitad de la altura de la caja.
        _yawTransform = new RotateTransform3D(_yaw);
        _pitchTransform = new RotateTransform3D(_pitch);
        var transform = new Transform3DGroup();
        transform.Children.Add(_yawTransform);
        transform.Children.Add(_pitchTransform);
        transform.Children.Add(_lift);
        transform.Children.Add(new RotateTransform3D(SideRotation));
        transform.Children.Add(Position);
        Box.Transform = transform;

        var mirrored = new Transform3DGroup();
        mirrored.Children.Add(transform);
        mirrored.Children.Add(new ScaleTransform3D(1, -1, 1));
        Reflection.Transform = mirrored;

        foreach (var brush in new Brush[] { _front, _back, _spine, _edge, _edge, _edge })
            _boxFaces.Add(AddFace(Box, brush));

        // WPF ya corrige la orientación de las caras al aplicar el espejo, así que el reflejo usa las mismas mallas.
        // Arriba y abajo no hacen falta: nunca se ven en el reflejo.
        foreach (var brush in new Brush[] { _frontReflection, _backReflection, _spineReflection, _edgeReflection })
            _reflectionFaces.Add(AddFace(Reflection, brush));

        SetArt(item);
        if (renderBackNow)
        {
            EnsureArt();
            EnsureBack();
        }
    }

    bool _hasArt;
    double _artScale = 1;

    /// <summary>True si ya tiene pintadas la portada y el lomo (las cajas lejanas no las tienen, para ahorrar memoria).</summary>
    public bool HasArt => _hasArt;

    /// <summary>Resolución a la que están pintadas (1 = completa).</summary>
    public double ArtScale => _artScale;

    /// <summary>Con false, no se pintan los reflejos en el suelo (ahorra memoria y tiempo).</summary>
    public static bool Reflections { get; set; } = true;

    /// <summary>Pinta la portada y el lomo si aún no los tiene. Es lo que más cuesta: se hace solo con las cajas que se ven.</summary>
    /// <param name="scale">Resolución (1 = completa); si ya estaba pintada a otra, se vuelve a pintar.</param>
    public void EnsureArt(double scale = 1)
    {
        if (_hasArt && _artScale == scale)
            return;
        var back = _backRendered;
        _hasArt = true;
        _artScale = scale;
        _backRendered = false;

        var previous = CoverArt.RenderScale;
        CoverArt.RenderScale = scale;
        try
        {
            var front = CoverArt.RenderCover(_item);
            _front.ImageSource = front;
            _frontReflection.ImageSource = Reflections ? CoverArt.RenderReflection(front) : null;

            var spine = CoverArt.RenderSpine(_item);
            _spine.ImageSource = spine;
            _spineReflection.ImageSource = Reflections ? CoverArt.RenderReflection(spine) : null;
        }
        finally
        {
            CoverArt.RenderScale = previous;
        }
        _back.ImageSource = _backReflection.ImageSource = null;
        if (back)
            EnsureBack();
    }

    /// <summary>Suelta las imágenes pintadas (la caja se ha alejado mucho); se vuelven a pintar si vuelve a verse.</summary>
    public void ReleaseArt()
    {
        if (!_hasArt)
            return;
        _hasArt = false;
        _backRendered = false;
        _front.ImageSource = _frontReflection.ImageSource = null;
        _spine.ImageSource = _spineReflection.ImageSource = null;
        _back.ImageSource = _backReflection.ImageSource = null;
    }

    public void SetArt(CarouselItem item)
    {
        _item = item;

        // La tarjeta de importar siempre es cuadrada; al cambiar de formato o de consola la caja cambia de medidas.
        var shape = BoxShape.Of(item);
        if (_shape != shape)
        {
            _shape = shape;
            _yawTransform.CenterY = _pitchTransform.CenterY = shape.Height / 2;
            var meshes = MeshesFor(shape);
            MeshGeometry3D[] box = [meshes.Front, meshes.Back, meshes.Spine, meshes.Edge, meshes.Top, meshes.Bottom];
            for (var i = 0; i < _boxFaces.Count; i++)
                _boxFaces[i].Geometry = box[i];
            for (var i = 0; i < _reflectionFaces.Count; i++)
                _reflectionFaces[i].Geometry = box[i];
        }

        // Cantos del color del plástico de la consola.
        var plastic = PlatformArt.Plastic(item.Platform);
        _edge.Color = plastic;
        _edgeReflection.GradientStops[0].Color = Color.FromArgb(95, plastic.R, plastic.G, plastic.B);
        _edgeReflection.GradientStops[1].Color = Color.FromArgb(0, plastic.R, plastic.G, plastic.B);

        // Las imágenes se pintan al verse la caja (EnsureArt); si ya estaban pintadas, se vuelven a pintar ahora.
        if (_hasArt)
        {
            var scale = _artScale;
            _hasArt = false;
            EnsureArt(scale);
        }
    }

    /// <summary>
    /// Gira la caja sobre sí misma para inspeccionarla (en grados). Si al inclinarla alguna esquina
    /// quedaría por debajo del suelo, la levanta lo justo.
    /// </summary>
    public void SetInspect(double yaw, double pitch)
    {
        _yaw.Angle = yaw;
        _pitch.Angle = pitch;

        // La contraportada solo se genera cuando hace falta: ahorra memoria con bibliotecas grandes.
        if (yaw != 0 && _hasArt)
            EnsureBack();

        double y = yaw * Math.PI / 180, p = pitch * Math.PI / 180;
        var shape = _shape ?? BoxShape.Square;
        var depthExtent = shape.Depth / 2 * Math.Abs(Math.Cos(y)) + shape.Width / 2 * Math.Abs(Math.Sin(y));
        var halfHeight = shape.Height / 2;
        _lift.OffsetY = Math.Max(0, halfHeight * Math.Abs(Math.Cos(p)) + depthExtent * Math.Abs(Math.Sin(p)) - halfHeight);
    }

    public void SetOpacity(double opacity)
    {
        if (_front.Opacity == opacity)
            return;
        foreach (var brush in _brushes)
            brush.Opacity = opacity;
    }

    void EnsureBack()
    {
        if (_backRendered)
            return;

        _backRendered = true;
        var previous = CoverArt.RenderScale;
        CoverArt.RenderScale = _artScale;
        BitmapSource back;
        try
        {
            back = CoverArt.RenderBack(_item);
        }
        finally
        {
            CoverArt.RenderScale = previous;
        }
        _back.ImageSource = back;
        _backReflection.ImageSource = Reflections ? CoverArt.RenderReflection(back) : null;
    }

    GeometryModel3D AddFace(Model3DGroup group, Brush brush)
    {
        // La malla se pone en SetArt, cuando se sabe el formato de la caja.
        var face = new GeometryModel3D { Material = new DiffuseMaterial(brush) };
        group.Children.Add(face);
        _faces.Add(face);
        return face;
    }

    static MeshGeometry3D Quad(Point3D bottomLeft, Point3D bottomRight, Point3D topRight, Point3D topLeft)
    {
        var mesh = new MeshGeometry3D
        {
            Positions = [bottomLeft, bottomRight, topRight, topLeft],
            TextureCoordinates = [new(0, 1), new(1, 1), new(1, 0), new(0, 0)],
            TriangleIndices = [0, 1, 2, 0, 2, 3],
        };
        mesh.Freeze();
        return mesh;
    }

    static BitmapSource CreatePlaceholder()
    {
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { Plastic.B, Plastic.G, Plastic.R, 255 }, 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Luz de la escena: ambiente más una luz frontal suave para que cada cara tenga su tono y se note el volumen.</summary>
    public static Model3DGroup CreateLighting() => new()
    {
        Children =
        {
            new AmbientLight(Color.FromRgb(0xB8, 0xB8, 0xB8)),
            new DirectionalLight(Color.FromRgb(0x70, 0x70, 0x70), new Vector3D(-0.35, -0.45, -1)),
        },
    };
}
