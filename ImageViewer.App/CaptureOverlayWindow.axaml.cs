using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ImageViewer.Imaging.Models;
using ImageViewer.Platform.Windows;

namespace ImageViewer.App;

public partial class CaptureOverlayWindow : Window
{
    private readonly InMemoryImageSource _capture;
    private bool _dragging;
    private Point _dragStart;
    private Point _dragEnd;

    public InMemoryImageSource? Result { get; private set; }

    public CaptureOverlayWindow() : this(new InMemoryImageSource(new byte[4], 1, 1, 4, "Snip"))
    {
        // Parameterless ctor for Avalonia XAML loader. Not used at runtime.
    }

    public CaptureOverlayWindow(InMemoryImageSource capture)
    {
        InitializeComponent();
        _capture = capture;

        var bitmap = new WriteableBitmap(
            new PixelSize(capture.Width, capture.Height),
            new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul);

        using (var lockable = bitmap.Lock())
        {
            unsafe
            {
                fixed (byte* src = capture.BgraPixels)
                {
                    Buffer.MemoryCopy(src, (void*)lockable.Address, lockable.RowBytes * capture.Height, capture.Stride * capture.Height);
                }
            }
        }

        CaptureImage.Source = bitmap;
        CaptureImage.Width = capture.Width;
        CaptureImage.Height = capture.Height;
        OverlayCanvas.Width = capture.Width;
        OverlayCanvas.Height = capture.Height;

        OverlayCanvas.PointerPressed += OnPointerPressed;
        OverlayCanvas.PointerMoved += OnPointerMoved;
        OverlayCanvas.PointerReleased += OnPointerReleased;
    }

    public Func<InMemoryImageSource, Task>? OnCaptureComplete { get; set; }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(OverlayCanvas).Properties.IsLeftButtonPressed) return;
        _dragging = true;
        _dragStart = e.GetPosition(OverlayCanvas);
        _dragEnd = _dragStart;
        SelectionBorder.IsVisible = true;
        UpdateSelection();
        e.Pointer.Capture(OverlayCanvas);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;
        _dragEnd = e.GetPosition(OverlayCanvas);
        UpdateSelection();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);

        var rect = NormalizeSelection();
        SelectionBorder.IsVisible = false;

        if (rect.Width > 4 && rect.Height > 4)
        {
            Result = ScreenCaptureService.Crop(_capture, (int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
        }

        Close();
    }

    private void OnOverlayKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _dragging = false;
            Result = null;
            Close();
            e.Handled = true;
        }
    }

    private void UpdateSelection()
    {
        var rect = NormalizeSelection();
        Canvas.SetLeft(SelectionBorder, rect.X);
        Canvas.SetTop(SelectionBorder, rect.Y);
        SelectionBorder.Width = rect.Width;
        SelectionBorder.Height = rect.Height;
    }

    private Rect NormalizeSelection()
    {
        var x = Math.Min(_dragStart.X, _dragEnd.X);
        var y = Math.Min(_dragStart.Y, _dragEnd.Y);
        var w = Math.Abs(_dragEnd.X - _dragStart.X);
        var h = Math.Abs(_dragEnd.Y - _dragStart.Y);
        return new Rect(x, y, w, h);
    }

    public async Task ShowAndCaptureAsync()
    {
        var tcs = new TaskCompletionSource<bool>();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(_capture.OriginX, _capture.OriginY);
            Width = _capture.Width;
            Height = _capture.Height;
            WindowState = WindowState.Normal;
            Show();
            Activate();
            Topmost = true;

            Closed += (_, _) => tcs.TrySetResult(true);
        });

        await tcs.Task;

        if (Result is not null && OnCaptureComplete is not null)
        {
            await OnCaptureComplete(Result);
        }
    }
}
