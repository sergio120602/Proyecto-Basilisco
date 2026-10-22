using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace Basilisco.Carousel;

/// <param name="Platform">Consola del juego (decide el borde y el plástico de la caja), o null si no tiene.</param>
/// <param name="SpineImagePath">Imagen del lomo, o null para el lomo con el nombre.</param>
/// <param name="BackFrame">Encuadre de la contraportada; null para verla entera.</param>
/// <param name="SpineFrame">Encuadre de la imagen del lomo; null para verla entera.</param>
/// <param name="Format">Formato de la caja: cuadrada (franja a la izquierda) o rectangular (franja arriba).</param>
/// <param name="FrontFrame">Encuadre de la portada; null para verla entera.</param>
public sealed record CarouselItem(string Title, bool IsAddCard, string? ImagePath = null, string? BackImagePath = null, string? Platform = null,
                                  string? SpineImagePath = null, Models.ImageFrame? BackFrame = null, Models.ImageFrame? SpineFrame = null,
                                  Models.CoverFormat Format = Models.CoverFormat.Square, Models.ImageFrame? FrontFrame = null,
                                  bool Steam = false);

/// <summary>
/// Carrusel 3D de cajas: la seleccionada de frente y el resto girado y apilado a los lados,
/// con reflejo en el suelo. La posición es continua y persigue al índice seleccionado,
/// así que pasar de una caja a otra siempre es un deslizamiento suave.
/// La caja seleccionada además se puede girar sobre sí misma para ver la contraportada y el grosor.
/// </summary>
public sealed class CoverFlow
{
    public const double CoverHeight = BoxModel.Height;

    static readonly Point3D BaseCameraPosition = new(0, 0.7, 3.7);
    static readonly Point3D BaseCameraTarget = new(0, 0.44, 0);

    // La cámara y el punto al que mira suben o bajan juntos: las cajas se ven más abajo o más arriba sin cambiar la perspectiva.
    Point3D CameraTarget => BaseCameraTarget + new Vector3D(0, _shift, 0);
    double _shift;

    /// <summary>Altura de la cámara (unidades de la escena): con más, las cajas se ven más abajo en la pantalla.</summary>
    public double Shift
    {
        get => _shift;
        set
        {
            _shift = Math.Clamp(value, MinShift, MaxShift);
            _camera.Position = CameraPosition;
            _camera.LookDirection = CameraTarget - CameraPosition;
        }
    }

    /// <summary>
    /// Alineación moderna: todas las cajas de frente, sin girar y a la misma profundidad, una al lado de otra en una
    /// fila recta (como la biblioteca de Steam en modo Big Picture): las bases y los altos quedan en línea horizontal.
    /// Sin ella, la clásica: las de los lados giradas y apiladas hacia el fondo.
    /// </summary>
    public bool Aligned
    {
        get => _aligned;
        set
        {
            _aligned = value;
            ApplyLayout();
        }
    }
    bool _aligned;

    public const double MinShift = -0.4;
    public const double MaxShift = 1.2;

    public const double MinZoom = 0.7;
    public const double MaxZoom = 1.8;

    // La cámara se acerca o se aleja del centro según el zoom (1 = la distancia original).
    Point3D CameraPosition => CameraTarget + (BaseCameraPosition - BaseCameraTarget) / _zoom;
    double _zoom = 1;
    PerspectiveCamera _camera = null!;
    const double FieldOfView = 55;

    // Pantallas panorámicas (21:9, 32:9…): WPF fija el ángulo de visión horizontal, así que en una pantalla más ancha
    // que 16:9 se vería menos por arriba y por abajo (las cajas, más grandes y cortadas). Se abre el ángulo lo justo para
    // ver lo mismo en vertical que en 16:9, y el carrusel llega más lejos a los lados para llenar el ancho de más.
    const double DesignAspect = 16.0 / 9.0;
    double _fieldOfView = FieldOfView;
    double _wide = 1;

    void UpdateAspect()
    {
        double width = _viewport.ActualWidth, height = _viewport.ActualHeight;
        if (width <= 0 || height <= 0)
            return;
        _wide = Math.Max(1, width / height / DesignAspect);
        _fieldOfView = 2 * Math.Atan(Math.Tan(FieldOfView * Math.PI / 360) * _wide) * 180 / Math.PI;
        _camera.FieldOfView = _fieldOfView;
        ApplyLayout();
    }

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

    // Modo estantería (orden personalizado): las cajas de lado, enseñando el lomo, una junto a otra.
    const double ShelfSpineZ = 0.8;      // Plano en el que quedan los lomos (más cerca que el carrusel, para verlos bien).
    const double ShelfGap = 0.012;       // Hueco entre lomos.
    const double ShelfFadeStart = 2.3;   // A partir de esta distancia del centro, los lomos se desvanecen…
    const double ShelfVisible = 3.0;     // …y dejan de pintarse.
    const double ShelfLift = 0.24;       // Lo que sube el juego cogido.
    const double ShelfPeek = 0.04;       // Lo que asoma el seleccionado sin cogerlo, para saber cuál es.
    const double ShelfStiffness = 7.0;

    readonly Viewport3D _viewport;
    readonly Model3DGroup _scene = new();
    readonly List<Slot> _slots = [];
    readonly List<Slot> _drawOrder = [];
    readonly List<Model3DGroup> _desiredModels = [];
    readonly Dictionary<GeometryModel3D, int> _modelIndex = [];

    double _position;
    int _target;
    bool _shelf;
    double _shelfBlend;
    double _yawTarget;
    double _pitchTarget;
    bool _animating;
    TimeSpan _lastFrame;

    public event Action<int>? SelectedIndexChanged;

    public int SelectedIndex => _target;

    /// <summary>Tamaño con el que se ven las cajas (acerca o aleja la cámara).</summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Math.Clamp(value, MinZoom, MaxZoom);
            _camera.Position = CameraPosition;
            _camera.LookDirection = CameraTarget - CameraPosition;
        }
    }

    /// <summary>
    /// Modo estantería: las cajas se giran para enseñar solo el lomo, una junto a otra, para colocarlas a mano.
    /// El paso entre el carrusel y la estantería es animado.
    /// </summary>
    public bool ShelfMode
    {
        get => _shelf;
        set
        {
            if (_shelf == value)
                return;
            _shelf = value;
            if (!value)
                Holding = false;
            _yawTarget = _pitchTarget = 0;
            StartAnimation();
        }
    }

    /// <summary>True mientras el juego del centro está cogido (levantado) en la estantería.</summary>
    public bool Holding { get; private set; }

    /// <summary>Coge o suelta el juego del centro de la estantería.</summary>
    public void SetHolding(bool holding)
    {
        if (!_shelf || _slots.Count == 0 || _slots[_target].Item.IsAddCard)
            holding = false;
        Holding = holding;
        StartAnimation();
    }

    /// <summary>
    /// Mueve el juego cogido un sitio a la izquierda o a la derecha: cambia de lugar con su vecino, que se aparta
    /// deslizándose. Devuelve false si ya está en un extremo (la tarjeta de importar siempre va la última).
    /// </summary>
    public bool MoveHeld(int direction)
    {
        var to = _target + direction;
        if (!Holding || to < 0 || to >= _slots.Count || _slots[to].Item.IsAddCard)
            return false;

        (_slots[_target], _slots[to]) = (_slots[to], _slots[_target]);
        foreach (var index in new[] { _target, to })
        {
            foreach (var face in _slots[index].Model.Faces)
                _modelIndex[face] = index;
        }
        _target = to;
        StartAnimation();
        SelectedIndexChanged?.Invoke(_target);
        return true;
    }

    /// <summary>Alto de la caja seleccionada (para poner el título justo encima).</summary>
    public double SelectedHeight => _slots.Count == 0 ? BoxShape.Square.Height : BoxShape.Of(_slots[_target].Item).Height;
    public int Count => _slots.Count;

    /// <summary>Giro al que va la caja seleccionada (grados alrededor del eje vertical y del horizontal).</summary>
    public (double Yaw, double Pitch) InspectTarget => (_yawTarget, _pitchTarget);

    public CoverFlow(Viewport3D viewport)
    {
        _viewport = viewport;
        _viewport.Camera = _camera = new PerspectiveCamera
        {
            Position = CameraPosition,
            LookDirection = CameraTarget - CameraPosition,
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = FieldOfView,
            NearPlaneDistance = 0.05,
            FarPlaneDistance = 100,
        };
        _viewport.Children.Add(new ModelVisual3D { Content = BoxModel.CreateLighting() });
        _viewport.Children.Add(new ModelVisual3D { Content = _scene });
        _viewport.SizeChanged += (_, _) => UpdateAspect();
    }

    /// <param name="jump">True para colocarse directamente en la selección sin deslizar (al cambiar de filtro).</param>
    public void SetItems(IReadOnlyList<CarouselItem> items, int selectedIndex, bool jump = false)
    {
        _slots.Clear();
        _modelIndex.Clear();
        _scene.Children.Clear();

        for (var i = 0; i < items.Count; i++)
        {
            var slot = new Slot(items[i]) { Shown = i };
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
        var halfWidth = depth * Math.Tan(_fieldOfView * Math.PI / 360);

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

        settled &= Approach(ref _shelfBlend, _shelf ? 1 : 0, ShelfStiffness, dt, 0.001);

        var inspectStep = 1 - Math.Exp(-InspectStiffness * dt);
        for (var i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];

            // Cada caja persigue su sitio: al cambiarlas de lugar en la estantería se deslizan en vez de saltar.
            settled &= Approach(ref slot.Shown, i, Stiffness * 1.4, dt, 0.001);
            var lift = !_shelf ? 0 : i == _target ? (Holding ? 1 : ShelfPeek / ShelfLift) : 0;
            settled &= Approach(ref slot.Lift, lift, Stiffness * 1.4, dt, 0.001);

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
            return BoxShape.Square.Width;

        var left = Math.Clamp((int)Math.Floor(_position), 0, _slots.Count - 1);
        var right = Math.Min(left + 1, _slots.Count - 1);
        var t = Math.Clamp(_position - left, 0, 1);
        return WidthOf(_slots[left]) * (1 - t) + WidthOf(_slots[right]) * t;

        static double WidthOf(Slot slot) => BoxModel.WidthOf(slot.Item);
    }

    void ApplyLayout()
    {
        _drawOrder.Clear();

        // Las vecinas se separan según lo ancha que sea la caja del centro (más juntas si es rectangular).
        // Se interpola entre las dos cajas más cercanas al centro para que no haya saltos al deslizar; y como
        // la separación es la misma para todas las laterales, nunca se cruzan entre ellas.
        var neighborOffset = NeighborOffsetX * (0.5 + 0.5 * CenterWidth() / BoxShape.Square.Width);

        // Fila moderna: el centro de cada caja, una junto a otra según su ancho.
        var rowCenters = _aligned ? RowCenters() : null;
        var rowFocus = rowCenters is null ? 0 : Interpolate(rowCenters, _position);

        // Estantería: el centro de cada lomo, uno junto a otro según el grosor de cada caja.
        var blend = _shelfBlend;
        var centers = blend > 0 ? ShelfCenters() : null;
        var focus = centers is null ? 0 : Interpolate(centers, _position);

        for (var i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            var offset = slot.Shown - _position;
            var distance = Math.Abs(offset);

            // Posición en el carrusel.
            var carouselVisible = distance <= MaxVisibleDistance * _wide;
            var side = Math.Sign(offset);
            var near = Math.Min(distance, 1);
            var stacked = Math.Max(0, distance - 1);
            var cx = side * (near * neighborOffset + stacked * StackSpacingX);
            var cz = -(near * NeighborDepth + stacked * StackSpacingDepth);
            var cAngle = side * near * SideAngle;
            double fadeStart = FadeStart * _wide, maxVisible = MaxVisibleDistance * _wide;
            var cOpacity = distance <= fadeStart ? 1 : Math.Max(0, (maxVisible - distance) / (maxVisible - fadeStart));
            if (rowCenters is not null)
            {
                // De frente y a la misma profundidad que la del centro: la fila queda recta en pantalla.
                cx = Interpolate(rowCenters, slot.Shown) - rowFocus;
                cz = 0;
                cAngle = 0;
                var reach = Math.Abs(cx);
                double rowFade = RowFadeStart * _wide, rowVisible = RowVisible * _wide;
                carouselVisible = reach < rowVisible;
                cOpacity = reach <= rowFade ? 1 : Math.Max(0, (rowVisible - reach) / (rowVisible - rowFade));
            }

            // Posición en la estantería: girada 90° (el lomo mira a la cámara) y con los lomos alineados.
            double sx = 0, sz = 0, sOpacity = 0;
            var shelfVisible = false;
            if (centers is not null)
            {
                sx = Interpolate(centers, slot.Shown) - focus;
                sz = ShelfSpineZ - BoxModel.WidthOf(slot.Item) / 2 + slot.Lift * ShelfLift * 0.6;
                var reach = Math.Abs(sx);
                double shelfFade = ShelfFadeStart * _wide, shelfMax = ShelfVisible * _wide;
                shelfVisible = reach < shelfMax;
                sOpacity = reach <= shelfFade ? 1 : Math.Max(0, (shelfMax - reach) / (shelfMax - shelfFade));
            }

            if (!(blend < 1 && carouselVisible) && !(blend > 0 && shelfVisible))
                continue;

            var model = slot.Model;
            model.Position.OffsetX = cx + (sx - cx) * blend;
            model.Position.OffsetZ = cz + (sz - cz) * blend;
            model.Position.OffsetY = slot.Lift * ShelfLift * blend;
            model.SideRotation.Angle = cAngle + (90 - cAngle) * blend;
            model.SetInspect(slot.Yaw, slot.Pitch);
            model.SetOpacity((carouselVisible ? cOpacity : 0) * (1 - blend) + (shelfVisible ? sOpacity : 0) * blend);

            // Orden de pintado: en la estantería manda la distancia real al centro.
            slot.Distance = blend > 0.5 ? Math.Abs(sx) * 10 : distance;
            _drawOrder.Add(slot);
        }

        // Las partes semitransparentes (reflejos y cajas lejanas) se pintan de atrás hacia delante.
        _drawOrder.Sort((a, b) => b.Distance.CompareTo(a.Distance));
        UpdateArt();
        SyncScene();
    }

    // Fila moderna: hueco entre cajas y hasta dónde se ven (se desvanecen hacia los bordes).
    const double RowGap = 0.1;
    const double RowFadeStart = 2.6;
    const double RowVisible = 3.6;

    /// <summary>Centro de cada caja en la fila moderna (cada una ocupa su ancho más un hueco).</summary>
    double[] RowCenters()
    {
        var centers = new double[_slots.Count];
        for (var i = 1; i < _slots.Count; i++)
            centers[i] = centers[i - 1] + (BoxModel.WidthOf(_slots[i - 1].Item) + BoxModel.WidthOf(_slots[i].Item)) / 2 + RowGap;
        return centers;
    }

    /// <summary>Centro de cada lomo en la estantería (cada caja ocupa su grosor más un pequeño hueco).</summary>
    double[] ShelfCenters()
    {
        var centers = new double[_slots.Count];
        for (var i = 1; i < _slots.Count; i++)
        {
            var previous = BoxShape.Of(_slots[i - 1].Item).Depth;
            var current = BoxShape.Of(_slots[i].Item).Depth;
            centers[i] = centers[i - 1] + (previous + current) / 2 + ShelfGap;
        }
        return centers;
    }

    /// <summary>Valor en una posición fraccionaria (entre dos cajas), para que todo se mueva sin saltos.</summary>
    static double Interpolate(double[] values, double position)
    {
        if (values.Length == 0)
            return 0;
        var left = Math.Clamp((int)Math.Floor(position), 0, values.Length - 1);
        var right = Math.Min(left + 1, values.Length - 1);
        var t = Math.Clamp(position - left, 0, 1);
        return values[left] + (values[right] - values[left]) * t;
    }

    // Si al deslizar aparece una caja que aún no está pintada, cuántas se pintan como mucho en cada fotograma.
    const int ArtPerFrame = 2;

    // Las cajas se pintan por adelantado en bloques de 50 (la 0-49, la 50-99…). Al llegar a la 35 de un bloque se
    // pinta ya el siguiente, para que esté listo antes de llegar; y al volver atrás, el anterior. Lo demás se suelta.
    const int ArtBlock = 50;
    const int ArtBlockAhead = 35;

    /// <summary>Calidad de las cajas (1 alta, 0,75 media, 0,5 baja): la resolución a la que se pintan.</summary>
    public double Quality { get; set; } = 1;

    bool _firstArt = true;
    bool _prerenderQueued;

    /// <summary>Cajas que se mantienen pintadas: el bloque de la seleccionada y, cerca de sus bordes, el vecino.</summary>
    (int Start, int End) KeptRange()
    {
        var start = _target / ArtBlock * ArtBlock;
        var end = start + ArtBlock;
        var within = _target - start;
        if (within >= ArtBlockAhead)
            end += ArtBlock;
        if (within < ArtBlock - ArtBlockAhead && start > 0)
            start -= ArtBlock;
        return (start, Math.Min(end, _slots.Count));
    }

    /// <summary>
    /// Pinta al momento las cajas que se ven y no estaban pintadas (casi nunca hace falta, porque ya están listas)
    /// y suelta las que quedan fuera del bloque. El resto del bloque se pinta cuando el carrusel está quieto, de una
    /// en una, para que deslizar vaya siempre fluido.
    /// </summary>
    void UpdateArt()
    {
        var budget = _firstArt ? int.MaxValue : ArtPerFrame;
        _firstArt = false;
        var pending = false;
        foreach (var slot in _drawOrder.OrderBy(slot => slot.Distance))
        {
            if (slot.Model.HasArt && slot.Model.ArtScale == Quality)
                continue;
            if (budget-- > 0)
                slot.Model.EnsureArt(Quality);
            else
                pending = true;
        }

        var (start, end) = KeptRange();
        for (var i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            if (slot.Model.HasArt && (i < start || i >= end) && !_drawOrder.Contains(slot) && _shelfBlend == 0)
                slot.Model.ReleaseArt();
        }

        // Si han quedado cajas sin pintar, se siguen pintando en los siguientes fotogramas.
        if (pending)
            StartAnimation();
        QueuePrerender();
    }

    void QueuePrerender()
    {
        if (_prerenderQueued)
            return;
        _prerenderQueued = true;
        _viewport.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, Prerender);
    }

    /// <summary>Pinta la siguiente caja del bloque (la más cercana a la seleccionada) y se vuelve a pedir.</summary>
    void Prerender()
    {
        _prerenderQueued = false;
        if (_animating)
            return; // Al pararse el carrusel se vuelve a pedir (UpdateArt).

        var (start, end) = KeptRange();
        Slot? next = null;
        var best = int.MaxValue;
        for (var i = start; i < end; i++)
        {
            var model = _slots[i].Model;
            if ((model.HasArt && model.ArtScale == Quality) || Math.Abs(i - _target) >= best)
                continue;
            best = Math.Abs(i - _target);
            next = _slots[i];
        }
        if (next is null)
            return;
        next.Model.EnsureArt(Quality);
        QueuePrerender();
    }

    /// <summary>Vuelve a pintar todas las cajas (al cambiar la calidad o los reflejos).</summary>
    public void RepaintAll()
    {
        foreach (var slot in _slots)
            slot.Model.ReleaseArt();
        _firstArt = true;
        ApplyLayout();
    }

    void SyncScene()
    {
        _desiredModels.Clear();
        if (BoxModel.Reflections)
        {
            foreach (var slot in _drawOrder)
                _desiredModels.Add(slot.Model.Reflection);
        }
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

        /// <summary>Sitio en el que se pinta (persigue a su índice real, para deslizarse al cambiar de lugar).</summary>
        public double Shown;

        /// <summary>Cuánto está levantada en la estantería (0 apoyada, 1 cogida).</summary>
        public double Lift;

        public void SetItem(CarouselItem item)
        {
            Item = item;
            Model.SetArt(item);
        }

        /// <summary>Deja el giro entre -180° y 180° sin que cambie nada en pantalla.</summary>
        public void NormalizeYaw() => Yaw -= 360 * Math.Round(Yaw / 360);
    }
}
