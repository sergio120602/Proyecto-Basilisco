using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Basilisco.Models;

namespace Basilisco.Carousel;

/// <summary>
/// La caja abierta en 3D: la tapa gira sobre el lomo y queda a la izquierda enseñando el manual, y a la derecha
/// queda la bandeja con el disco. Al jugar, el disco empieza a girar cada vez más deprisa. Con un cartucho, al jugar
/// el cartucho sale de su hueco y se eleva, la caja se cierra y se tumba, y el cartucho se mete en ella por arriba,
/// como en la ranura de una consola (al acabar de jugar, lo mismo al revés).
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
    // La bandeja con el disco, que pasa como la hoja de plástico de un libro para llegar a la banda sonora.
    readonly AxisAngleRotation3D _tray = new(new Vector3D(0, 1, 0), 0);
    const double TraySeconds = 0.85;
    // La bisagra de la hoja: en el lomo, pegada a la tapa, un pelo por encima para que al tumbarse quede sobre ella.
    const double TrayHinge = 0.003;
    double _trayTurn, _trayTarget;
    Action? _trayDone;
    GeometryModel3D? _trayFace;
    Material? _trayFaceBack;
    readonly TranslateTransform3D _lift = new();
    bool _cartridge;
    readonly TranslateTransform3D _shift = new();

    // Meter el cartucho en la caja: 0 en su hueco de la bandeja, 1 dentro de la caja cerrada y tumbada.
    const double InsertSeconds = 2.3;
    const double RemoveSeconds = 1.9;
    const double TiltDegrees = 62;
    readonly AxisAngleRotation3D _tilt = new(new Vector3D(1, 0, 0), 0);
    double _insert, _insertTarget;
    Point _cartCenter;
    Size _cartHalf;

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
    /// <param name="content">El disco (gira al jugar) o el cartucho (se levanta, como al sacarlo para meterlo en la consola).</param>
    public void Show(BoxShape shape, BitmapSource cover, BitmapSource inside, BitmapSource tray, CaseContent content)
    {
        _cartridge = content.Cartridge;
        _shape = shape;
        double w = shape.Width, h = shape.Height, d = Math.Max(shape.Depth, 0.03);
        var plastic = new SolidColorBrush(Color.FromRgb(0x20, 0x22, 0x27));

        // La base: la bandeja (al fondo, z = 0) y sus paredes hasta media altura.
        var bottom = new Model3DGroup();
        _trayFace = Face(new(0, 0, 0), new(w, 0, 0), new(w, h, 0), new(0, h, 0), new ImageBrush(tray));
        _trayFaceBack = _trayFace.BackMaterial;
        bottom.Children.Add(_trayFace);
        var wall = d * 0.5;
        bottom.Children.Add(Face(new(0, 0, wall), new(w, 0, wall), new(w, 0, 0), new(0, 0, 0), plastic));          // abajo
        bottom.Children.Add(Face(new(0, h, 0), new(w, h, 0), new(w, h, wall), new(0, h, wall), plastic));          // arriba
        bottom.Children.Add(Face(new(w, 0, 0), new(w, 0, wall), new(w, h, wall), new(w, h, 0), plastic));          // derecha
        bottom.Children.Add(Face(new(0, 0, wall), new(0, 0, 0), new(0, h, 0), new(0, h, wall), plastic));          // lomo por dentro

        // La tapa: la portada por fuera (mirando al frente cuando está cerrada) y el manual por dentro.
        var lid = new Model3DGroup();
        lid.Children.Add(Face(new(0, 0, d), new(w, 0, d), new(w, h, d), new(0, h, d), new ImageBrush(cover)));
        _inside = new ImageBrush(inside);
        lid.Children.Add(Face(new(w, 0, d - 0.002), new(0, 0, d - 0.002), new(0, h, d - 0.002), new(w, h, d - 0.002), _inside));
        // Las paredes de la tapa (la otra mitad del canto): abajo y a la derecha. Arriba no, para que quede la ranura por
        // donde entra el cartucho.
        lid.Children.Add(Face(new(0, 0, d), new(w, 0, d), new(w, 0, wall), new(0, 0, wall), plastic));
        lid.Children.Add(Face(new(w, 0, wall), new(w, 0, d), new(w, h, d), new(w, h, wall), plastic));
        // Gira sobre el lomo: el eje está en x = 0, a media altura de la caja.
        lid.Transform = new RotateTransform3D(_lid, new Point3D(0, 0, d / 2));

        // El disco o el cartucho, encima de la bandeja. El disco gira sobre su centro; el cartucho se levanta.
        var (rx, ry) = (content.Size.Width / 2, content.Size.Height / 2);
        var center = OpenCaseArt.ContentCenter(content.Cartridge);
        // En la escena la altura crece hacia arriba; en las texturas, hacia abajo.
        var (cx, cy) = (w * center.X, h * (1 - center.Y));
        Model3D discModel = content.Cartridge
            ? Cartridge(content, cx, cy, rx, ry)
            : Face(new(cx - rx, cy - ry, 0.004), new(cx + rx, cy - ry, 0.004),
                   new(cx + rx, cy + ry, 0.004), new(cx - rx, cy + ry, 0.004), new ImageBrush(content.Image));
        _cartCenter = new Point(cx, cy);
        _cartHalf = new Size(rx, ry);
        _lift.OffsetX = _lift.OffsetY = _lift.OffsetZ = 0;
        discModel.Transform = new Transform3DGroup { Children = { new RotateTransform3D(_disc, new Point3D(cx, cy, 0)), _lift } };

        // Lo transparente (el disco) va al final para que se vea lo que hay detrás. La bandeja y el disco giran juntos
        // sobre el lomo, como una hoja (para pasar a la banda sonora): la bisagra está en el borde, pegada a la tapa, así
        // que no se separa de ella, y al acabar queda tumbada justo encima.
        var trayPage = new Model3DGroup { Transform = new RotateTransform3D(_tray, new Point3D(0, 0, TrayHinge)) };
        trayPage.Children.Add(bottom);
        trayPage.Children.Add(discModel);
        // Primero se centra (cerrada) y después se tumba hacia atrás sobre su borde de abajo (al meter el cartucho).
        var group = new Model3DGroup { Transform = new Transform3DGroup { Children = { _shift, new RotateTransform3D(_tilt, new Point3D(0, 0, 0)) } } };
        group.Children.Add(lid);
        group.Children.Add(trayPage);
        _content.Content = group;

        _open = 0;
        _openTarget = 1;
        _trayTurn = _trayTarget = 0;
        _tray.Angle = 0;
        _trayDone = null;
        _spin = 0;
        _spinning = false;
        _disc.Angle = 0;
        _insert = _insertTarget = 0;
        _tilt.Angle = 0;
        _closed = null;
        Apply();
        UpdateCamera();
        StartAnimation();
    }

    /// <summary>El disco empieza a girar.</summary>
    ImageBrush? _inside;

    /// <summary>Cambia el interior de la tapa (cuando se acaba de crear el manual).</summary>
    public void SetInside(BitmapSource inside)
    {
        if (_inside is not null)
            _inside.ImageSource = inside;
    }

    public void Spin()
    {
        _spinning = true;
        _stopping = false;
        _spinTime = 0;
        _insertTarget = 1;
        StartAnimation();
    }

    /// <summary>Lo que hay que esperar al darle a Jugar antes de lanzar el juego: lo que tarda en girar el disco o en meterse el cartucho.</summary>
    public TimeSpan LaunchDelay => TimeSpan.FromSeconds(_cartridge ? InsertSeconds + 0.15 : 1.3);

    /// <summary>
    /// El cartucho de un cartucho con grosor: la pegatina delante, el mismo dibujo detrás y los cantos del color de su
    /// plástico (un poco metidos, para no asomar por las esquinas redondeadas).
    /// </summary>
    static Model3DGroup Cartridge(CaseContent content, double cx, double cy, double rx, double ry)
    {
        const double back = 0.004;
        var front = back + Math.Max(0.006, content.Thickness);
        var image = new ImageBrush(content.Image);
        var edge = new SolidColorBrush(content.Body);
        double ix = rx * 0.94, iy = ry * 0.94;
        var model = new Model3DGroup();
        model.Children.Add(Face(new(cx - rx, cy - ry, front), new(cx + rx, cy - ry, front), new(cx + rx, cy + ry, front), new(cx - rx, cy + ry, front), image));
        model.Children.Add(Edges());
        return model;

        Model3DGroup Edges()
        {
            var sides = new Model3DGroup();
            sides.Children.Add(Face(new(cx - ix, cy - iy, back), new(cx + ix, cy - iy, back), new(cx + ix, cy - iy, front), new(cx - ix, cy - iy, front), edge));   // abajo
            sides.Children.Add(Face(new(cx - ix, cy + iy, front), new(cx + ix, cy + iy, front), new(cx + ix, cy + iy, back), new(cx - ix, cy + iy, back), edge));   // arriba
            sides.Children.Add(Face(new(cx - ix, cy - iy, back), new(cx - ix, cy - iy, front), new(cx - ix, cy + iy, front), new(cx - ix, cy + iy, back), edge));   // izquierda
            sides.Children.Add(Face(new(cx + ix, cy - iy, front), new(cx + ix, cy - iy, back), new(cx + ix, cy + iy, back), new(cx + ix, cy + iy, front), edge));   // derecha
            sides.Children.Add(Face(new(cx + rx, cy - ry, back), new(cx - rx, cy - ry, back), new(cx - rx, cy + ry, back), new(cx + rx, cy + ry, back), image));    // detrás
            return sides;
        }
    }

    // Frenando: el disco pierde velocidad poco a poco (o el cartucho vuelve a bajar) y después se avisa.
    bool _stopping;
    double _stopTime;
    Action? _stopped;
    const double StopSeconds = 2.4;

    /// <summary>
    /// Pasa la hoja de plástico donde está el disco (hacia la izquierda, para ver la banda sonora) o la devuelve a su
    /// sitio, y avisa al acabar.
    /// </summary>
    public void TurnTray(bool turned, Action done)
    {
        _trayTarget = turned ? 1 : 0;
        if (_trayTurn == _trayTarget)
        {
            done();
            return;
        }
        _trayDone = done;
        StartAnimation();
    }

    public bool TrayTurned => _trayTarget == 1;

    /// <summary>
    /// Lo que se ve por detrás de la hoja del disco al pasarla (la otra cara de la página, sin luces, tal cual), o null
    /// para dejarla como era. Se ve al revés, porque la hoja se da la vuelta: aquí se le da la vuelta de nuevo.
    /// </summary>
    public void SetTrayBack(Brush? brush)
    {
        if (_trayFace is null)
            return;
        if (brush is null)
        {
            _trayFace.BackMaterial = _trayFaceBack;
            return;
        }
        brush.RelativeTransform = new ScaleTransform(-1, 1, 0.5, 0.5);
        _trayFace.BackMaterial = new MaterialGroup { Children = { new DiffuseMaterial(Brushes.Black), new EmissiveMaterial(brush) } };
    }

    /// <summary>El disco deja de girar poco a poco (el cartucho vuelve a la caja) y avisa al acabar.</summary>
    public void StopSpin(Action stopped)
    {
        if (!_spinning)
        {
            stopped();
            return;
        }
        _insertTarget = 0;
        _stopping = true;
        _stopTime = 0;
        _stopped = stopped;
        StartAnimation();
    }

    bool _paused;

    /// <summary>
    /// Mientras juegas, Basilisco no se ve: la animación se para para no gastar la gráfica y sigue donde iba al volver.
    /// </summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value)
                return;
            _paused = value;
            if (value)
                StopAnimation();
            else if (_spinning || _open != _openTarget)
                StartAnimation();
        }
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
        _spinning = _stopping = _paused = false;
        _stopped = null;
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

        if (_trayTurn != _trayTarget)
        {
            var trayStep = dt / TraySeconds;
            _trayTurn = _trayTarget > _trayTurn ? Math.Min(_trayTarget, _trayTurn + trayStep) : Math.Max(_trayTarget, _trayTurn - trayStep);
            if (_trayTurn == _trayTarget && _trayDone is { } trayDone)
            {
                _trayDone = null;
                trayDone();
            }
        }

        if (_spinning && _cartridge)
        {
            // El cartucho: entra en la caja (o sale y vuelve a su hueco, al acabar de jugar) y avisa al terminar.
            _insert = _insertTarget > _insert ? Math.Min(_insertTarget, _insert + dt / InsertSeconds) : Math.Max(_insertTarget, _insert - dt / RemoveSeconds);
            if (_stopping && _insert == 0)
            {
                _spinning = _stopping = false;
                var stopped = _stopped;
                _stopped = null;
                stopped?.Invoke();
            }
        }
        else if (_spinning)
        {
            _spinTime += dt;
            // Al frenar, de la velocidad que llevaba a cero, cada vez más despacio.
            var brake = 1.0;
            if (_stopping)
            {
                _stopTime += dt;
                brake = 1 - Ease(Math.Min(1, _stopTime / StopSeconds));
            }
            _spin = SpinSpeed * Math.Min(1, _spinTime / SpinRamp) * brake;
            _disc.Angle = (_disc.Angle - _spin * 360 * dt) % 360;
            if (_stopping && _stopTime >= StopSeconds)
            {
                _spinning = _stopping = false;
                var stopped = _stopped;
                _stopped = null;
                stopped?.Invoke();
            }
        }
        Apply();

        if (_open == _openTarget && _openTarget == 0 && _closed is { } closed)
        {
            _closed = null;
            StopAnimation();
            closed();
        }
        else if (_open == _openTarget && _trayTurn == _trayTarget && (!_spinning || _cartridge && _insert == _insertTarget))
        {
            // Con el cartucho ya metido no hay nada que mover mientras juegas.
            StopAnimation();
        }
    }

    void Apply()
    {
        // Al meter el cartucho, la caja se cierra (y se tumba) aunque esté abierta.
        var close = _cartridge ? Stage(0.32, 0.6) : 0;
        var t = Ease(_open) * (1 - close);
        // Cerrada se ve centrada; abierta, el lomo queda en el centro con la tapa a la izquierda.
        _lid.Angle = -180 * t;
        // La hoja pasa hasta tumbarse sobre la tapa (por la bisagra, queda justo encima, sin pisarla).
        _tray.Angle = -180 * Ease(_trayTurn);
        _shift.OffsetX = -_shape.Width / 2 * (1 - t);
        if (!_cartridge)
            return;
        UpdateCamera();
        _tilt.Angle = -TiltDegrees * Stage(0.42, 0.76);

        // El cartucho, en las medidas de la caja (antes de tumbarla): sale hacia delante, sube por encima de la caja
        // (lejos de la tapa, que se cierra), baja a la altura de la ranura (entre la bandeja y la tapa) y entra por arriba
        // hasta dejar fuera un tercio, como en una consola.
        double w = _shape.Width, h = _shape.Height, d = Math.Max(_shape.Depth, 0.03);
        var rise = Stage(0, 0.2);
        var up = Stage(0.12, 0.38);
        var align = Stage(0.55, 0.76);
        var insert = Stage(0.76, 1);
        var above = h + _cartHalf.Height + 0.05;
        var inside = h - 0.3 * _cartHalf.Height;
        _lift.OffsetX = (w / 2 - _cartCenter.X) * up;
        _lift.OffsetY = (above - _cartCenter.Y) * up + (inside - above) * insert;
        _lift.OffsetZ = 0.22 * rise * (1 - align) + (d * 0.52 - 0.004) * align;
    }

    /// <summary>Lo que va de un tramo de la animación del cartucho (de <paramref name="from"/> a <paramref name="to"/>), suavizado.</summary>
    double Stage(double from, double to) => Ease(Math.Clamp((_insert - from) / (to - from), 0, 1));

    static double Ease(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    /// <summary>La cámara encuadra la caja abierta entera (las dos mitades) con un poco de aire alrededor.</summary>
    /// <summary>
    /// Dónde se ve en pantalla la caja abierta (las dos mitades, de la tapa a la bandeja), en coordenadas del viewport:
    /// para poner encima, en su sitio, otra doble página (la banda sonora).
    /// </summary>
    public Rect OpenSpreadBounds()
    {
        double width = _viewport.ActualWidth, height = _viewport.ActualHeight;
        if (width <= 0 || height <= 0 || _shape is null)
            return Rect.Empty;
        var forward = _camera.LookDirection;
        forward.Normalize();
        var right = Vector3D.CrossProduct(forward, _camera.UpDirection);
        right.Normalize();
        var up = Vector3D.CrossProduct(right, forward);
        var tan = Math.Tan(_camera.FieldOfView * Math.PI / 360);
        Point Project(Point3D point)
        {
            var v = point - _camera.Position;
            var depth = Vector3D.DotProduct(v, forward);
            var x = Vector3D.DotProduct(v, right) / (depth * tan);
            var y = Vector3D.DotProduct(v, up) / (depth * tan);
            return new Point(width / 2 + x * width / 2, height / 2 - y * width / 2);
        }
        double w = _shape.Width, h = _shape.Height;
        var corners = new[] { Project(new(-w, 0, 0)), Project(new(w, 0, 0)), Project(new(w, h, 0)), Project(new(-w, h, 0)) };
        var left = corners.Min(p => p.X);
        var top = corners.Min(p => p.Y);
        return new Rect(left, top, corners.Max(p => p.X) - left, corners.Max(p => p.Y) - top);
    }

    void UpdateCamera()
    {
        var aspect = _viewport.ActualWidth > 0 && _viewport.ActualHeight > 0 ? _viewport.ActualWidth / _viewport.ActualHeight : 16.0 / 9;
        // En WPF el campo de visión de la cámara es el horizontal.
        var halfFov = _camera.FieldOfView / 2 * Math.PI / 180;
        var needWidth = _shape.Width * 2 * 1.12;
        var needHeight = _shape.Height * 1.12;
        var centerY = _shape.Height / 2;
        if (_cartridge && _insert > 0)
        {
            // Mientras el cartucho entra: la caja (cerrándose, más estrecha) con el cartucho por encima, sin que se salga.
            var top = _shape.Height + 2 * _cartHalf.Height + 0.08;
            var above = Stage(0.06, 0.36);
            var closed = Stage(0.32, 0.6);
            needWidth += (_shape.Width * 1.6 - needWidth) * closed;
            needHeight += (top * 1.1 - needHeight) * above;
            centerY += (top / 2 - centerY) * above;
            // Ya tumbada se ve más baja: la cámara se acerca a ella y al cartucho que asoma.
            var lying = Stage(0.42, 0.85);
            needHeight += (_shape.Height * 0.9 - needHeight) * lying;
            centerY += (_shape.Height * 0.36 - centerY) * lying;
        }
        var width = Math.Max(needWidth, needHeight * aspect);
        var distance = width / 2 / Math.Tan(halfFov);
        var center = new Point3D(0, centerY, 0);
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
