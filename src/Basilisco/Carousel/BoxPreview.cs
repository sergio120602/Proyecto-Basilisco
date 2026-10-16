using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Basilisco.Carousel;

/// <summary>
/// Vista previa de una caja en 3D dentro de un Viewport3D: se gira arrastrando con el ratón
/// y puede animarse para enseñar la portada o la contraportada.
/// </summary>
public sealed class BoxPreview
{
    const double Stiffness = 12.0;
    const double DegreesPerPixel = 0.6;

    readonly Viewport3D _viewport;
    readonly ModelVisual3D _content = new();
    BoxModel? _box;

    double _yaw, _pitch, _yawTarget, _pitchTarget;
    Point? _dragStart;
    (double Yaw, double Pitch) _dragFrom;
    bool _animating;
    TimeSpan _lastFrame;

    public BoxPreview(Viewport3D viewport)
    {
        _viewport = viewport;
        _viewport.Camera = new PerspectiveCamera
        {
            Position = new Point3D(0, 0.62, 2.0),
            LookDirection = new Vector3D(0, -0.12, -2.0),
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = 45,
        };
        _viewport.Children.Add(new ModelVisual3D { Content = BoxModel.CreateLighting() });
        _viewport.Children.Add(_content);

        // El Viewport3D solo recibe el ratón donde hay geometría; un fondo transparente encima lo cubre entero.
        var surface = (Panel)VisualTreeHelper.GetParent(_viewport);
        surface.Background ??= Brushes.Transparent;
        surface.MouseLeftButtonDown += OnMouseDown;
        surface.MouseMove += OnMouseMove;
        surface.MouseLeftButtonUp += OnMouseUp;
        surface.LostMouseCapture += (_, _) => _dragStart = null;
    }

    /// <summary>True si se está viendo la parte de atrás de la caja.</summary>
    public bool IsShowingBack
    {
        get
        {
            var yaw = ((_yawTarget % 360) + 360) % 360;
            return yaw is > 90 and < 270;
        }
    }

    public void SetItem(CarouselItem item)
    {
        if (_box is null)
        {
            _box = new BoxModel(item, renderBackNow: true);
            var group = new Model3DGroup();
            group.Children.Add(_box.Reflection);
            group.Children.Add(_box.Box);
            _content.Content = group;
        }
        else
        {
            _box.SetArt(item);
        }
        _box.SetInspect(_yaw, _pitch);
    }

    /// <summary>Quita la caja y vuelve a la posición inicial (al cerrar el menú).</summary>
    public void Clear()
    {
        _content.Content = null;
        _box = null;
        _yaw = _pitch = _yawTarget = _pitchTarget = 0;
    }

    public void ShowFront() => TurnTo(0);

    public void ShowBack() => TurnTo(180);

    // Gira por el camino más corto hasta la cara pedida.
    void TurnTo(double face)
    {
        var turns = Math.Round((_yawTarget - face) / 360);
        _yawTarget = face + turns * 360;
        _pitchTarget = 0;
        StartAnimation();
    }

    void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var surface = (IInputElement)sender;
        _dragStart = e.GetPosition(surface);
        _dragFrom = (_yawTarget, _pitchTarget);
        surface.CaptureMouse();
        e.Handled = true;
    }

    void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start)
            return;

        var delta = e.GetPosition((IInputElement)sender) - start;
        _yawTarget = _dragFrom.Yaw + delta.X * DegreesPerPixel;
        _pitchTarget = Math.Clamp(_dragFrom.Pitch + delta.Y * DegreesPerPixel * CoverFlow.PitchDragFactor, -CoverFlow.MaxPitch, CoverFlow.MaxPitch);
        StartAnimation();
    }

    void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null;
        ((IInputElement)sender).ReleaseMouseCapture();
    }

    void StartAnimation()
    {
        if (_animating)
            return;
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

        var step = 1 - Math.Exp(-Stiffness * dt);
        _yaw += (_yawTarget - _yaw) * step;
        _pitch += (_pitchTarget - _pitch) * step;
        if (Math.Abs(_yawTarget - _yaw) < 0.05 && Math.Abs(_pitchTarget - _pitch) < 0.05)
        {
            _yaw = _yawTarget;
            _pitch = _pitchTarget;
            _animating = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        _box?.SetInspect(_yaw, _pitch);
    }
}
