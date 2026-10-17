using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Basilisco.Carousel;

/// <param name="Platform">Consola del juego (decide el borde y el plástico de la caja), o null si no tiene.</param>
/// <param name="SpineImagePath">Imagen del lomo, o null para el lomo con el nombre.</param>
/// <param name="BackFrame">Encuadre de la contraportada; null para verla entera.</param>
/// <param name="SpineFrame">Encuadre de la imagen del lomo; null para verla entera.</param>
/// <param name="Format">Formato de la caja: cuadrada (franja a la izquierda) o rectangular (franja arriba).</param>
/// <param name="FrontFrame">Encuadre de la portada; null para verla entera.</param>
public sealed record CarouselItem(string Title, bool IsAddCard, string? ImagePath = null, string? BackImagePath = null, string? Platform = null,
                                  string? SpineImagePath = null, Models.ImageFrame? BackFrame = null, Models.ImageFrame? SpineFrame = null,
                                  Models.CoverFormat Format = Models.CoverFormat.Square, Models.ImageFrame? FrontFrame = null);

/// <summary>
/// Carrusel 3D de cajas: la seleccionada de frente y el resto girado y apilado a los lados,
/// con reflejo en el suelo. La posición es continua y persigue al índice seleccionado,
/// así que pasar de una caja a otra siempre es un deslizamiento suave.
/// La caja seleccionada además se puede girar sobre sí misma para ver la contraportada y el grosor.
/// </summary>
public sealed class CoverFlow
{
    public const double CoverHeight = BoxModel.Height;

    static readonly Point3D CameraPosition = new(0, 0.7, 3.7);
    static readonly Point3D CameraTarget = new(0, 0.44, 0);
    const double FieldOfView = 55;

    // Disposición de las cajas laterales.
    const double NeighborOffsetX = 1.14;
    const double StackSpacingX = 0.25;
    const double NeighborDepth = 0.55;
    // Separación en profundidad de las cajas apiladas. Con menos, al deslizar (cuando cada caja tiene un giro
    // distinto) el lomo de la de detrás llega a atravesar la portada de la de delante y se ve una raya negra.
    // Con 0,3 queda siempre al menos 0,05 por detrás durante toda la animación.
    const double StackSpacingDepth = 0.3;
    const double SideAngle = 32;

    // Cuanto mayor, más rápido llega la animación a su destino.
    const double Stiffness = 9.0;
    const double InspectStiffness = 12.0;
    const double FadeStart = 4.5;
    const double MaxVisibleDistance = 6.5;

    /// <summary>Inclinación máxima hacia delante o hacia atrás al inspeccionar, en grados.</summary>
    public const double MaxPitch = 30;

    /// <summary>Al arrastrar, la inclinación responde más despacio que el giro para que sea fácil de controlar.</summary>
    public const double PitchDragFactor = 0.5;

    readonly Viewport3D _viewport;
    readonly Model3DGroup _scene = new();
    readonly List<Slot> _slots = [];
    readonly List<Slot> _drawOrder = [];
    readonly List<Model3DGroup> _desiredModels = [];
    readonly Dictionary<GeometryModel3D, int> _modelIndex = [];

    double _position;
    int _target;
    double _yawTarget;
    double _pitchTarget;
    bool _animating;
    TimeSpan _lastFrame;

    public event Action<int>? SelectedIndexChanged;

    public int SelectedIndex => _target;
    public int Count => _slots.Count;

    /// <summary>Giro al que va la caja seleccionada (grados alrededor del eje vertical y del horizontal).</summary>
    public (double Yaw, double Pitch) InspectTarget => (_yawTarget, _pitchTarget);

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
        _viewport.Children.Add(new ModelVisual3D { Content = BoxModel.CreateLighting() });
        _viewport.Children.Add(new ModelVisual3D { Content = _scene });
    }

    /// <param name="jump">True para colocarse directamente en la selección sin deslizar (al cambiar de filtro).</param>
    public void SetItems(IReadOnlyList<CarouselItem> items, int selectedIndex, bool jump = false)
    {
        _slots.Clear();
        _modelIndex.Clear();
        _scene.Children.Clear();

        for (var i = 0; i < items.Count; i++)
        {
            var slot = new Slot(items[i]);
            _slots.Add(slot);
            foreach (var face in slot.Model.Faces)
                _modelIndex[face] = i;
        }

        var last = Math.Max(0, _slots.Count - 1);
        _target = Math.Clamp(selectedIndex, 0, last);
        _position = jump ? _target : Math.Clamp(_position, 0, last);
        _yawTarget = _pitchTarget = 0;

        ApplyLayout();
        StartAnimation();
        SelectedIndexChanged?.Invoke(_target);
    }

    /// <summary>Vuelve a pintar una sola caja (por ejemplo, tras personalizarla).</summary>
    public void UpdateItem(int index, CarouselItem item)
    {
        if (index >= 0 && index < _slots.Count)
            _slots[index].SetItem(item);
    }

    public void MoveBy(int delta) => Select(_target + delta);

    public void Select(int index)
    {
        if (_slots.Count == 0)
            return;

        index = Math.Clamp(index, 0, _slots.Count - 1);
        if (index == _target)
            return;

        // La caja que dejamos vuelve a ponerse de frente por el camino más corto.
        _slots[_target].NormalizeYaw();
        _target = index;
        _yawTarget = _pitchTarget = 0;
        StartAnimation();
        SelectedIndexChanged?.Invoke(_target);
    }

    /// <summary>True si la caja seleccionada se puede girar (la tarjeta de importar no tiene contraportada).</summary>
    public bool CanInspectSelected => _slots.Count > 0 && !_slots[_target].Item.IsAddCard;

    /// <summary>Gira la caja seleccionada hasta el ángulo dado; la caja lo sigue con suavidad.</summary>
    public void SetInspect(double yaw, double pitch)
    {
        if (!CanInspectSelected)
            return;

        _yawTarget = yaw;
        _pitchTarget = Math.Clamp(pitch, -MaxPitch, MaxPitch);
        StartAnimation();
    }

    /// <summary>Da la vuelta a la caja seleccionada: de la portada a la contraportada y viceversa.</summary>
    public void FlipSelected() => SetInspect(Math.Round(_yawTarget / 180) * 180 + 180, 0);

    /// <summary>Índice de la caja bajo el punto dado (en coordenadas del viewport), si hay alguna.</summary>
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
        var settled = Approach(ref _position, _target, Stiffness, dt, 0.001);

        var inspectStep = 1 - Math.Exp(-InspectStiffness * dt);
        for (var i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            double yaw = i == _target ? _yawTarget : 0, pitch = i == _target ? _pitchTarget : 0;
            if (slot.Yaw == yaw && slot.Pitch == pitch)
                continue;

            slot.Yaw += (yaw - slot.Yaw) * inspectStep;
            slot.Pitch += (pitch - slot.Pitch) * inspectStep;
            if (Math.Abs(yaw - slot.Yaw) < 0.05 && Math.Abs(pitch - slot.Pitch) < 0.05)
            {
                slot.Yaw = yaw;
                slot.Pitch = pitch;
            }
            else
            {
                settled = false;
            }
        }

        if (settled)
        {
            _animating = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        ApplyLayout();
    }

    static bool Approach(ref double value, double target, double stiffness, double dt, double epsilon)
    {
        value += (target - value) * (1 - Math.Exp(-stiffness * dt));
        if (Math.Abs(target - value) >= epsilon)
            return false;
        value = target;
        return true;
    }

    /// <summary>Ancho de la caja que está en el centro, interpolado durante el deslizamiento.</summary>
    double CenterWidth()
    {
        if (_slots.Count == 0)
            return BoxModel.WidthOf(Models.CoverFormat.Square);

        var left = Math.Clamp((int)Math.Floor(_position), 0, _slots.Count - 1);
        var right = Math.Min(left + 1, _slots.Count - 1);
        var t = Math.Clamp(_position - left, 0, 1);
        return WidthOf(_slots[left]) * (1 - t) + WidthOf(_slots[right]) * t;

        static double WidthOf(Slot slot) =>
            BoxModel.WidthOf(slot.Item.IsAddCard ? Models.CoverFormat.Square : slot.Item.Format);
    }

    void ApplyLayout()
    {
        _drawOrder.Clear();

        // Las vecinas se separan según lo ancha que sea la caja del centro (más juntas si es rectangular).
        // Se interpola entre las dos cajas más cercanas al centro para que no haya saltos al deslizar; y como
        // la separación es la misma para todas las laterales, nunca se cruzan entre ellas.
        var neighborOffset = NeighborOffsetX * (0.5 + 0.5 * CenterWidth() / BoxModel.WidthOf(Models.CoverFormat.Square));

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

            var model = slot.Model;
            model.Position.OffsetX = side * (near * neighborOffset + stacked * StackSpacingX);
            model.Position.OffsetZ = -(near * NeighborDepth + stacked * StackSpacingDepth);
            model.SideRotation.Angle = side * near * SideAngle;
            model.SetInspect(slot.Yaw, slot.Pitch);
            model.SetOpacity(distance <= FadeStart
                ? 1
                : Math.Max(0, (MaxVisibleDistance - distance) / (MaxVisibleDistance - FadeStart)));

            _drawOrder.Add(slot);
        }

        // Las partes semitransparentes (reflejos y cajas lejanas) se pintan de atrás hacia delante.
        _drawOrder.Sort((a, b) => b.Distance.CompareTo(a.Distance));
        SyncScene();
    }

    void SyncScene()
    {
        _desiredModels.Clear();
        foreach (var slot in _drawOrder)
            _desiredModels.Add(slot.Model.Reflection);
        foreach (var slot in _drawOrder)
            _desiredModels.Add(slot.Model.Box);

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

    sealed class Slot(CarouselItem item)
    {
        public BoxModel Model { get; } = new(item);
        public CarouselItem Item { get; private set; } = item;
        public double Distance;
        public double Yaw;
        public double Pitch;

        public void SetItem(CarouselItem item)
        {
            Item = item;
            Model.SetArt(item);
        }

        /// <summary>Deja el giro entre -180° y 180° sin que cambie nada en pantalla.</summary>
        public void NormalizeYaw() => Yaw -= 360 * Math.Round(Yaw / 360);
    }
}
