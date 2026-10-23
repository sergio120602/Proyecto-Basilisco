using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

namespace Basilisco.Carousel;

/// <summary>
/// El manual como un libro en 3D: abierto sobre su tapa, ligeramente tumbado, con la página izquierda y la derecha. Al
/// pasar página, la hoja gira sobre el lomo y se curva (el borde va por delante), con la página siguiente por detrás,
/// como un cuaderno impreso. Solo tiene en memoria las páginas que se ven (y las de la hoja que pasa).
/// </summary>
public sealed class BookView : ContentControl
{
    const int Columns = 28;                  // Tiras de la hoja que se curva (más, más suave).
    const double Curl = 0.95;                // Cuánto se adelanta el borde al girar.
    const double Tilt = -20;                 // Lo tumbado que está el libro sobre la mesa.
    static readonly TimeSpan TurnTime = TimeSpan.FromMilliseconds(650);
    static readonly Color Paper = Color.FromRgb(0xF3, 0xEF, 0xE6);

    // Recortado a su zona: con zoom, el libro no tapa los botones de arriba ni de abajo.
    readonly Viewport3D _viewport = new() { ClipToBounds = true };
    readonly PerspectiveCamera _camera = new() { FieldOfView = 38, UpDirection = new Vector3D(0, 1, 0) };
    readonly GeometryModel3D _left = new(), _right = new(), _board = new(), _sheetFront = new(), _sheetBack = new();
    readonly MeshGeometry3D _frontMesh = new(), _backMesh = new();
    readonly Model3DGroup _sheet = new();
    double _width = 0.72;                    // Ancho de una página (el alto es 1).

    bool _turning;
    double _angle;                           // 0: la hoja plana a la derecha; π: plana a la izquierda.
    double _from, _to;
    int _direction;
    TimeSpan _start;
    Action? _finish;

    public BookView()
    {
        Content = _viewport;
        _viewport.Camera = _camera;

        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(0x9A, 0x9A, 0x9A)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(0x7A, 0x7A, 0x76), new Vector3D(-0.35, -0.5, -1)));

        var book = new Model3DGroup { Transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), Tilt)) };
        book.Children.Add(_board);
        book.Children.Add(_left);
        book.Children.Add(_right);
        _sheetFront.Geometry = _frontMesh;
        _sheetBack.Geometry = _backMesh;
        _sheet.Children.Add(_sheetFront);
        _sheet.Children.Add(_sheetBack);
        book.Children.Add(_sheet);

        var scene = new Model3DGroup();
        scene.Children.Add(lights);
        scene.Children.Add(book);
        _viewport.Children.Add(new ModelVisual3D { Content = scene });

        // Las páginas en plano y la tapa (oscura, un poco más grande, por debajo).
        _left.Geometry = Quad(-_width, 0, 0.0005);
        _right.Geometry = Quad(0, _width, 0.0005);
        _board.Geometry = Quad(-_width - 0.03, _width + 0.03, -0.004, 1.06);
        _board.Material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x2A, 0x23, 0x1C)));
        _sheet.Children.Clear();
        SizeChanged += (_, _) => UpdateCamera();
        UpdateCamera();
    }

    /// <summary>Proporción de las páginas (ancho / alto), para que no se deformen.</summary>
    public double PageAspect
    {
        get => _width;
        set
        {
            _width = Math.Clamp(value, 0.4, 1.6);
            _left.Geometry = Quad(-_width, 0, 0.0005);
            _right.Geometry = Quad(0, _width, 0.0005);
            _board.Geometry = Quad(-_width - 0.03, _width + 0.03, -0.004, 1.06);
            UpdateCamera();
        }
    }

    public bool IsTurning => _turning;

    /// <summary>Las dos páginas a la vista (null: no hay página; en la portada, la izquierda).</summary>
    public void Show(ImageSource? left, ImageSource? right)
    {
        Finish();
        _left.Material = left is null ? null : PageMaterial(left);
        _right.Material = right is null ? null : PageMaterial(right);
    }

    /// <summary>
    /// Pasa una hoja. Hacia delante: la hoja es la página derecha (<paramref name="front"/>) con la siguiente por detrás
    /// (<paramref name="back"/>), y debajo quedan las nuevas. Hacia atrás, al revés. Al acabar se quedan las de debajo.
    /// </summary>
    public void Turn(int direction, ImageSource? front, ImageSource? back, ImageSource? underLeft, ImageSource? underRight)
    {
        Finish();
        _left.Material = underLeft is null ? null : PageMaterial(underLeft);
        _right.Material = underRight is null ? null : PageMaterial(underRight);
        _sheetFront.Material = PageMaterial(front);
        _sheetBack.Material = PageMaterial(back);
        if (!_sheet.Children.Contains(_sheetFront))
        {
            _sheet.Children.Add(_sheetFront);
            _sheet.Children.Add(_sheetBack);
        }

        _direction = direction;
        (_from, _to) = direction > 0 ? (0.0, Math.PI) : (Math.PI, 0.0);
        _angle = _from;
        UpdateSheet();
        // Al acabar, la hoja queda plana del otro lado: su cara de detrás (o la de delante, al volver) pasa a ser esa página.
        var landed = direction > 0 ? back : front;
        _finish = () =>
        {
            _sheet.Children.Clear();
            if (direction > 0)
                _left.Material = landed is null ? null : PageMaterial(landed);
            else
                _right.Material = landed is null ? null : PageMaterial(landed);
        };
        _turning = true;
        _start = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    /// <summary>Acaba al momento la hoja que se está pasando (si se pide otra antes de que termine).</summary>
    public void Finish()
    {
        if (!_turning)
            return;
        _turning = false;
        CompositionTarget.Rendering -= OnRendering;
        _finish?.Invoke();
        _finish = null;
    }

    void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (_start == TimeSpan.Zero)
            _start = now;
        var t = Math.Clamp((now - _start).TotalMilliseconds / TurnTime.TotalMilliseconds, 0, 1);
        // Empieza y acaba despacio, como una hoja de verdad.
        var eased = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
        _angle = _from + (_to - _from) * eased;
        UpdateSheet();
        if (t >= 1)
            Finish();
    }

    /// <summary>
    /// La hoja curvada: tiras desde el lomo hasta el borde, cada una un poco más girada que la anterior (el borde va por
    /// delante), sin pasar nunca por debajo de las páginas. Por delante, una página; por detrás, la otra.
    /// </summary>
    void UpdateSheet()
    {
        var lift = Math.Sin(_angle);
        var points = new Point[Columns + 1];
        double x = 0, z = 0.002;
        points[0] = new Point(x, z);
        var step = _width / Columns;
        for (var j = 0; j < Columns; j++)
        {
            var u = (j + 0.5) / Columns;
            var a = Math.Clamp(_angle + _direction * Curl * lift * Math.Pow(u, 1.6), 0.0005, Math.PI - 0.0005);
            x += Math.Cos(a) * step;
            z += Math.Sin(a) * step;
            points[j + 1] = new Point(x, Math.Max(z, 0.002));
        }
        Build(_frontMesh, points, flipped: false);
        Build(_backMesh, points, flipped: true);
    }

    static void Build(MeshGeometry3D mesh, Point[] points, bool flipped)
    {
        var positions = new Point3DCollection((points.Length) * 2);
        var coordinates = new PointCollection(points.Length * 2);
        var indices = new Int32Collection(Columns * 6);
        for (var k = 0; k < points.Length; k++)
        {
            var u = (double)k / Columns;
            positions.Add(new Point3D(points[k].X, -0.5, points[k].Y));
            positions.Add(new Point3D(points[k].X, 0.5, points[k].Y));
            var tu = flipped ? 1 - u : u;
            coordinates.Add(new Point(tu, 1));
            coordinates.Add(new Point(tu, 0));
        }
        for (var k = 0; k < Columns; k++)
        {
            int b0 = k * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
            if (flipped)
            {
                indices.Add(b0); indices.Add(t1); indices.Add(b1);
                indices.Add(b0); indices.Add(t0); indices.Add(t1);
            }
            else
            {
                indices.Add(b0); indices.Add(b1); indices.Add(t1);
                indices.Add(b0); indices.Add(t1); indices.Add(t0);
            }
        }
        mesh.Positions = positions;
        mesh.TextureCoordinates = coordinates;
        mesh.TriangleIndices = indices;
    }

    /// <summary>Un rectángulo plano de <paramref name="x0"/> a <paramref name="x1"/>, de alto 1 (o el dado), a esa altura.</summary>
    static MeshGeometry3D Quad(double x0, double x1, double z, double height = 1)
    {
        var h = height / 2;
        return new MeshGeometry3D
        {
            Positions = [new Point3D(x0, -h, z), new Point3D(x1, -h, z), new Point3D(x1, h, z), new Point3D(x0, h, z)],
            TextureCoordinates = [new Point(0, 1), new Point(1, 1), new Point(1, 0), new Point(0, 0)],
            TriangleIndices = [0, 1, 2, 0, 2, 3],
        };
    }

    static Material PageMaterial(ImageSource? image) =>
        new DiffuseMaterial(image is null ? new SolidColorBrush(Paper) : new ImageBrush(image) { Stretch = Stretch.Fill });

    /// <summary>La cámara, lo justo para que quepa el libro abierto (con un margen) en el tamaño que haya.</summary>
    void UpdateCamera()
    {
        double width = Math.Max(1, ActualWidth), height = Math.Max(1, ActualHeight);
        var half = Math.Tan(_camera.FieldOfView * Math.PI / 360);
        var distance = Math.Max((_width + 0.08) / half, 0.62 / (half * height / width)) / _zoom;
        _camera.Position = new Point3D(_pan.X, -0.05 + _pan.Y, distance);
        _camera.LookDirection = new Vector3D(0, 0.05, -distance);
    }

    // ---- Zoom (para las pantallas pequeñas) ----

    public const double MinZoom = 1, MaxZoom = 4;
    double _zoom = 1;
    Vector _pan;

    /// <summary>Lo cerca que se ve el libro (1 = entero). Con zoom, se mueve arrastrándolo (ver <see cref="PanBy"/>).</summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Math.Clamp(value, MinZoom, MaxZoom);
            ClampPan();
            UpdateCamera();
        }
    }

    /// <summary>Mueve el libro con el ratón: <paramref name="pixels"/> es lo arrastrado en pantalla.</summary>
    public void PanBy(Vector pixels)
    {
        if (_zoom <= MinZoom || ActualWidth <= 0)
            return;
        // Lo que se ve de ancho en unidades del libro, para que el libro siga al ratón.
        var half = Math.Tan(_camera.FieldOfView * Math.PI / 360);
        var visible = 2 * _camera.Position.Z * half;
        _pan -= new Vector(pixels.X, -pixels.Y) * (visible / ActualWidth);
        ClampPan();
        UpdateCamera();
    }

    /// <summary>Acerca hacia un punto de la pantalla (donde está el ratón), como en un visor de fotos.</summary>
    public void ZoomAt(double zoom, Point point)
    {
        var before = ToBook(point);
        Zoom = zoom;
        var after = ToBook(point);
        _pan += before - after;
        ClampPan();
        UpdateCamera();
    }

    /// <summary>Un punto de la pantalla en unidades del libro (aproximado, sin la inclinación).</summary>
    Vector ToBook(Point point)
    {
        var half = Math.Tan(_camera.FieldOfView * Math.PI / 360);
        var visible = 2 * _camera.Position.Z * half;
        var scale = visible / Math.Max(1, ActualWidth);
        return new Vector(_pan.X + (point.X - ActualWidth / 2) * scale, _pan.Y - (point.Y - ActualHeight / 2) * scale);
    }

    /// <summary>Sin salirse del libro: cuanto más zoom, más se puede mover.</summary>
    void ClampPan()
    {
        var room = 1 - 1 / _zoom;
        _pan = new Vector(Math.Clamp(_pan.X, -(_width + 0.05) * room, (_width + 0.05) * room), Math.Clamp(_pan.Y, -0.55 * room, 0.55 * room));
    }

    /// <summary>True si el punto (en coordenadas de este control) está en la mitad izquierda del libro.</summary>
    public bool IsLeftHalf(Point point) => point.X < ActualWidth / 2;
}
