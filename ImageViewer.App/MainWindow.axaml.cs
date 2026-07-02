using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ImageViewer.App.ViewModels;
using ImageViewer.Core.Caps;
using ImageViewer.Core.Models;
using ImageViewer.Core.Services;
using ImageViewer.Imaging.Models;
using ImageViewer.Platform.Windows;
using SkiaSharp;

namespace ImageViewer.App;

public partial class MainWindow : Window
{
    private MenuItem? _zoomFixMenuItem;
    private MenuItem? _fullscreenMenuItem;
    private MenuItem? _sortDirectionMenuItem;
    private MenuItem? _setCapHotkeyMenuItem;
    private readonly List<(MenuItem Item, SortField Field, string Label)> _sortFieldMenuItems = new();
    private bool _isFullscreen;
    private bool _isPanning;
    private Point _lastPanPoint;
    private bool _uiReady;
    private bool _loadingCaps;

    private WindowState _preFullscreenState;
    private PixelPoint _preFullscreenPosition;
    private Size _preFullscreenSize;

    private bool _miniPanelInteracting;
    private bool _scrubBarInteracting;
    private bool _scrubBarDragging;
    private bool _frameSliderInteracting;
    private bool _syncingScrubSliders;
    private CancellationTokenSource? _scrubBarFadeCts;
    private CancellationTokenSource? _toastCts;
    private Avalonia.PixelSize _lastFitSize;

    private bool _capsActive;
    private bool _capsDrawing;
    private Point _capsStart;
    private Point _capsEnd;
    private int _capsSequence;

    public MainWindow()
    {
        InitializeComponent();
        BuildContextMenu();
        Opened += OnWindowOpened;
        Closing += OnWindowClosing;
        KeyDown += OnWindowKeyDown;
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);

        ViewerArea.PropertyChanged += (_, args) =>
        {
            if (args.Property == BoundsProperty)
                OnViewerAreaResized();
        };

        MiniPanelHitZone.AddHandler(PointerPressedEvent, OnMiniPanelPointerPressed, handledEventsToo: true);
        MiniPanelHitZone.AddHandler(PointerReleasedEvent, OnMiniPanelPointerReleased, handledEventsToo: true);
        ScrubBarHitZone.AddHandler(PointerPressedEvent, OnScrubBarPointerPressed, handledEventsToo: true);
        ScrubBarHitZone.AddHandler(PointerReleasedEvent, OnScrubBarPointerReleased, handledEventsToo: true);
        ScrubBarHitZone.AddHandler(PointerMovedEvent, OnScrubBarPointerMoved, handledEventsToo: true);
        ScrubBarHitZone.AddHandler(PointerCaptureLostEvent, OnScrubBarPointerCaptureLost, handledEventsToo: true);
        FrameSlider.AddHandler(PointerPressedEvent, OnFrameSliderPointerPressed, handledEventsToo: true);
        FrameSlider.AddHandler(PointerReleasedEvent, OnFrameSliderPointerReleased, handledEventsToo: true);
        FrameSlider.AddHandler(PointerCaptureLostEvent, OnFrameSliderPointerCaptureLost, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        CapsFormatCombo.SelectionChanged += (_, _) => SaveCapsSettings();
        CapsClipboardCheckbox.IsCheckedChanged += (_, _) => SaveCapsSettings();
        CapsOriginalPixelsCheckbox.IsCheckedChanged += (_, _) => SaveCapsSettings();
        CapsAutoCapCheckbox.IsCheckedChanged += (_, _) => SaveCapsSettings();

        CapsAspectX.LostFocus += (_, _) => SaveCapsSettings();
        CapsAspectY.LostFocus += (_, _) => SaveCapsSettings();
        CapsFixedW.LostFocus += (_, _) => SaveCapsSettings();
        CapsFixedH.LostFocus += (_, _) => SaveCapsSettings();
        CapsResizeValue.LostFocus += (_, _) => SaveCapsSettings();

        PositionChanged += (_, _) => PersistWindowGeometry();
        this.PropertyChanged += (_, args) =>
        {
            if (args.Property == WindowStateProperty || args.Property == BoundsProperty)
                PersistWindowGeometry();
        };

        _uiReady = true;
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        if (ViewModel is null) return;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        Dispatcher.UIThread.Post(async () =>
        {
            await RunLoggedAsync(async () =>
            {
                if (ViewModel is null) return;
                await ViewModel.InitializeAsync();
                LoadCapsSettings();
                LoadOrganizerSettings();
                SyncCaptureHotkeyLabel();
                UpdateScrubRange();
            }, "Main window opened");
        }, DispatcherPriority.Normal);

        FlashMiniPanel();
    }

    private void FlashMiniPanel()
    {
        MiniPanel.Opacity = 1;
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            Dispatcher.UIThread.Post(() =>
            {
                if (MiniPanelLock.IsChecked != true && !_miniPanelInteracting)
                    MiniPanel.Opacity = 0;
            });
        });
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (ViewModel is not null)
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _scrubBarFadeCts?.Cancel();
        _scrubBarFadeCts?.Dispose();
        _scrubBarFadeCts = null;
    }

    private void HandleClipboardPaste()
    {
        if (Application.Current is not App app) return;

        InMemoryImageSource? source = null;
        try
        {
            source = WindowsClipboardHelper.TryReadBitmapFromClipboard();
        }
        catch
        {
            // best effort — treat as no image
        }

        if (source is null)
        {
            ShowToast("No image on clipboard");
            return;
        }

        app.OpenMemoryViewer(source);
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        await RunLoggedAsync(async () =>
        {
            if (ViewModel is null) return;

            if (e.Key == Key.V && (e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                HandleClipboardPaste();
                e.Handled = true;
                return;
            }

            switch (e.Key)
            {
                case Key.Right:
                {
                    var msg = await ViewModel.NavigateAsync(1);
                    RefitImage();
                    if (msg is not null) ShowToast(msg);
                    break;
                }
                case Key.Left:
                {
                    var msg = await ViewModel.NavigateAsync(-1);
                    RefitImage();
                    if (msg is not null) ShowToast(msg);
                    break;
                }
                case Key.Delete:
                    await ViewModel.DeleteAsync();
                    RefitImage();
                    break;
                case Key.F:
                    ToggleFullscreen();
                    break;
                case Key.Q when e.KeyModifiers == KeyModifiers.None:
                    Close();
                    break;
                case Key.Z:
                    ViewModel.ResetZoom();
                    PositionImage();
                    break;
                case Key.X:
                    ViewModel.ToggleZoomFix();
                    SyncMenuChecks();
                    break;
                case Key.C when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                    await CopyRenderedImageToClipboardAsync();
                    break;
                case Key.C when e.KeyModifiers == KeyModifiers.None:
                    if (_capsActive)
                        await PerformCapsCapture();
                    break;
            }
        }, "Window key handler");
    }

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            _preFullscreenState = WindowState;
            _preFullscreenPosition = Position;
            _preFullscreenSize = new Size(Width, Height);
            _isFullscreen = true;
            WindowState = WindowState.FullScreen;
        }
        else
        {
            _isFullscreen = false;
            WindowState = _preFullscreenState;
            if (_preFullscreenState == WindowState.Normal)
            {
                Position = _preFullscreenPosition;
                Width = _preFullscreenSize.Width;
                Height = _preFullscreenSize.Height;
            }
        }

        SyncMenuChecks();
        Dispatcher.UIThread.Post(RefitImage, DispatcherPriority.Render);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.CurrentImage))
        {
            PositionImage();
        }

        if (e.PropertyName is nameof(MainWindowViewModel.CurrentImageLoadVersion))
        {
            var img = ViewModel?.CurrentImage;
            var newSize = img?.PixelSize ?? default;

            if (ViewModel is not null && !ViewModel.ZoomFix)
            {
                _lastFitSize = newSize;
                if (ViewerArea.Bounds.Width > 0 && ViewerArea.Bounds.Height > 0)
                    RefitImage();
                else
                    Dispatcher.UIThread.Post(RefitImage, DispatcherPriority.Render);
            }
            else
            {
                PositionImage();
            }
        }

        if (e.PropertyName is nameof(MainWindowViewModel.CurrentIndex) or nameof(MainWindowViewModel.ImageCount))
        {
            UpdateScrubRange();
        }

        if (e.PropertyName is nameof(MainWindowViewModel.AnimFrameCount))
        {
            FrameSlider.Maximum = Math.Max(0, (ViewModel?.AnimFrameCount ?? 1) - 1);
        }

        if (e.PropertyName is nameof(MainWindowViewModel.CurrentRating))
        {
            SyncStarButtons();
        }
    }

    private void PositionImage()
    {
        if (ViewModel is null) return;

        var viewW = ViewerArea.Bounds.Width;
        var viewH = ViewerArea.Bounds.Height;
        var img = ViewModel.CurrentImage;
        if (img is null || viewW <= 0 || viewH <= 0)
        {
            CheckerboardBg.IsVisible = false;
            return;
        }

        var imgW = img.PixelSize.Width * ViewModel.Zoom;
        var imgH = img.PixelSize.Height * ViewModel.Zoom;

        var left = (viewW - imgW) / 2.0 + ViewModel.OffsetX;
        var top = (viewH - imgH) / 2.0 + ViewModel.OffsetY;

        Canvas.SetLeft(MainImage, left);
        Canvas.SetTop(MainImage, top);

        MainImage.RenderTransform = new ScaleTransform(ViewModel.Zoom, ViewModel.Zoom);
        MainImage.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);

        SyncCheckerboard(left, top, imgW, imgH);
    }

    private void SyncCheckerboard(double left, double top, double imgW, double imgH)
    {
        Canvas.SetLeft(CheckerboardBg, left);
        Canvas.SetTop(CheckerboardBg, top);
        CheckerboardBg.Width = imgW;
        CheckerboardBg.Height = imgH;
        CheckerboardBg.IsVisible = ViewModel?.CurrentImage is not null;
    }

    private void RefitImage()
    {
        if (ViewModel is null) return;

        ViewModel.SetFitZoom(ViewerArea.Bounds.Width, ViewerArea.Bounds.Height);
        PositionImage();
    }

    private void OnViewerAreaResized()
    {
        if (ViewModel is null) return;

        if (!ViewModel.ZoomFix)
            RefitImage();
        else
            PositionImage();
    }

    private void UpdateScrubRange()
    {
        _syncingScrubSliders = true;
        if (ViewModel is null)
        {
            ScrubSlider.Maximum = 0;
            ScrubSlider.Value = 0;
            BottomScrubSlider.Maximum = 0;
            BottomScrubSlider.Value = 0;
            _syncingScrubSliders = false;
            return;
        }

        var max = Math.Max(0, ViewModel.ImageCount - 1);
        var value = Math.Clamp(ViewModel.CurrentIndex, 0, max);
        ScrubSlider.Maximum = max;
        ScrubSlider.Value = value;
        BottomScrubSlider.Maximum = max;
        BottomScrubSlider.Value = value;
        _syncingScrubSliders = false;
    }

    private async void OnPrevClick(object? sender, RoutedEventArgs e)
    {
        await RunLoggedAsync(async () =>
        {
            var msg = await (ViewModel?.NavigateAsync(-1) ?? Task.FromResult<string?>(null));
            RefitImage();
            if (msg is not null) ShowToast(msg);
        }, "Prev button");
    }

    private async void OnNextClick(object? sender, RoutedEventArgs e)
    {
        await RunLoggedAsync(async () =>
        {
            var msg = await (ViewModel?.NavigateAsync(1) ?? Task.FromResult<string?>(null));
            RefitImage();
            if (msg is not null) ShowToast(msg);
        }, "Next button");
    }

    private void OnToggleSidePanelClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.ToggleSidePanel();
        Dispatcher.UIThread.Post(RefitImage, DispatcherPriority.Render);
    }

    private async void OnStarClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        uint stars;
        if (btn.Tag is string tagStr)
        {
            if (!uint.TryParse(tagStr, out stars)) return;
        }
        else if (btn.Tag is int intVal)
        {
            stars = (uint)intVal;
        }
        else return;

        await RunLoggedAsync(async () =>
        {
            if (ViewModel is null) return;
            var newRating = ViewModel.CurrentRating == stars ? 0u : stars;
            await ViewModel.SetRatingAsync(newRating);
        }, "Star rating click");
    }

    private void SyncStarButtons()
    {
        var rating = ViewModel?.CurrentRating ?? 0;
        Button[] stars = [Star1, Star2, Star3, Star4, Star5];
        for (var i = 0; i < stars.Length; i++)
        {
            if (i < rating)
                stars[i].Classes.Add("active");
            else
                stars[i].Classes.Remove("active");
        }
    }

    private void OnMiniPanelPointerEntered(object? sender, PointerEventArgs e)
    {
        MiniPanel.Opacity = 1;
    }

    private void OnMiniPanelPointerExited(object? sender, PointerEventArgs e)
    {
        if (MiniPanelLock.IsChecked == true || _miniPanelInteracting) return;
        MiniPanel.Opacity = 0;
    }

    private void OnMiniPanelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _miniPanelInteracting = true;
    }

    private async void OnMiniPanelPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _miniPanelInteracting = false;

        var pos = e.GetPosition(MiniPanelHitZone);
        var bounds = MiniPanelHitZone.Bounds;
        if (pos.X < 0 || pos.Y < 0 || pos.X > bounds.Width || pos.Y > bounds.Height)
        {
            if (MiniPanelLock.IsChecked != true)
                MiniPanel.Opacity = 0;
        }

        await RunLoggedAsync(async () =>
        {
            if (ViewModel is null) return;
            await ViewModel.EnsureCurrentLoadedAsync();
            PositionImage();
        }, "Ensure current after scrub");
    }

    private void OnViewerPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_capsActive && _capsDrawing)
        {
            _capsEnd = e.GetPosition(ViewerArea);
            UpdateCapsRect();
            return;
        }

        if (!_isPanning || ViewModel is null) return;

        var current = e.GetPosition(ViewerArea);
        var dx = current.X - _lastPanPoint.X;
        var dy = current.Y - _lastPanPoint.Y;
        _lastPanPoint = current;
        ViewModel.PanBy(dx, dy);
        PositionImage();
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;

        if (props.IsXButton1Pressed)
        {
            e.Handled = true;
            _ = RunLoggedAsync(async () =>
            {
                var msg = await (ViewModel?.NavigateAsync(-1) ?? Task.FromResult<string?>(null));
                RefitImage();
                if (msg is not null) ShowToast(msg);
            }, "XButton1 nav");
        }
        else if (props.IsXButton2Pressed)
        {
            e.Handled = true;
            _ = RunLoggedAsync(async () =>
            {
                var msg = await (ViewModel?.NavigateAsync(1) ?? Task.FromResult<string?>(null));
                RefitImage();
                if (msg is not null) ShowToast(msg);
            }, "XButton2 nav");
        }
    }

    private void OnViewerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(ViewerArea).Properties;

        if (props.IsMiddleButtonPressed)
        {
            _isPanning = true;
            _lastPanPoint = e.GetPosition(ViewerArea);
            e.Pointer.Capture(ViewerArea);
            return;
        }

        if (props.IsLeftButtonPressed)
        {
            if (_capsActive)
            {
                _capsDrawing = true;
                _capsStart = e.GetPosition(ViewerArea);
                _capsEnd = _capsStart;
                CapsBorder.IsVisible = true;
                CapsOverlayCanvas.IsVisible = true;
                UpdateCapsRect();
                e.Pointer.Capture(ViewerArea);
                return;
            }

            _isPanning = true;
            _lastPanPoint = e.GetPosition(ViewerArea);
            e.Pointer.Capture(ViewerArea);
        }
    }

    private void OnViewerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_capsDrawing)
        {
            _capsDrawing = false;
            e.Pointer.Capture(null);

            if (CapsAutoCapCheckbox.IsChecked == true && CapsBorder.IsVisible)
            {
                _ = RunLoggedAsync(async () => await PerformCapsCapture(), "Auto cap on mouse-up");
            }
            return;
        }

        _isPanning = false;
        e.Pointer.Capture(null);
    }

    private void OnViewerPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (ViewModel is null) return;

        var viewportPoint = e.GetPosition(ViewerArea);
        var viewW = ViewerArea.Bounds.Width;
        var viewH = ViewerArea.Bounds.Height;
        var img = ViewModel.CurrentImage;
        if (img is null || viewW <= 0 || viewH <= 0) return;

        var oldZoom = ViewModel.Zoom;
        var factor = e.Delta.Y > 0 ? 1.15 : 1.0 / 1.15;
        var newZoom = Math.Clamp(oldZoom * factor, 0.05, 32.0);
        if (Math.Abs(newZoom - oldZoom) < 0.0001) return;

        var imgW = img.PixelSize.Width;
        var imgH = img.PixelSize.Height;

        var currentLeft = (viewW - imgW * oldZoom) / 2.0 + ViewModel.OffsetX;
        var currentTop = (viewH - imgH * oldZoom) / 2.0 + ViewModel.OffsetY;

        var imgX = (viewportPoint.X - currentLeft) / oldZoom;
        var imgY = (viewportPoint.Y - currentTop) / oldZoom;

        var newCenterOffsetX = (viewW - imgW * newZoom) / 2.0;
        var newCenterOffsetY = (viewH - imgH * newZoom) / 2.0;

        var newLeft = viewportPoint.X - imgX * newZoom;
        var newTop = viewportPoint.Y - imgY * newZoom;

        ViewModel.SetZoomDirect(newZoom, newLeft - newCenterOffsetX, newTop - newCenterOffsetY);
        PositionImage();
    }

    private async void OnScrubSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_uiReady || ViewModel is null || _syncingScrubSliders) return;

        _syncingScrubSliders = true;
        BottomScrubSlider.Value = e.NewValue;
        _syncingScrubSliders = false;

        var idx = (int)Math.Round(e.NewValue);
        await RunLoggedAsync(async () =>
        {
            await ViewModel.SetIndexAsync(idx);
            RefitImage();
        }, "Scrub slider");
    }

    private async void OnBottomScrubSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_uiReady || ViewModel is null || _syncingScrubSliders) return;

        ShowScrubBar();
        ScheduleScrubBarFade(force: true, delayMs: 300);

        _syncingScrubSliders = true;
        ScrubSlider.Value = e.NewValue;
        _syncingScrubSliders = false;

        var idx = (int)Math.Round(e.NewValue);
        await RunLoggedAsync(async () =>
        {
            await ViewModel.SetIndexAsync(idx);
            RefitImage();
        }, "Bottom scrub slider");
    }

    private void OnScrubBarPointerEntered(object? sender, PointerEventArgs e)
    {
        ShowScrubBar();
    }

    private void OnScrubBarPointerExited(object? sender, PointerEventArgs e)
    {
        ScheduleScrubBarFade(force: true);
    }

    private void OnScrubBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _scrubBarInteracting = true;
        ShowScrubBar();

        if (!e.GetCurrentPoint(ScrubBarHitZone).Properties.IsLeftButtonPressed)
            return;

        _scrubBarDragging = true;
        e.Pointer.Capture(ScrubBarHitZone);
        UpdateScrubFromPointer(e.GetPosition(BottomScrubSlider).X);
        e.Handled = true;
    }

    private void OnScrubBarPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_scrubBarDragging) return;
        UpdateScrubFromPointer(e.GetPosition(BottomScrubSlider).X);
    }

    private async void OnScrubBarPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        EndScrubBarDrag(e.Pointer);

        _scrubBarInteracting = false;
        ScheduleScrubBarFade(force: true);

        await RunLoggedAsync(async () =>
        {
            if (ViewModel is null) return;
            await ViewModel.EnsureCurrentLoadedAsync();
            RefitImage();
        }, "Ensure current after bottom scrub");
    }

    private void OnScrubBarPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndScrubBarDrag(null);
        _scrubBarInteracting = false;
        ScheduleScrubBarFade(force: true);
    }

    private void EndScrubBarDrag(IPointer? pointer)
    {
        if (!_scrubBarDragging) return;
        _scrubBarDragging = false;
        pointer?.Capture(null);
    }

    private void UpdateScrubFromPointer(double xRelativeToSlider)
    {
        if (BottomScrubSlider.Maximum <= 0) return;

        var width = BottomScrubSlider.Bounds.Width;
        if (width <= 0) return;

        const double thumbHalfWidth = 8.0;
        var trackStart = thumbHalfWidth;
        var trackLength = width - thumbHalfWidth * 2;
        if (trackLength <= 0) return;

        var proportion = Math.Clamp((xRelativeToSlider - trackStart) / trackLength, 0.0, 1.0);
        var target = Math.Round(proportion * BottomScrubSlider.Maximum);

        if (Math.Abs(target - BottomScrubSlider.Value) > 0.0001)
            BottomScrubSlider.Value = target;
    }

    private void ShowScrubBar()
    {
        _scrubBarFadeCts?.Cancel();
        ScrubBar.Opacity = 0.75;
    }

    private void ScheduleScrubBarFade(bool force, int delayMs = 0)
    {
        _scrubBarFadeCts?.Cancel();
        _scrubBarFadeCts?.Dispose();
        _scrubBarFadeCts = new CancellationTokenSource();
        var token = _scrubBarFadeCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                if (delayMs > 0)
                    await Task.Delay(delayMs, token);
                Dispatcher.UIThread.Post(() =>
                {
                    if (token.IsCancellationRequested) return;
                    if (!force && _scrubBarInteracting) return;
                    ScrubBar.Opacity = 0;
                });
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private void OnRotateCwClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.RotateCw();
        RefitImage();
    }

    private void OnRotateCcwClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.RotateCcw();
        RefitImage();
    }

    private void OnFlipHorizontalClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.FlipHorizontal();
        PositionImage();
    }

    private void OnFlipVerticalClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.FlipVertical();
        PositionImage();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        await RunLoggedAsync(async () =>
        {
            if (ViewModel is null) return;
            await ViewModel.SaveAsync();
            RefitImage();
        }, "Save");
    }

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
    {
        await RunLoggedAsync(async () =>
        {
            if (ViewModel is null) return;

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Image As",
                DefaultExtension = Path.GetExtension(ViewModel.CurrentEntry?.FullPath ?? ".png").TrimStart('.'),
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } },
                    new FilePickerFileType("JPEG") { Patterns = new[] { "*.jpg", "*.jpeg" } },
                    new FilePickerFileType("WebP") { Patterns = new[] { "*.webp" } },
                    new FilePickerFileType("BMP") { Patterns = new[] { "*.bmp" } },
                },
            });

            if (file is null) return;
            await ViewModel.SaveAsAsync(file.Path.LocalPath);
        }, "Save As");
    }

    private void OnPlayPauseClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.ToggleAnimPlayPause();
    }

    private void OnFrameSliderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _frameSliderInteracting = true;
    }

    private void OnFrameSliderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _frameSliderInteracting = false;
    }

    private void OnFrameSliderPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _frameSliderInteracting = false;
    }

    private void OnFrameSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_uiReady || ViewModel is null) return;

        if (ViewModel.IsAnimPlaying && !_frameSliderInteracting) return;

        ViewModel.SeekAnimFrame((int)Math.Round(e.NewValue));
    }

    #region Drag and Drop

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        await RunLoggedAsync(async () =>
        {
            if (ViewModel is null) return;

#pragma warning disable CS0618
            var files = e.Data.GetFiles();
#pragma warning restore CS0618
            if (files is null) return;

            foreach (var item in files)
            {
                var path = item.Path?.LocalPath;
                if (path is null) continue;

                if (SupportedFormats.IsSupported(path))
                {
                    await ViewModel.LoadFolderAndSelectAsync(path);
                    RefitImage();
                    return;
                }
            }
        }, "Drag-and-drop load");
    }

    #endregion

    #region Caps Tool

    private void OnCapsActiveChanged(object? sender, RoutedEventArgs e)
    {
        if (!_uiReady) return;
        _capsActive = CapsActiveCheckbox.IsChecked == true;
        CapsOverlayCanvas.IsHitTestVisible = _capsActive;

        if (!_capsActive)
        {
            CapsBorder.IsVisible = false;
            CapsOverlayCanvas.IsVisible = false;
        }

        SaveCapsSettings();
    }

    private void OnCapsOptionChanged(object? sender, RoutedEventArgs e)
    {
        if (!_uiReady) return;

        if (CapsFixedCheckbox.IsChecked == true && sender == CapsFixedCheckbox)
            CapsAspectCheckbox.IsChecked = false;
        else if (CapsAspectCheckbox.IsChecked == true && sender == CapsAspectCheckbox)
            CapsFixedCheckbox.IsChecked = false;

        SyncCapsPanelVisibility();
        SaveCapsSettings();
    }

    private void SyncCapsPanelVisibility()
    {
        CapsAspectPanel.IsVisible = CapsAspectCheckbox.IsChecked == true;
        CapsFixedPanel.IsVisible = CapsFixedCheckbox.IsChecked == true;
        CapsResizePanel.IsVisible = CapsResizeCheckbox.IsChecked == true;
        CapsSavePanel.IsVisible = CapsSaveCheckbox.IsChecked == true;
    }

    private void LoadCapsSettings()
    {
        if (ViewModel is null) return;
        _loadingCaps = true;
        var caps = ViewModel.CapsSettings;

        _capsActive = caps.CapsEnabled;
        CapsActiveCheckbox.IsChecked = caps.CapsEnabled;
        CapsOverlayCanvas.IsHitTestVisible = _capsActive;

        CapsAspectCheckbox.IsChecked = caps.AspectRatioEnabled;
        CapsAspectX.Text = caps.AspectRatioX.ToString();
        CapsAspectY.Text = caps.AspectRatioY.ToString();

        CapsFixedCheckbox.IsChecked = caps.FixedSizeEnabled;
        CapsFixedW.Text = caps.FixedWidth.ToString();
        CapsFixedH.Text = caps.FixedHeight.ToString();

        CapsResizeCheckbox.IsChecked = caps.ResizeLargestDimensionEnabled;
        CapsResizeValue.Text = caps.ResizeLargestDimension.ToString();

        CapsFormatCombo.SelectedIndex = caps.OutputFormat switch
        {
            CapsOutputFormat.Png => 1,
            CapsOutputFormat.Jpeg => 2,
            CapsOutputFormat.WebP => 3,
            _ => 0,
        };

        CapsSaveCheckbox.IsChecked = caps.SaveCapsEnabled;
        CapsSaveDir.Text = caps.SaveCapsDirectory ?? string.Empty;

        CapsClipboardCheckbox.IsChecked = caps.CopyToClipboard;
        CapsOriginalPixelsCheckbox.IsChecked = caps.OriginalPixels;
        CapsAutoCapCheckbox.IsChecked = caps.AutoCap;

        SyncCapsPanelVisibility();
        _loadingCaps = false;
    }

    private void SaveCapsSettings()
    {
        if (!_uiReady || _loadingCaps || ViewModel is null) return;
        var caps = ViewModel.CapsSettings;

        caps.CapsEnabled = CapsActiveCheckbox.IsChecked == true;
        caps.AutoCap = CapsAutoCapCheckbox.IsChecked == true;

        caps.AspectRatioEnabled = CapsAspectCheckbox.IsChecked == true;
        caps.AspectRatioX = int.TryParse(CapsAspectX.Text, out var ax) ? ax : 16;
        caps.AspectRatioY = int.TryParse(CapsAspectY.Text, out var ay) ? ay : 9;

        caps.FixedSizeEnabled = CapsFixedCheckbox.IsChecked == true;
        caps.FixedWidth = int.TryParse(CapsFixedW.Text, out var fw) ? fw : 640;
        caps.FixedHeight = int.TryParse(CapsFixedH.Text, out var fh) ? fh : 480;

        caps.ResizeLargestDimensionEnabled = CapsResizeCheckbox.IsChecked == true;
        caps.ResizeLargestDimension = int.TryParse(CapsResizeValue.Text, out var rv) ? rv : 1280;

        caps.OutputFormat = CapsFormatCombo.SelectedIndex switch
        {
            1 => CapsOutputFormat.Png,
            2 => CapsOutputFormat.Jpeg,
            3 => CapsOutputFormat.WebP,
            _ => CapsOutputFormat.SameAsSource,
        };

        caps.SaveCapsEnabled = CapsSaveCheckbox.IsChecked == true;
        caps.SaveCapsDirectory = string.IsNullOrWhiteSpace(CapsSaveDir.Text) ? null : CapsSaveDir.Text;

        caps.CopyToClipboard = CapsClipboardCheckbox.IsChecked == true;
        caps.OriginalPixels = CapsOriginalPixelsCheckbox.IsChecked == true;

        ViewModel.PersistCapsSettings();
    }

    private void PersistWindowGeometry()
    {
        if (!_uiReady || ViewModel is null || _isFullscreen) return;

        var geo = ViewModel.Settings.Window ??= new WindowGeometry();
        geo.IsMaximized = WindowState == WindowState.Maximized;

        if (WindowState == WindowState.Normal)
        {
            geo.X = Position.X;
            geo.Y = Position.Y;
            var w = (int)Bounds.Width;
            var h = (int)Bounds.Height;
            if (w > 0 && h > 0)
            {
                geo.Width = w;
                geo.Height = h;
            }
        }

        if (Application.Current is App app)
            app.LastWindowGeometry = geo;

        ViewModel.PersistSettings();
    }

    private async void OnCapsBrowseClick(object? sender, RoutedEventArgs e)
    {
        await RunLoggedAsync(async () =>
        {
            var startDir = Path.GetDirectoryName(ViewModel?.CurrentEntry?.FullPath);
            IStorageFolder? suggestedStart = null;
            if (!string.IsNullOrEmpty(startDir))
                suggestedStart = await StorageProvider.TryGetFolderFromPathAsync(startDir);

            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Caps Save Folder",
                AllowMultiple = false,
                SuggestedStartLocation = suggestedStart,
            });

            if (folders.Count > 0)
            {
                CapsSaveDir.Text = folders[0].Path.LocalPath;
                SaveCapsSettings();
            }
        }, "Caps browse folder");
    }

    private void LoadOrganizerSettings()
    {
        if (ViewModel is null) return;
        MoveDir.Text = ViewModel.OrganizerSettings.MoveDirectory ?? string.Empty;
    }

    private void SaveOrganizerSettings()
    {
        if (!_uiReady || ViewModel is null) return;
        ViewModel.OrganizerSettings.MoveDirectory = string.IsNullOrWhiteSpace(MoveDir.Text) ? null : MoveDir.Text;
        ViewModel.PersistOrganizerSettings();
    }

    private async void OnMoveBrowseClick(object? sender, RoutedEventArgs e)
    {
        await RunLoggedAsync(async () =>
        {
            var startDir = !string.IsNullOrWhiteSpace(MoveDir.Text)
                ? MoveDir.Text
                : Path.GetDirectoryName(ViewModel?.CurrentEntry?.FullPath);

            IStorageFolder? suggestedStart = null;
            if (!string.IsNullOrEmpty(startDir))
                suggestedStart = await StorageProvider.TryGetFolderFromPathAsync(startDir);

            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Move Folder",
                AllowMultiple = false,
                SuggestedStartLocation = suggestedStart,
            });

            if (folders.Count > 0)
            {
                MoveDir.Text = folders[0].Path.LocalPath;
                SaveOrganizerSettings();
            }
        }, "Move browse folder");
    }

    private async void OnMovePrevClick(object? sender, RoutedEventArgs e)
    {
        await RunLoggedAsync(async () =>
        {
            var msg = await (ViewModel?.MoveCurrentAsync(-1, MoveDir.Text) ?? Task.FromResult<string?>(null));
            RefitImage();
            if (msg is not null) ShowToast(msg);
        }, "Move go prev");
    }

    private async void OnMoveNextClick(object? sender, RoutedEventArgs e)
    {
        await RunLoggedAsync(async () =>
        {
            var msg = await (ViewModel?.MoveCurrentAsync(1, MoveDir.Text) ?? Task.FromResult<string?>(null));
            RefitImage();
            if (msg is not null) ShowToast(msg);
        }, "Move go next");
    }

    private async void OnCopyPrevClick(object? sender, RoutedEventArgs e)
    {
        await RunLoggedAsync(async () =>
        {
            var msg = await (ViewModel?.CopyCurrentAsync(-1, MoveDir.Text) ?? Task.FromResult<string?>(null));
            RefitImage();
            if (msg is not null) ShowToast(msg);
        }, "Copy go prev");
    }

    private async void OnCopyNextClick(object? sender, RoutedEventArgs e)
    {
        await RunLoggedAsync(async () =>
        {
            var msg = await (ViewModel?.CopyCurrentAsync(1, MoveDir.Text) ?? Task.FromResult<string?>(null));
            RefitImage();
            if (msg is not null) ShowToast(msg);
        }, "Copy go next");
    }

    private async void OnCapsCapture(object? sender, RoutedEventArgs e)
    {
        await PerformCapsCapture();
    }

    private async void OnDeletePrevClick(object? sender, RoutedEventArgs e)
    {
        await RunLoggedAsync(async () =>
        {
            await (ViewModel?.DeleteAsync(-1) ?? Task.CompletedTask);
            RefitImage();
        }, "Delete go prev");
    }

    private async void OnDeleteNextClick(object? sender, RoutedEventArgs e)
    {
        await RunLoggedAsync(async () =>
        {
            await (ViewModel?.DeleteAsync(1) ?? Task.CompletedTask);
            RefitImage();
        }, "Delete go next");
    }

    private async Task PerformCapsCapture()
    {
        await RunLoggedAsync(async () =>
        {
            if (ViewModel?.CurrentImage is null) return;

            bool hasSelection = CapsBorder.IsVisible;
            SKBitmap? sourceBitmap = null;
            SKBitmap? resizedBitmap = null;
            RenderTargetBitmap? renderTarget = null;

            try
            {
                if (hasSelection)
                {
                    var left = Canvas.GetLeft(CapsBorder);
                    var top = Canvas.GetTop(CapsBorder);
                    var w = CapsBorder.Width;
                    var h = CapsBorder.Height;

                    if (double.IsNaN(left) || double.IsNaN(top) || w <= 0 || h <= 0) return;

                    if (CapsOriginalPixelsCheckbox.IsChecked == true)
                    {
                        sourceBitmap = CreateOriginalPixelsCapsBitmap(left, top, w, h);
                        if (sourceBitmap is null) return;
                    }
                    else
                    {
                        renderTarget = new RenderTargetBitmap(
                            new Avalonia.PixelSize((int)ViewerArea.Bounds.Width, (int)ViewerArea.Bounds.Height));
                        renderTarget.Render(ViewerArea);

                        using var ms = new MemoryStream();
                        renderTarget.Save(ms);
                        ms.Position = 0;
                        using var skBmp = SKBitmap.Decode(ms);

                        if (skBmp is null) return;

                        var srcRect = new SKRectI(
                            Math.Max(0, (int)left),
                            Math.Max(0, (int)top),
                            Math.Min(skBmp.Width, (int)(left + w)),
                            Math.Min(skBmp.Height, (int)(top + h)));

                        if (srcRect.Width <= 0 || srcRect.Height <= 0) return;

                        sourceBitmap = new SKBitmap(srcRect.Width, srcRect.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                        skBmp.ExtractSubset(sourceBitmap, srcRect);
                    }

                    var result = ApplyCapsConstraints(sourceBitmap.Width, sourceBitmap.Height);

                    if (result.Width != sourceBitmap.Width || result.Height != sourceBitmap.Height)
                    {
                        resizedBitmap = sourceBitmap.Resize(new SKImageInfo(result.Width, result.Height, SKColorType.Bgra8888, SKAlphaType.Premul), SKFilterQuality.High);
                    }
                }
                else
                {
                    var data = ViewModel.GetCurrentPixelData();
                    if (data is null) return;

                    var (pixels, imgW, imgH, stride) = data.Value;
                    sourceBitmap = new SKBitmap(new SKImageInfo(imgW, imgH, SKColorType.Bgra8888, SKAlphaType.Premul));
                    var dstPtr = sourceBitmap.GetPixels();
                    var dstStride = sourceBitmap.RowBytes;
                    for (int y = 0; y < imgH; y++)
                        Marshal.Copy(pixels, y * stride, dstPtr + y * dstStride, Math.Min(stride, dstStride));

                    var result = ApplyCapsConstraints(imgW, imgH);
                    if (result.Width != imgW || result.Height != imgH)
                    {
                        resizedBitmap = sourceBitmap.Resize(new SKImageInfo(result.Width, result.Height, SKColorType.Bgra8888, SKAlphaType.Premul), SKFilterQuality.High);
                    }
                }

                var finalBitmap = resizedBitmap ?? sourceBitmap;

                var format = GetCapsOutputFormat();
                var ext = format switch
                {
                    CapsOutputFormat.Png => "png",
                    CapsOutputFormat.Jpeg => "jpg",
                    CapsOutputFormat.WebP => "webp",
                    _ => Path.GetExtension(ViewModel.CurrentEntry?.FullPath ?? ".png").TrimStart('.'),
                };

                if (string.IsNullOrWhiteSpace(ext)) ext = "png";

                var fileName = ViewModel.BuildCapsFileName(ViewModel.CurrentEntry?.Name ?? "capture", ext, _capsSequence++);

                if (CapsSaveCheckbox.IsChecked == true)
                {
                    var saveDir = string.IsNullOrWhiteSpace(CapsSaveDir.Text)
                        ? Path.GetDirectoryName(ViewModel.CurrentEntry?.FullPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                        : CapsSaveDir.Text;

                    Directory.CreateDirectory(saveDir);
                    var fullPath = Path.Combine(saveDir, fileName);

                    var skFormat = format switch
                    {
                        CapsOutputFormat.Jpeg => SKEncodedImageFormat.Jpeg,
                        CapsOutputFormat.WebP => SKEncodedImageFormat.Webp,
                        _ => SKEncodedImageFormat.Png,
                    };

                    using var encoded = finalBitmap.Encode(skFormat, 92);
                    await using var fileStream = File.Create(fullPath);
                    encoded.SaveTo(fileStream);
                }

                if (CapsClipboardCheckbox.IsChecked == true)
                {
                    CopyBitmapToNativeClipboard(finalBitmap);
                }

                CapsBorder.IsVisible = false;
                CapsOverlayCanvas.IsVisible = false;
            }
            finally
            {
                resizedBitmap?.Dispose();
                sourceBitmap?.Dispose();
                renderTarget?.Dispose();
            }
        }, "Caps capture");
    }

    private SKBitmap? CreateOriginalPixelsCapsBitmap(double selectionLeft, double selectionTop, double selectionWidth, double selectionHeight)
    {
        if (ViewModel is null || ViewModel.CurrentImage is null || ViewModel.Zoom <= 0)
            return null;

        var imageLeft = Canvas.GetLeft(MainImage);
        var imageTop = Canvas.GetTop(MainImage);
        if (double.IsNaN(imageLeft) || double.IsNaN(imageTop))
            return null;

        var data = ViewModel.GetCurrentPixelData();
        if (data is null)
            return null;

        var (pixels, imgW, imgH, stride) = data.Value;
        var sourceLeft = (selectionLeft - imageLeft) / ViewModel.Zoom;
        var sourceTop = (selectionTop - imageTop) / ViewModel.Zoom;
        var sourceRight = (selectionLeft + selectionWidth - imageLeft) / ViewModel.Zoom;
        var sourceBottom = (selectionTop + selectionHeight - imageTop) / ViewModel.Zoom;

        var srcRect = new SKRectI(
            Math.Clamp((int)Math.Floor(sourceLeft), 0, imgW),
            Math.Clamp((int)Math.Floor(sourceTop), 0, imgH),
            Math.Clamp((int)Math.Ceiling(sourceRight), 0, imgW),
            Math.Clamp((int)Math.Ceiling(sourceBottom), 0, imgH));

        if (srcRect.Width <= 0 || srcRect.Height <= 0)
            return null;

        var bitmap = new SKBitmap(new SKImageInfo(srcRect.Width, srcRect.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var dstPtr = bitmap.GetPixels();
        var dstStride = bitmap.RowBytes;
        var bytesPerRow = srcRect.Width * 4;

        for (var y = 0; y < srcRect.Height; y++)
        {
            var srcOffset = (srcRect.Top + y) * stride + srcRect.Left * 4;
            Marshal.Copy(pixels, srcOffset, dstPtr + y * dstStride, bytesPerRow);
        }

        return bitmap;
    }

    private Core.Caps.PixelSize ApplyCapsConstraints(int sourceW, int sourceH)
    {
        var options = new CapsConstraintOptions
        {
            AspectRatioEnabled = CapsAspectCheckbox.IsChecked == true,
            AspectRatioX = int.TryParse(CapsAspectX.Text, out var ax) ? ax : 1,
            AspectRatioY = int.TryParse(CapsAspectY.Text, out var ay) ? ay : 1,
            FixedSizeEnabled = CapsFixedCheckbox.IsChecked == true,
            FixedWidth = int.TryParse(CapsFixedW.Text, out var fw) ? fw : sourceW,
            FixedHeight = int.TryParse(CapsFixedH.Text, out var fh) ? fh : sourceH,
            ResizeLargestDimensionEnabled = CapsResizeCheckbox.IsChecked == true,
            ResizeLargestDimension = int.TryParse(CapsResizeValue.Text, out var rv) ? rv : 0,
        };

        return CapsConstraintEvaluator.ApplyCaptureModes(new Core.Caps.PixelSize(sourceW, sourceH), options);
    }

    private CapsOutputFormat GetCapsOutputFormat()
    {
        return CapsFormatCombo.SelectedIndex switch
        {
            1 => CapsOutputFormat.Png,
            2 => CapsOutputFormat.Jpeg,
            3 => CapsOutputFormat.WebP,
            _ => CapsOutputFormat.SameAsSource,
        };
    }

    private void UpdateCapsRect()
    {
        var w = Math.Abs(_capsEnd.X - _capsStart.X);
        var h = Math.Abs(_capsEnd.Y - _capsStart.Y);

        if (CapsAspectCheckbox.IsChecked == true)
        {
            if (int.TryParse(CapsAspectX.Text, out var ax) && int.TryParse(CapsAspectY.Text, out var ay) && ax > 0 && ay > 0)
            {
                var ratio = (double)ax / ay;
                if (w / h > ratio)
                    w = h * ratio;
                else
                    h = w / ratio;
            }
        }
        else if (CapsFixedCheckbox.IsChecked == true)
        {
            if (int.TryParse(CapsFixedW.Text, out var fw) && int.TryParse(CapsFixedH.Text, out var fh) && fw > 0 && fh > 0)
            {
                w = fw;
                h = fh;
            }
        }

        var x = _capsEnd.X >= _capsStart.X ? _capsStart.X : _capsStart.X - w;
        var y = _capsEnd.Y >= _capsStart.Y ? _capsStart.Y : _capsStart.Y - h;

        Canvas.SetLeft(CapsBorder, x);
        Canvas.SetTop(CapsBorder, y);
        CapsBorder.Width = Math.Max(1, w);
        CapsBorder.Height = Math.Max(1, h);
    }

    #endregion

    #region Context Menu

    private void BuildContextMenu()
    {
        var showInExplorer = new MenuItem { Header = "Show in Explorer" };
        showInExplorer.Click += async (_, _) => await RunLoggedAsync(() => ViewModel?.ShowInExplorerAsync() ?? Task.CompletedTask, "Context Show in Explorer");

        var properties = new MenuItem { Header = "Properties" };
        properties.Click += async (_, _) => await RunLoggedAsync(() => ViewModel?.OpenPropertiesAsync() ?? Task.CompletedTask, "Context Properties");

        var copyImage = new MenuItem { Header = "Copy image to clipboard" };
        copyImage.Click += async (_, _) => await CopyRenderedImageToClipboardAsync();

        var settings = new MenuItem { Header = "Settings" };

        _setCapHotkeyMenuItem = new MenuItem
        {
            Header = $"Set Cap Hotkey: {GetCurrentCaptureHotkeyDisplay()}",
            StaysOpenOnClick = true,
        };
        _setCapHotkeyMenuItem.Click += (_, _) => BeginCaptureHotkeySetting(_setCapHotkeyMenuItem);
        settings.Items.Add(_setCapHotkeyMenuItem);

        var showExif = new MenuItem { Header = "Show/Hide EXIF" };
        showExif.Click += (_, _) => ViewModel?.ToggleExifOverlay();

        _zoomFixMenuItem = new MenuItem { Header = "Zoom: Fix", ToggleType = MenuItemToggleType.CheckBox };
        _zoomFixMenuItem.Click += (_, _) =>
        {
            ViewModel?.ToggleZoomFix();
            SyncMenuChecks();
        };

        var zoom100 = new MenuItem { Header = "Zoom: 100%" };
        zoom100.Click += (_, _) =>
        {
            ViewModel?.ResetZoom();
            PositionImage();
        };

        _fullscreenMenuItem = new MenuItem { Header = "Fullscreen", ToggleType = MenuItemToggleType.CheckBox };
        _fullscreenMenuItem.Click += (_, _) => ToggleFullscreen();

        var sortBy = new MenuItem { Header = "Sort By" };
        sortBy.ItemsSource = new object[]
        {
            MakeSortMenuItem("Name", SortField.Name),
            MakeSortMenuItem("Date Modified", SortField.DateModified),
            MakeSortMenuItem("Size", SortField.Size),
            MakeSortMenuItem("Type", SortField.Type),
            MakeSortMenuItem("Rating", SortField.Rating),
            new Separator(),
            MakeSortDirectionToggle(),
        };

        var print = new MenuItem { Header = "Print" };
        print.Click += async (_, _) => await RunLoggedAsync(() => ViewModel?.PrintAsync() ?? Task.CompletedTask, "Context print");

        var rate = new MenuItem { Header = "Rate" };
        rate.ItemsSource = new object[]
        {
            MakeRateMenuItem(1),
            MakeRateMenuItem(2),
            MakeRateMenuItem(3),
            MakeRateMenuItem(4),
            MakeRateMenuItem(5),
        };

        var file = new MenuItem { Header = "File" };
        var fileCopy = new MenuItem { Header = "Copy" };
        fileCopy.Click += async (_, _) => await RunLoggedAsync(() => CopyCurrentFileDropListAsync(cut: false), "Context file copy");

        var fileCut = new MenuItem { Header = "Cut" };
        fileCut.Click += async (_, _) => await RunLoggedAsync(() => CopyCurrentFileDropListAsync(cut: true), "Context file cut");

        var fileDelete = new MenuItem { Header = "Delete" };
        fileDelete.Click += async (_, _) => await RunLoggedAsync(() => ViewModel?.DeleteAsync() ?? Task.CompletedTask, "Context delete");

        file.ItemsSource = new object[] { fileCopy, fileCut, fileDelete };

        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => Close();

        var contextMenu = new ContextMenu
        {
            ItemsSource = new object[]
            {
                showInExplorer,
                properties,
                copyImage,
                settings,
                showExif,
                _zoomFixMenuItem,
                zoom100,
                _fullscreenMenuItem,
                sortBy,
                print,
                rate,
                file,
                exit,
            },
        };

        contextMenu.Opening += (_, _) =>
        {
            SyncMenuChecks();
            SyncCaptureHotkeyLabel();
        };
        ViewerArea.ContextMenu = contextMenu;
    }

    private MenuItem MakeSortMenuItem(string label, SortField field)
    {
        var menuItem = new MenuItem { Header = label };
        _sortFieldMenuItems.Add((menuItem, field, label));
        menuItem.Click += async (_, _) => await RunLoggedAsync(async () =>
        {
            await (ViewModel?.SetSortAsync(field) ?? Task.CompletedTask);
            SyncSortFieldLabels();
            RefitImage();
        }, $"Sort {label}");
        return menuItem;
    }

    private void SyncSortFieldLabels()
    {
        var current = ViewModel?.SortField ?? SortField.Name;
        foreach (var (item, field, label) in _sortFieldMenuItems)
            item.Header = field == current ? $"{label} ✓" : label;
    }

    private MenuItem MakeSortDirectionToggle()
    {
        var menuItem = new MenuItem();
        _sortDirectionMenuItem = menuItem;
        SyncSortDirectionLabel();
        menuItem.Click += async (_, _) => await RunLoggedAsync(async () =>
        {
            await (ViewModel?.ToggleSortDirectionAsync() ?? Task.CompletedTask);
            SyncSortDirectionLabel();
            RefitImage();
        }, "Toggle sort direction");
        return menuItem;
    }

    private void SyncSortDirectionLabel()
    {
        if (_sortDirectionMenuItem is null) return;
        var dir = ViewModel?.SortDirection ?? SortDirection.Ascending;
        _sortDirectionMenuItem.Header = dir == SortDirection.Ascending ? "Ascending ✓" : "Descending ✓";
    }

    private MenuItem MakeRateMenuItem(uint rating)
    {
        var item = new MenuItem { Header = rating.ToString() };
        item.Click += async (_, _) => await RunLoggedAsync(() => ViewModel?.SetRatingAsync(rating) ?? Task.CompletedTask, "Set rating");
        return item;
    }

    private string GetCurrentCaptureHotkeyDisplay()
    {
        try
        {
            var settings = ViewModel?.Settings?.Capture?.Hotkey;
            return settings?.Display ?? HotkeyBinding.DefaultCapture.Display;
        }
        catch
        {
            return HotkeyBinding.DefaultCapture.Display;
        }
    }

    private void SyncCaptureHotkeyLabel()
    {
        if (_setCapHotkeyMenuItem is null) return;
        if (_setCapHotkeyMenuItem.Header?.ToString()?.StartsWith("Use hotkey now", StringComparison.Ordinal) == true)
            return;

        _setCapHotkeyMenuItem.Header = $"Set Cap Hotkey: {GetCurrentCaptureHotkeyDisplay()}";
    }

    private void BeginCaptureHotkeySetting(MenuItem item)
    {
        if (item.Header?.ToString()?.StartsWith("Use hotkey now", StringComparison.Ordinal) == true)
            return;

        var originalHeader = item.Header;
        item.Header = "Use hotkey now (Esc to cancel)";
        Focus();

        var captured = false;

        EventHandler<KeyEventArgs>? keyHandler = null;
        EventHandler<RoutedEventArgs>? lostFocusHandler = null;
        EventHandler? closedHandler = null;

        void Cleanup()
        {
            RemoveHandler(KeyDownEvent, keyHandler);
            item.RemoveHandler(KeyDownEvent, keyHandler);
            LostFocus -= lostFocusHandler;
            Closed -= closedHandler;
        }

        keyHandler = async (_, e) =>
        {
            if (captured) return;

            if (e.Key == Key.Escape)
            {
                captured = true;
                item.Header = originalHeader;
                Cleanup();
                e.Handled = true;
                return;
            }

            var modifiers = e.KeyModifiers;
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                e.Key == Key.LWin || e.Key == Key.RWin)
            {
                return;
            }

            var keyName = e.Key.ToString();
            if (string.IsNullOrEmpty(keyName)) return;

            captured = true;
            var binding = new HotkeyBinding
            {
                Ctrl = (modifiers & KeyModifiers.Control) != 0,
                Alt = (modifiers & KeyModifiers.Alt) != 0,
                Shift = (modifiers & KeyModifiers.Shift) != 0,
                Win = (modifiers & KeyModifiers.Meta) != 0,
                Key = keyName,
            };

            try
            {
                if (ViewModel is not null)
                {
                    ViewModel.Settings.Capture ??= new CaptureSettings();
                    ViewModel.Settings.Capture.Hotkey = binding;
                    await ViewModel.PersistSettingsImmediatelyAsync();
                }

                if (Application.Current is App app)
                    app.ReRegisterHotkey(binding);
            }
            catch
            {
                // best effort
            }

            item.Header = $"Set Cap Hotkey: {binding.Display}";
            Cleanup();
            e.Handled = true;
        };

        lostFocusHandler = (_, _) =>
        {
            if (captured) return;
            captured = true;
            item.Header = originalHeader;
            Cleanup();
        };

        closedHandler = (_, _) =>
        {
            if (!captured)
            {
                captured = true;
                item.Header = originalHeader;
                Cleanup();
            }
        };

        AddHandler(KeyDownEvent, keyHandler, handledEventsToo: true);
        item.AddHandler(KeyDownEvent, keyHandler, handledEventsToo: true);
        LostFocus += lostFocusHandler;
        Closed += closedHandler;
    }

    private void SyncMenuChecks()
    {
        if (ViewModel is null) return;
        if (_zoomFixMenuItem is not null)
            _zoomFixMenuItem.IsChecked = ViewModel.ZoomFix;

        if (_fullscreenMenuItem is not null)
            _fullscreenMenuItem.IsChecked = _isFullscreen;

        SyncSortFieldLabels();
        SyncSortDirectionLabel();
    }

    #endregion

    #region Clipboard

    private async Task CopyRenderedImageToClipboardAsync()
    {
        await RunLoggedAsync(() =>
        {
            if (ViewModel is null) return Task.CompletedTask;

            var data = ViewModel.GetCurrentPixelData();
            if (data is null) return Task.CompletedTask;

            var (pixels, w, h, stride) = data.Value;
            ImageViewer.Platform.Windows.WindowsClipboardHelper.CopyBitmapToClipboard(pixels, w, h, stride);
            return Task.CompletedTask;
        }, "Copy rendered image to clipboard");
    }

    private static void CopyBitmapToNativeClipboard(SKBitmap bitmap)
    {
        var w = bitmap.Width;
        var h = bitmap.Height;
        var rowBytes = bitmap.RowBytes;
        var pixels = new byte[rowBytes * h];
        Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        ImageViewer.Platform.Windows.WindowsClipboardHelper.CopyBitmapToClipboard(pixels, w, h, rowBytes);
    }

    private async Task CopyCurrentFileDropListAsync(bool cut)
    {
        await RunLoggedAsync(() =>
        {
            var path = ViewModel?.CurrentEntry?.FullPath;
            if (path is null || ViewModel is null) return Task.CompletedTask;

            var shell = ((App)Application.Current!).Services.ShellService;
            shell.CopyFilesToClipboard(new[] { path }, cut);
            return Task.CompletedTask;
        }, cut ? "File cut" : "File copy");
    }

    #endregion

    private void ShowToast(string message)
    {
        _toastCts?.Cancel();
        _toastCts = new CancellationTokenSource();
        var token = _toastCts.Token;

        ToastText.Text = message;
        ToastOverlay.IsVisible = true;
        ToastOverlay.Opacity = 1;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1200, token);
                Dispatcher.UIThread.Post(() =>
                {
                    if (token.IsCancellationRequested) return;
                    ToastOverlay.Opacity = 0;
                });
                await Task.Delay(300, token);
                Dispatcher.UIThread.Post(() =>
                {
                    if (token.IsCancellationRequested) return;
                    ToastOverlay.IsVisible = false;
                });
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private async Task RunLoggedAsync(Func<Task> action, string context)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ((App)Application.Current!).Services.CrashLogger.Log(ex, context);
            ShowToast($"Error: {context}");
        }
    }
}
