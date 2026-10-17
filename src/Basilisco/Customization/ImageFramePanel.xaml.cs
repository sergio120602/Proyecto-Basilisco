using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Basilisco.Models;

namespace Basilisco.Customization;

/// <summary>
/// Editor para encuadrar una imagen en una cara de la caja (contraportada o lomo): se mueve arrastrando
/// y se amplía con la rueda o la barra. Enseña la cara tal como quedará.
/// </summary>
public partial class ImageFramePanel : UserControl
{
    const double WheelStep = 1.1;

    Func<ImageFrame, BitmapSource>? _render;
    Action<ImageFrame>? _onAccept;
    ImageFrame _frame = ImageFrame.Default;
    double _fillZoom = 1;
    bool _renderPending;
    bool _syncingSlider;

    Point? _dragStart;
    ImageFrame _dragFrom = ImageFrame.Default;

    public bool IsOpen => Visibility == Visibility.Visible;

    public ImageFramePanel()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    /// <param name="render">Pinta la cara con un encuadre dado.</param>
    /// <param name="fillZoom">Zoom con el que la imagen llena la cara entera (botón «Llenar»).</param>
    public void Open(string title, ImageFrame frame, double fillZoom, Func<ImageFrame, BitmapSource> render, Action<ImageFrame> onAccept)
    {
        TitleText.Text = title;
        _render = render;
        _onAccept = onAccept;
        _fillZoom = fillZoom;
        Visibility = Visibility.Visible;
        SetFrame(frame);
        _ = Dispatcher.BeginInvoke(() => Focus(), DispatcherPriority.Input);
    }

    void SetFrame(ImageFrame frame)
    {
        _frame = frame with { Zoom = Math.Clamp(frame.Zoom, ImageFrame.MinZoom, ImageFrame.MaxZoom) };
        ZoomText.Text = $"{_frame.Zoom * 100:0} %";

        _syncingSlider = true;
        ZoomSlider.Value = ZoomToSlider(_frame.Zoom);
        _syncingSlider = false;

        // Se vuelve a pintar una vez por fotograma como mucho, aunque el ratón mande muchos movimientos.
        if (_renderPending)
            return;
        _renderPending = true;
        _ = Dispatcher.BeginInvoke(() =>
        {
            _renderPending = false;
            if (_render is not null)
                FaceImage.Source = _render(_frame);
        }, DispatcherPriority.Render);
    }

    // La barra va en escala logarítmica: del 50 % al 1200 %, con el 100 % cerca de la izquierda.
    static double ZoomToSlider(double zoom) =>
        Math.Log(zoom / ImageFrame.MinZoom) / Math.Log(ImageFrame.MaxZoom / ImageFrame.MinZoom);

    static double SliderToZoom(double value) =>
        ImageFrame.MinZoom * Math.Pow(ImageFrame.MaxZoom / ImageFrame.MinZoom, value);

    void Accept()
    {
        var onAccept = _onAccept;
        var frame = _frame;
        Close();
        onAccept?.Invoke(frame);
    }

    void Close()
    {
        _render = null;
        _onAccept = null;
        _dragStart = null;
        FaceImage.Source = null;
        Visibility = Visibility.Collapsed;
    }

    void OnFaceMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(FaceImage);
        _dragFrom = _frame;
        FaceImage.CaptureMouse();
        e.Handled = true;
    }

    void OnFaceMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || FaceImage.ActualWidth <= 0 || FaceImage.ActualHeight <= 0)
            return;

        // El desplazamiento se guarda en fracción de la cara: lo que arrastras en pantalla, tal cual.
        var delta = e.GetPosition(FaceImage) - start;
        SetFrame(_dragFrom with
        {
            X = _dragFrom.X + delta.X / FaceImage.ActualWidth,
            Y = _dragFrom.Y + delta.Y / FaceImage.ActualHeight,
        });
    }

    void OnFaceMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null;
        FaceImage.ReleaseMouseCapture();
    }

    void OnFaceLostCapture(object sender, MouseEventArgs e) => _dragStart = null;

    void OnFaceMouseWheel(object sender, MouseWheelEventArgs e)
    {
        SetFrame(_frame with { Zoom = _frame.Zoom * Math.Pow(WheelStep, e.Delta / (double)Mouse.MouseWheelDeltaForOneLine) });
        e.Handled = true;
    }

    void OnZoomSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_syncingSlider)
            SetFrame(_frame with { Zoom = SliderToZoom(e.NewValue) });
    }

    void OnFitClick(object sender, RoutedEventArgs e) => SetFrame(ImageFrame.Default);

    void OnFillClick(object sender, RoutedEventArgs e) => SetFrame(new ImageFrame(_fillZoom));

    void OnAcceptClick(object sender, RoutedEventArgs e) => Accept();

    void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    // Clic en el fondo oscuro: no hace nada, pero tampoco debe llegar al menú de debajo (que se cerraría).
    void OnBackdropMouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter: Accept(); break;
            case Key.Escape: Close(); break;
            default: return;
        }
        e.Handled = true;
    }
}
