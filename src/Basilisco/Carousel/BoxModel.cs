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
    // Caja cuadrada: las carátulas de 1024 × 1024 la llenan entera.
    public const double Width = 1.0;
    public const double Height = 1.0;

    /// <summary>Grosor de la caja; en proporción, el de una caja de CD de PS1.</summary>
    public const double Depth = 0.08;

    const double W = Width / 2;
    const double D = Depth / 2;

    // Cada cara se define con sus esquinas abajo-izquierda, abajo-derecha, arriba-derecha y arriba-izquierda
    // vistas desde fuera, para que la textura quede derecha y la cara mire hacia fuera.
    static readonly MeshGeometry3D FrontMesh = Quad(new(-W, 0, D), new(W, 0, D), new(W, Height, D), new(-W, Height, D));
    static readonly MeshGeometry3D BackMesh = Quad(new(W, 0, -D), new(-W, 0, -D), new(-W, Height, -D), new(W, Height, -D));
    static readonly MeshGeometry3D SpineMesh = Quad(new(-W, 0, -D), new(-W, 0, D), new(-W, Height, D), new(-W, Height, -D));
    static readonly MeshGeometry3D EdgeMesh = Quad(new(W, 0, D), new(W, 0, -D), new(W, Height, -D), new(W, Height, D));
    static readonly MeshGeometry3D TopMesh = Quad(new(-W, Height, D), new(W, Height, D), new(W, Height, -D), new(-W, Height, -D));
    static readonly MeshGeometry3D BottomMesh = Quad(new(-W, 0, -D), new(W, 0, -D), new(W, 0, D), new(-W, 0, D));

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
        var center = new Point3D(0, Height / 2, 0);
        var transform = new Transform3DGroup();
        transform.Children.Add(new RotateTransform3D(_yaw, center));
        transform.Children.Add(new RotateTransform3D(_pitch, center));
        transform.Children.Add(_lift);
        transform.Children.Add(new RotateTransform3D(SideRotation));
        transform.Children.Add(Position);
        Box.Transform = transform;

        var mirrored = new Transform3DGroup();
        mirrored.Children.Add(transform);
        mirrored.Children.Add(new ScaleTransform3D(1, -1, 1));
        Reflection.Transform = mirrored;

        AddFace(Box, FrontMesh, _front);
        AddFace(Box, BackMesh, _back);
        AddFace(Box, SpineMesh, _spine);
        AddFace(Box, EdgeMesh, _edge);
        AddFace(Box, TopMesh, _edge);
        AddFace(Box, BottomMesh, _edge);

        // WPF ya corrige la orientación de las caras al aplicar el espejo, así que el reflejo usa las mismas mallas.
        // Arriba y abajo no hacen falta: nunca se ven en el reflejo.
        AddFace(Reflection, FrontMesh, _frontReflection);
        AddFace(Reflection, BackMesh, _backReflection);
        AddFace(Reflection, SpineMesh, _spineReflection);
        AddFace(Reflection, EdgeMesh, _edgeReflection);

        SetArt(item);
        if (renderBackNow)
            EnsureBack();
    }

    public void SetArt(CarouselItem item)
    {
        _item = item;

        var front = CoverArt.RenderCover(item);
        _front.ImageSource = front;
        _frontReflection.ImageSource = CoverArt.RenderReflection(front);

        var spine = CoverArt.RenderSpine(item);
        _spine.ImageSource = spine;
        _spineReflection.ImageSource = CoverArt.RenderReflection(spine);

        if (_backRendered)
        {
            _backRendered = false;
            EnsureBack();
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
        if (yaw != 0)
            EnsureBack();

        double y = yaw * Math.PI / 180, p = pitch * Math.PI / 180;
        var depthExtent = D * Math.Abs(Math.Cos(y)) + W * Math.Abs(Math.Sin(y));
        var halfHeight = Height / 2;
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
        var back = CoverArt.RenderBack(_item);
        _back.ImageSource = back;
        _backReflection.ImageSource = CoverArt.RenderReflection(back);
    }

    void AddFace(Model3DGroup group, MeshGeometry3D mesh, Brush brush)
    {
        var face = new GeometryModel3D(mesh, new DiffuseMaterial(brush));
        group.Children.Add(face);
        _faces.Add(face);
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
