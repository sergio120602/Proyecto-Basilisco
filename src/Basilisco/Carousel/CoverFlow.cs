using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Basilisco.Carousel;

public sealed record CarouselItem(string Title, bool IsAddCard);

/// <summary>
/// Carrusel 3D de portadas: la seleccionada de frente y el resto girado y apilado a los lados,
/// con reflejo en el suelo. La posición es continua y persigue al índice seleccionado,
/// así que pasar de una portada a otra siempre es un deslizamiento suave.
/// </summary>
public sealed class CoverFlow
{
    public const double CoverWidth = 1.15;
    public const double CoverHeight = 1.0;

    static readonly Point3D CameraPosition = new(0, 0.7, 3.7);
    static readonly Point3D CameraTarget = new(0, 0.44, 0);
    const double FieldOfView = 55;

    // Disposición de las portadas laterales.
    const double NeighborOffsetX = 1.15;
    const double StackSpacingX = 0.24;
    const double NeighborDepth = 0.55;
    const double StackSpacingDepth = 0.2;
    const double SideAngle = 32;

    // Cuanto mayor, más rápido llega la animación a su destino.
    const double Stiffness = 9.0;
    const double FadeStart = 4.5;
    const double MaxVisibleDistance = 6.5;

    static readonly MeshGeometry3D CoverMesh = CreateQuad(0, CoverHeight);
    static readonly MeshGeometry3D ReflectionMesh = CreateQuad(-CoverHeight, 0);

    readonly Viewport3D _viewport;
    readonly Model3DGroup _scene = new();
    readonly List<Slot> _slots = [];
    readonly List<Slot> _drawOrder = [];
    readonly List<GeometryModel3D> _desiredModels = [];
    readonly Dictionary<GeometryModel3D, int> _modelIndex = [];

    double _position;
    int _target;
    bool _animating;
    TimeSpan _lastFrame;

    public event Action<int>? SelectedIndexChanged;

    public int SelectedIndex => _target;
    public int Count => _slots.Count;

    public CoverFlow(Viewport3D viewport)
    {
        _viewport = viewport;
        _viewport.Camera = new PerspectiveCamera
        {
            Position = CameraPosition,
            LookDirection = CameraTarget - CameraPosition,
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = FieldOfView,
            NearPlaneDistance = 0.1,
            FarPlaneDistance = 100,
        };
        _viewport.Children.Add(new ModelVisual3D { Content = new AmbientLight(Colors.White) });
        _viewport.Children.Add(new ModelVisual3D { Content = _scene });
    }

    public void SetItems(IReadOnlyList<CarouselItem> items, int selectedIndex)
    {
        _slots.Clear();
        _modelIndex.Clear();
        _scene.Children.Clear();

        for (var i = 0; i < items.Count; i++)
        {
            var slot = new Slot(items[i]);
            _slots.Add(slot);
            _modelIndex[slot.Cover] = i;
            _modelIndex[slot.Reflection] = i;
        }

        var last = Math.Max(0, _slots.Count - 1);
        _target = Math.Clamp(selectedIndex, 0, last);
        _position = Math.Clamp(_position, 0, last);

        ApplyLayout();
        StartAnimation();
        SelectedIndexChanged?.Invoke(_target);
    }

    public void MoveBy(int delta) => Select(_target + delta);

    public void Select(int index)
    {
        if (_slots.Count == 0)
            return;

        index = Math.Clamp(index, 0, _slots.Count - 1);
        if (index == _target)
            return;

        _target = index;
        StartAnimation();
        SelectedIndexChanged?.Invoke(_target);
    }

    /// <summary>Índice de la portada bajo el punto dado (en coordenadas del viewport), si hay alguna.</summary>
    public int? HitTest(Point point)
    {
        int? hit = null;
        VisualTreeHelper.HitTest(_viewport, null, result =>
        {
            if (result is RayMeshGeometry3DHitTestResult mesh &&
                mesh.ModelHit is GeometryModel3D model &&
                _modelIndex.TryGetValue(model, out var index))
            {
                hit = index;
                return HitTestResultBehavior.Stop;
            }
            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(point));
        return hit;
    }

    /// <summary>Proyecta un punto de la escena a píxeles del viewport, para colocar la interfaz 2D encima.</summary>
    public Point Project(Point3D point)
    {
        var forward = CameraTarget - CameraPosition;
        forward.Normalize();
        var right = Vector3D.CrossProduct(forward, new Vector3D(0, 1, 0));
        right.Normalize();
        var up = Vector3D.CrossProduct(right, forward);

        var v = point - CameraPosition;
        var depth = Vector3D.DotProduct(v, forward);
        var halfWidth = depth * Math.Tan(FieldOfView * Math.PI / 360);

        double width = _viewport.ActualWidth, height = _viewport.ActualHeight;
        var x = Vector3D.DotProduct(v, right) / halfWidth;
        var y = Vector3D.DotProduct(v, up) / halfWidth;
        return new Point(width / 2 + x * width / 2, height / 2 - y * width / 2);
    }

    void StartAnimation()
    {
        if (_animating)
            return;

        // Solo nos enganchamos al bucle de render mientras hay movimiento, para no gastar GPU en reposo.
        _animating = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame)
            return;

        var dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min((now - _lastFrame).TotalSeconds, 0.05);
        _lastFrame = now;

        // Acercamiento exponencial: independiente de los FPS y sin tirones aunque el destino cambie a mitad.
        _position += (_target - _position) * (1 - Math.Exp(-Stiffness * dt));
        if (Math.Abs(_target - _position) < 0.001)
        {
            _position = _target;
            _animating = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        ApplyLayout();
    }

    void ApplyLayout()
    {
        _drawOrder.Clear();

        for (var i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            var offset = i - _position;
            var distance = Math.Abs(offset);
            slot.Distance = distance;
            if (distance > MaxVisibleDistance)
                continue;

            var side = Math.Sign(offset);
            var near = Math.Min(distance, 1);
            var stacked = Math.Max(0, distance - 1);

            slot.Translation.OffsetX = side * (near * NeighborOffsetX + stacked * StackSpacingX);
            slot.Translation.OffsetZ = -(near * NeighborDepth + stacked * StackSpacingDepth);
            slot.Rotation.Angle = side * near * SideAngle;
            slot.SetOpacity(distance <= FadeStart
                ? 1
                : Math.Max(0, (MaxVisibleDistance - distance) / (MaxVisibleDistance - FadeStart)));

            _drawOrder.Add(slot);
        }

        // Las partes semitransparentes (reflejos y portadas lejanas) se pintan de atrás hacia delante.
        _drawOrder.Sort((a, b) => b.Distance.CompareTo(a.Distance));
        SyncScene();
    }

    void SyncScene()
    {
        _desiredModels.Clear();
        foreach (var slot in _drawOrder)
            _desiredModels.Add(slot.Reflection);
        foreach (var slot in _drawOrder)
            _desiredModels.Add(slot.Cover);

        var children = _scene.Children;
        var unchanged = children.Count == _desiredModels.Count;
        for (var i = 0; unchanged && i < _desiredModels.Count; i++)
            unchanged = ReferenceEquals(children[i], _desiredModels[i]);
        if (unchanged)
            return;

        children.Clear();
        foreach (var model in _desiredModels)
            children.Add(model);
    }

    static MeshGeometry3D CreateQuad(double bottom, double top)
    {
        const double half = CoverWidth / 2;
        var mesh = new MeshGeometry3D
        {
            Positions = [new(-half, bottom, 0), new(half, bottom, 0), new(half, top, 0), new(-half, top, 0)],
            TextureCoordinates = [new(0, 1), new(1, 1), new(1, 0), new(0, 0)],
            TriangleIndices = [0, 1, 2, 0, 2, 3],
        };
        mesh.Freeze();
        return mesh;
    }

    sealed class Slot
    {
        public readonly GeometryModel3D Cover;
        public readonly GeometryModel3D Reflection;
        public readonly AxisAngleRotation3D Rotation = new(new Vector3D(0, 1, 0), 0);
        public readonly TranslateTransform3D Translation = new();
        public double Distance;

        readonly ImageBrush _coverBrush;
        readonly ImageBrush _reflectionBrush;

        public Slot(CarouselItem item)
        {
            var (cover, reflection) = CoverArt.Render(item);
            _coverBrush = new ImageBrush(cover);
            _reflectionBrush = new ImageBrush(reflection);

            var transform = new Transform3DGroup();
            transform.Children.Add(new RotateTransform3D(Rotation));
            transform.Children.Add(Translation);

            Cover = new GeometryModel3D(CoverMesh, new DiffuseMaterial(_coverBrush)) { Transform = transform };
            Reflection = new GeometryModel3D(ReflectionMesh, new DiffuseMaterial(_reflectionBrush)) { Transform = transform };
        }

        public void SetOpacity(double opacity)
        {
            if (_coverBrush.Opacity == opacity)
                return;
            _coverBrush.Opacity = opacity;
            _reflectionBrush.Opacity = opacity;
        }
    }
}
