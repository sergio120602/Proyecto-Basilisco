using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Basilisco.Models;

namespace Basilisco.Carousel;

/// <summary>
/// La caja abierta en 3D: la tapa gira sobre el lomo y queda a la izquierda enseñando el manual, y a la derecha
/// queda la bandeja con el disco. Al jugar, el disco empieza a girar cada vez más deprisa.
/// </summary>
public sealed class OpenCaseScene
{
    const double OpenSeconds = 0.9;
    const double CloseSeconds = 0.45;

    // Vueltas por segundo del disco a toda velocidad, y lo que tarda en llegar.
    const double SpinSpeed = 2.2;
    const double SpinRamp = 1.2;

    readonly Viewport3D _viewport;
    readonly ModelVisual3D _content = new();
    readonly PerspectiveCamera _camera;

    readonly AxisAngleRotation3D _lid = new(new Vector3D(0, 1, 0), 0);
    readonly AxisAngleRotation3D _disc = new(new Vector3D(0, 0, 1), 0);
    readonly TranslateTransform3D _shift = new();

    BoxShape _shape = BoxShape.Square;
    double _open;           // 0 cerrada, 1 abierta
    double _openTarget;
    double _spin;           // vueltas por segundo
    bool _spinning;
    double _spinTime;
    TimeSpan _lastFrame;
    bool _animating;
    Action? _closed;

    public OpenCaseScene(Viewport3D viewport)
    {
        _viewport = viewport;
        _camera = new PerspectiveCamera { FieldOfView = 34, UpDirection = new Vector3D(0, 1, 0) };
        _viewport.Camera = _camera;
        _viewport.Children.Add(new ModelVisual3D
        {
            Content = new Model3DGroup
            {
                Children =
                {
                    new AmbientLight(Color.FromRgb(0xA8, 0xA8, 0xA8)),
                    new DirectionalLight(Color.FromRgb(0x80, 0x80, 0x80), new Vector3D(-0.3, -0.5, -1)),
                },
            },
        });
        _viewport.Children.Add(_content);
        _viewport.SizeChanged += (_, _) => UpdateCamera();
    }

    public bool IsSpinning => _spinning;

    /// <param name="cover">La portada tal como se ve en el carrusel (por fuera de la tapa).</param>
    public void Show(BoxShape shape, BitmapSource cover, BitmapSource inside, BitmapSource tray, BitmapSource disc, double discDiameter)
    {
        _shape = shape;
        double w = shape.Width, h = shape.Height, d = Math.Max(shape.Depth, 0.03);
        var plastic = new SolidColorBrush(Color.FromRgb(0x20, 0x22, 0x27));

        // La base: la bandeja (al fondo, z = 0) y sus paredes hasta media altura.
        var bottom = new Model3DGroup();
        bottom.Children.Add(Face(new(0, 0, 0), new(w, 0, 0), new(w, h, 0), new(0, h, 0), new ImageBrush(tray)));
        var wall = d * 0.5;
        bottom.Children.Add(Face(new(0, 0, wall), new(w, 0, wall), new(w, 0, 0), new(0, 0, 0), plastic));          // abajo
        bottom.Children.Add(Face(new(0, h, 0), new(w, h, 0), new(w, h, wall), new(0, h, wall), plastic));          // arriba
        bottom.Children.Add(Face(new(w, 0, 0), new(w, 0, wall), new(w, h, wall), new(w, h, 0), plastic));          // derecha
        bottom.Children.Add(Face(new(0, 0, wall), new(0, 0, 0), new(0, h, 0), new(0, h, wall), plastic));          // lomo por dentro

        // La tapa: la portada por fuera (mirando al frente cuando está cerrada) y el manual por dentro.
        var lid = new Model3DGroup();
        lid.Children.Add(Face(new(0, 0, d), new(w, 0, d), new(w, h, d), new(0, h, d), new ImageBrush(cover)));
        lid.Children.Add(Face(new(w, 0, d - 0.002), new(0, 0, d - 0.002), new(0, h, d - 0.002), new(w, h, d - 0.002), new ImageBrush(inside)));
        // Gira sobre el lomo: el eje está en x = 0, a media altura de la caja.
        lid.Transform = new RotateTransform3D(_lid, new Point3D(0, 0, d / 2));

        // El disco, encima de la bandeja; gira sobre su centro.
        var r = discDiameter / 2;
        var discModel = Face(new(w / 2 - r, h / 2 - r, 0.004), new(w / 2 + r, h / 2 - r, 0.004),
                             new(w / 2 + r, h / 2 + r, 0.004), new(w / 2 - r, h / 2 + r, 0.004), new ImageBrush(disc));
        discModel.Transform = new RotateTransform3D(_disc, new Point3D(w / 2, h / 2, 0));

        // Lo transparente (el disco) va al final para que se vea lo que hay detrás.
        var group = new Model3DGroup { Transform = _shift };
        group.Children.Add(bottom);
        group.Children.Add(lid);
        group.Children.Add(discModel);
        _content.Content = group;

        _open = 0;
        _openTarget = 1;
        _spin = 0;
        _spinning = false;
        _disc.Angle = 0;
        _closed = null;
        Apply();
        UpdateCamera();
        StartAnimation();
    }

    /// <summary>El disco empieza a girar.</summary>
    public void Spin()
    {
        _spinning = true;
        _spinTime = 0;
        StartAnimation();
    }

    /// <summary>Cierra la tapa y avisa al acabar.</summary>
    public void Close(Action closed)
    {
        _openTarget = 0;
        _closed = closed;
        StartAnimation();
    }

    public void Clear()
    {
        StopAnimation();
        _content.Content = null;
        _spinning = false;
    }

    void StartAnimation()
    {
        if (_animating)
            return;
        _animating = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    void StopAnimation()
    {
        if (!_animating)
            return;
        _animating = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        var dt = _lastFrame == TimeSpan.Zero ? 0 : Math.Min(0.05, (now - _lastFrame).TotalSeconds);
        _lastFrame = now;

        // Abrir y cerrar a ritmo constante; la curva suave se aplica al dibujar.
        var step = dt / (_openTarget > _open ? OpenSeconds : CloseSeconds);
        _open = _openTarget > _open ? Math.Min(_openTarget, _open + step) : Math.Max(_openTarget, _open - step);

        if (_spinning)
        {
            _spinTime += dt;
            _spin = SpinSpeed * Math.Min(1, _spinTime / SpinRamp);
            _disc.Angle = (_disc.Angle - _spin * 360 * dt) % 360;
        }
        Apply();

        if (_open == _openTarget && _openTarget == 0 && _closed is { } closed)
        {
            _closed = null;
            StopAnimation();
            closed();
        }
        else if (_open == _openTarget && !_spinning)
        {
            StopAnimation();
        }
    }

    void Apply()
    {
        var t = Ease(_open);
        // Cerrada se ve centrada; abierta, el lomo queda en el centro con la tapa a la izquierda.
        _lid.Angle = -180 * t;
        _shift.OffsetX = -_shape.Width / 2 * (1 - t);
    }

    static double Ease(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    /// <summary>La cámara encuadra la caja abierta entera (las dos mitades) con un poco de aire alrededor.</summary>
    void UpdateCamera()
    {
        var aspect = _viewport.ActualWidth > 0 && _viewport.ActualHeight > 0 ? _viewport.ActualWidth / _viewport.ActualHeight : 16.0 / 9;
        // En WPF el campo de visión de la cámara es el horizontal.
        var halfFov = _camera.FieldOfView / 2 * Math.PI / 180;
        var needWidth = _shape.Width * 2 * 1.12;
        var needHeight = _shape.Height * 1.12;
        var width = Math.Max(needWidth, needHeight * aspect);
        var distance = width / 2 / Math.Tan(halfFov);
        var center = new Point3D(0, _shape.Height / 2, 0);
        _camera.Position = new Point3D(center.X, center.Y + distance * 0.12, distance);
        _camera.LookDirection = center - _camera.Position;
    }

    static GeometryModel3D Face(Point3D bottomLeft, Point3D bottomRight, Point3D topRight, Point3D topLeft, Brush brush)
    {
        var mesh = new MeshGeometry3D
        {
            Positions = [bottomLeft, bottomRight, topRight, topLeft],
            TextureCoordinates = [new Point(0, 1), new Point(1, 1), new Point(1, 0), new Point(0, 0)],
            TriangleIndices = [0, 1, 2, 0, 2, 3],
        };
        mesh.Freeze();
        var material = new DiffuseMaterial(brush);
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }
}
