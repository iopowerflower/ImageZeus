using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ImageViewer.App.Services;
using ImageViewer.Core.Caps;
using ImageViewer.Core.Models;
using ImageViewer.Core.Services;
using ImageViewer.Imaging.Cache;
using ImageViewer.Imaging.Decoding;
using ImageViewer.Imaging.Models;
using ImageViewer.Persistence;

namespace ImageViewer.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppServices _services;
    private readonly NavigationLoadCoordinator _loadCoordinator;
    private readonly DebouncedSettingsWriter _settingsWriter;
    private readonly ImageSorter _imageSorter = new();
    private readonly FolderImageIndexBuilder _indexBuilder = new();

    private ViewerSettings _settings = new();
    private IReadOnlyList<ImageEntry> _images = Array.Empty<ImageEntry>();
    private ImageCacheLease? _currentLease;
    private CancellationTokenSource _loadCts = new();
    private int _loadGeneration;
    private int _appliedGeneration;

    private WriteableBitmap? _currentImage;
    private int _currentIndex = -1;
    private bool _isSidePanelOpen;
    private bool _zoomFix;
    private bool _showExif;
    private double _zoom = 1;
    private double _offsetX;
    private double _offsetY;
    private bool _disposed;

    private int _rotateDegrees;
    private bool _flipH;
    private bool _flipV;
    private bool _isDirty;

    private DispatcherTimer? _animTimer;
    private int _animFrameIndex;
    private bool _isAnimPlaying;
    private bool _isAnimated;
    private uint _currentRating;

    private DispatcherTimer? _dirRefreshTimer;
    private string? _currentFolder;

    public MainWindowViewModel(AppServices services)
    {
        _services = services;
        _loadCoordinator = new NavigationLoadCoordinator(_services.DecodePipeline, _services.CrashLogger);
        _settingsWriter = new DebouncedSettingsWriter(_services.SettingsStore, _services.CrashLogger, TimeSpan.FromMilliseconds(500));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string WindowTitle =>
        CurrentEntry is null
            ? "ImageZeus"
            : $"ImageZeus - {CurrentEntry.Name} ({CurrentIndex + 1}/{Math.Max(1, _images.Count)})";

    public WriteableBitmap? CurrentImage
    {
        get => _currentImage;
        private set
        {
            if (!ReferenceEquals(_currentImage, value))
            {
                _currentImage?.Dispose();
                _currentImage = value;
                OnPropertyChanged();
            }
        }
    }

    public double Zoom
    {
        get => _zoom;
        private set
        {
            if (Math.Abs(_zoom - value) > 0.0001)
            {
                _zoom = value;
                OnPropertyChanged();
            }
        }
    }

    public double OffsetX
    {
        get => _offsetX;
        private set
        {
            if (Math.Abs(_offsetX - value) > 0.0001)
            {
                _offsetX = value;
                OnPropertyChanged();
            }
        }
    }

    public double OffsetY
    {
        get => _offsetY;
        private set
        {
            if (Math.Abs(_offsetY - value) > 0.0001)
            {
                _offsetY = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ZoomFix
    {
        get => _zoomFix;
        private set
        {
            if (_zoomFix != value)
            {
                _zoomFix = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsSidePanelOpen
    {
        get => _isSidePanelOpen;
        private set
        {
            if (_isSidePanelOpen != value)
            {
                _isSidePanelOpen = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ShowExif
    {
        get => _showExif;
        private set
        {
            if (_showExif != value)
            {
                _showExif = value;
                OnPropertyChanged();
            }
        }
    }

    public string ExifText
    {
        get
        {
            if (CurrentEntry is null) return string.Empty;
            var dims = _currentLease?.Image is { } img ? $"{img.Width}×{img.Height} | " : "";
            return $"{CurrentEntry.Name} | {dims}{CurrentEntry.DateModified:yyyy-MM-dd HH:mm} | {CurrentEntry.SizeBytes / 1024.0:F1} KB";
        }
    }

    public bool IsAnimated
    {
        get => _isAnimated;
        private set
        {
            if (_isAnimated != value)
            {
                _isAnimated = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsAnimPlaying
    {
        get => _isAnimPlaying;
        private set
        {
            if (_isAnimPlaying != value)
            {
                _isAnimPlaying = value;
                OnPropertyChanged();
            }
        }
    }

    public int AnimFrameCount => _currentLease?.Image?.Frames.Count ?? 0;

    public int AnimFrameIndex
    {
        get => _animFrameIndex;
        private set
        {
            if (_animFrameIndex != value)
            {
                _animFrameIndex = value;
                OnPropertyChanged();
            }
        }
    }

    public uint CurrentRating
    {
        get => _currentRating;
        private set
        {
            if (_currentRating != value)
            {
                _currentRating = value;
                OnPropertyChanged();
            }
        }
    }

    public int CurrentIndex => _currentIndex;

    public int ImageCount => _images.Count;

    public ImageEntry? CurrentEntry => _currentIndex >= 0 && _currentIndex < _images.Count ? _images[_currentIndex] : null;

    public SortField SortField => _settings.SortField;

    public SortDirection SortDirection => _settings.SortDirection;

    public async Task InitializeAsync()
    {
        try
        {
            var firstFile = _services.Args.FirstOrDefault(File.Exists);

            var settingsTask = _services.SettingsStore.LoadAsync(CancellationToken.None);

            _settings = await settingsTask;
            IsSidePanelOpen = _settings.IsSidePanelOpen;

            if (string.IsNullOrWhiteSpace(firstFile))
            {
                OnPropertyChanged(nameof(WindowTitle));
                return;
            }

            var fullFirst = Path.GetFullPath(firstFile);

            var ratingsTask = _services.RatingService.GetAllRatingsAsync(CancellationToken.None);
            var decodeTask = _services.DecodePipeline.LoadAsync(fullFirst, CancellationToken.None);

            var raw = await ratingsTask;
            var ratings = raw.ToDictionary(
                kv => kv.Key, kv => (uint?)kv.Value, StringComparer.OrdinalIgnoreCase);

            var fastImages = _indexBuilder.BuildFast(firstFile, ratings);
            _images = _imageSorter.Sort(fastImages, _settings.SortField, _settings.SortDirection);
            _currentIndex = _images.ToList().FindIndex(
                x => x.FullPath.Equals(fullFirst, StringComparison.OrdinalIgnoreCase));
            if (_currentIndex < 0 && _images.Count > 0)
                _currentIndex = 0;

            _currentFolder = Path.GetDirectoryName(fullFirst);
            RaiseNavigationProperties();

            try
            {
                var lease = await decodeTask;
                Interlocked.Increment(ref _loadGeneration);
                Volatile.Write(ref _appliedGeneration, _loadGeneration);
                ApplyLease(lease, 0);
            }
            catch (Exception ex)
            {
                _services.CrashLogger.Log(ex, "Decode first image");
            }

            _ = Task.Run(() => BackfillMetadataAsync(fullFirst, ratings));

            StartDirectoryRefreshTimer();
        }
        catch (Exception ex)
        {
            _services.CrashLogger.Log(ex, "Initialize main window view model");
        }
    }

    private async Task BackfillMetadataAsync(string selectedPath, Dictionary<string, uint?> ratings)
    {
        try
        {
            var fullImages = await Task.Run(() => _indexBuilder.Build(selectedPath, ratings));

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var currentPath = CurrentEntry?.FullPath;
                _images = _imageSorter.Sort(fullImages, _settings.SortField, _settings.SortDirection);

                if (currentPath is not null)
                {
                    _currentIndex = _images.ToList().FindIndex(
                        x => x.FullPath.Equals(currentPath, StringComparison.OrdinalIgnoreCase));
                }
                if (_currentIndex < 0 && _images.Count > 0)
                    _currentIndex = 0;

                RaiseNavigationProperties();
            });
        }
        catch (Exception ex)
        {
            _services.CrashLogger.Log(ex, "Backfill metadata");
        }
    }

    public async Task<string?> NavigateAsync(int delta)
    {
        if (_images.Count == 0)
            return null;

        string? wrapMessage = null;
        var raw = _currentIndex + delta;

        if (raw >= _images.Count)
        {
            raw = 0;
            wrapMessage = "First image";
        }
        else if (raw < 0)
        {
            raw = _images.Count - 1;
            wrapMessage = "Last image";
        }

        if (raw == _currentIndex)
            return null;

        _currentIndex = raw;
        RaiseNavigationProperties();
        await LoadCurrentAsync();
        return wrapMessage;
    }

    public async Task SetIndexAsync(int index)
    {
        if (index < 0 || index >= _images.Count || index == _currentIndex)
            return;

        _currentIndex = index;
        RaiseNavigationProperties();
        await LoadCurrentAsync();
    }

    public async Task EnsureCurrentLoadedAsync()
    {
        if (_currentIndex < 0 || _currentIndex >= _images.Count)
            return;

        var expectedPath = _images[_currentIndex].FullPath;
        if (_currentLease?.Image?.Key is not null &&
            _currentLease.Image.Key.Equals(expectedPath, StringComparison.OrdinalIgnoreCase))
            return;

        await LoadCurrentAsync();
    }

    public async Task SetSortAsync(SortField sortField)
    {
        _settings.SortField = sortField;
        PersistSettings();

        var selected = CurrentEntry?.FullPath;
        _images = _imageSorter.Sort(_images, _settings.SortField, _settings.SortDirection);
        _currentIndex = selected is null ? _currentIndex : _images.ToList().FindIndex(x => x.FullPath.Equals(selected, StringComparison.OrdinalIgnoreCase));
        RaiseNavigationProperties();
        await LoadCurrentAsync();
    }

    public async Task ToggleSortDirectionAsync()
    {
        _settings.SortDirection = _settings.SortDirection == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;
        PersistSettings();

        var selected = CurrentEntry?.FullPath;
        _images = _imageSorter.Sort(_images, _settings.SortField, _settings.SortDirection);
        _currentIndex = selected is null ? _currentIndex : _images.ToList().FindIndex(x => x.FullPath.Equals(selected, StringComparison.OrdinalIgnoreCase));
        RaiseNavigationProperties();
        await LoadCurrentAsync();
    }

    public void ToggleZoomFix()
    {
        ZoomFix = !ZoomFix;
    }

    public void ResetZoom()
    {
        Zoom = 1.0;
        OffsetX = 0;
        OffsetY = 0;
    }

    public void SetFitZoom(double viewportWidth, double viewportHeight)
    {
        if (ZoomFix || _currentLease?.Image is null)
        {
            return;
        }

        var scale = FitZoomCalculator.CalculateFitScale(
            new Size2D(_currentLease.Image.Width, _currentLease.Image.Height),
            new Size2D(viewportWidth, viewportHeight),
            zoomFix: false);

        Zoom = scale;
        OffsetX = 0;
        OffsetY = 0;
    }

    public void ZoomAt(double wheelDelta, double anchorX, double anchorY)
    {
        if (_currentLease is null)
        {
            return;
        }

        var factor = wheelDelta > 0 ? 1.15 : 1.0 / 1.15;
        var nextZoom = Math.Clamp(Zoom * factor, 0.05, 32.0);
        if (Math.Abs(nextZoom - Zoom) < 0.0001)
        {
            return;
        }

        var offset = ZoomAnchorMath.ComputeAnchoredOffset(
            new Point2D(OffsetX, OffsetY),
            Zoom,
            nextZoom,
            new Point2D(anchorX, anchorY));

        Zoom = nextZoom;
        OffsetX = offset.X;
        OffsetY = offset.Y;
    }

    public void SetZoomDirect(double zoom, double offsetX, double offsetY)
    {
        Zoom = zoom;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    public void PanBy(double dx, double dy)
    {
        OffsetX += dx;
        OffsetY += dy;
    }

    public void ToggleSidePanel()
    {
        IsSidePanelOpen = !IsSidePanelOpen;
        _settings.IsSidePanelOpen = IsSidePanelOpen;
        PersistSettings();
    }

    public void ToggleExifOverlay()
    {
        ShowExif = !ShowExif;
    }

    public ViewerSettings Settings => _settings;

    public CapsSettings CapsSettings => _settings.Caps;

    public void PersistCapsSettings()
    {
        PersistSettings();
    }

    public void ToggleAnimPlayPause()
    {
        if (!IsAnimated) return;

        if (IsAnimPlaying)
            StopAnimation();
        else
            StartAnimation();
    }

    public void SeekAnimFrame(int frameIndex)
    {
        if (!IsAnimated || _currentLease?.Image is null) return;
        var frames = _currentLease.Image.Frames;
        frameIndex = Math.Clamp(frameIndex, 0, frames.Count - 1);

        StopAnimation();
        AnimFrameIndex = frameIndex;
        ShowFrame(frameIndex);
    }

    private void StartAnimation()
    {
        if (_currentLease?.Image is null || !IsAnimated) return;

        var frames = _currentLease.Image.Frames;
        var intervalMs = frames[_animFrameIndex].Duration.TotalMilliseconds;
        if (intervalMs < 10) intervalMs = 100;

        _animTimer?.Stop();
        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };
        _animTimer.Tick += OnAnimTimerTick;
        _animTimer.Start();
        IsAnimPlaying = true;
    }

    private void StopAnimation()
    {
        _animTimer?.Stop();
        _animTimer = null;
        IsAnimPlaying = false;
    }

    private void OnAnimTimerTick(object? sender, EventArgs e)
    {
        if (_currentLease?.Image is null || !IsAnimated)
        {
            StopAnimation();
            return;
        }

        var frames = _currentLease.Image.Frames;
        var nextIndex = (_animFrameIndex + 1) % frames.Count;
        AnimFrameIndex = nextIndex;
        ShowFrame(nextIndex);

        var nextDuration = frames[nextIndex].Duration.TotalMilliseconds;
        if (nextDuration < 10) nextDuration = 100;
        if (_animTimer is not null)
            _animTimer.Interval = TimeSpan.FromMilliseconds(nextDuration);
    }

    private void ShowFrame(int frameIndex)
    {
        if (_currentLease?.Image is null) return;
        var frames = _currentLease.Image.Frames;
        if (frameIndex < 0 || frameIndex >= frames.Count) return;

        var frame = frames[frameIndex];
        if (_isDirty)
        {
            frame = ApplyTransform(frame);
        }

        if (_currentImage is not null &&
            _currentImage.PixelSize.Width == frame.Width &&
            _currentImage.PixelSize.Height == frame.Height)
        {
            PixelFrameAvaloniaBlitter.Blit(frame, _currentImage);
            OnPropertyChanged(nameof(CurrentImage));
        }
        else
        {
            CurrentImage = PixelFrameAvaloniaBlitter.CreateWriteableBitmap(frame);
        }
    }

    public bool IsDirty => _isDirty;

    public void RotateCw()
    {
        _rotateDegrees = (_rotateDegrees + 90) % 360;
        _isDirty = true;
        RebuildTransformedImage();
    }

    public void RotateCcw()
    {
        _rotateDegrees = (_rotateDegrees + 270) % 360;
        _isDirty = true;
        RebuildTransformedImage();
    }

    public void FlipHorizontal()
    {
        _flipH = !_flipH;
        _isDirty = true;
        RebuildTransformedImage();
    }

    public void FlipVertical()
    {
        _flipV = !_flipV;
        _isDirty = true;
        RebuildTransformedImage();
    }

    private void ClearTransformState()
    {
        _rotateDegrees = 0;
        _flipH = false;
        _flipV = false;
        _isDirty = false;
    }

    private void RebuildTransformedImage()
    {
        if (_currentLease?.Image is null || _currentLease.Image.Frames.Count == 0)
            return;

        var srcFrame = _currentLease.Image.Frames[0];
        var transformed = ApplyTransform(srcFrame);
        var bitmap = PixelFrameAvaloniaBlitter.CreateWriteableBitmap(transformed);
        CurrentImage = bitmap;
    }

    private DecodedFrame ApplyTransform(DecodedFrame src)
    {
        var w = src.Width;
        var h = src.Height;
        var pixels = src.Pixels;
        var stride = src.Stride;

        if (_flipH)
        {
            pixels = FlipHorizontalPixels(pixels, w, h, stride);
            stride = w * 4;
        }

        if (_flipV)
        {
            pixels = FlipVerticalPixels(pixels, w, h, stride);
            stride = w * 4;
        }

        for (var r = 0; r < _rotateDegrees; r += 90)
        {
            var result = Rotate90Pixels(pixels, w, h, stride);
            pixels = result.pixels;
            var oldW = w;
            w = h;
            h = oldW;
            stride = w * 4;
        }

        return new DecodedFrame(w, h, stride, pixels, src.Duration);
    }

    private static byte[] FlipHorizontalPixels(byte[] src, int w, int h, int stride)
    {
        var dst = new byte[w * 4 * h];
        var dstStride = w * 4;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var srcOff = y * stride + x * 4;
                var dstOff = y * dstStride + (w - 1 - x) * 4;
                dst[dstOff] = src[srcOff];
                dst[dstOff + 1] = src[srcOff + 1];
                dst[dstOff + 2] = src[srcOff + 2];
                dst[dstOff + 3] = src[srcOff + 3];
            }
        }
        return dst;
    }

    private static byte[] FlipVerticalPixels(byte[] src, int w, int h, int stride)
    {
        var dst = new byte[w * 4 * h];
        var dstStride = w * 4;
        for (var y = 0; y < h; y++)
        {
            Buffer.BlockCopy(src, y * stride, dst, (h - 1 - y) * dstStride, w * 4);
        }
        return dst;
    }

    private static (byte[] pixels, int newW, int newH) Rotate90Pixels(byte[] src, int w, int h, int stride)
    {
        var newW = h;
        var newH = w;
        var dst = new byte[newW * 4 * newH];
        var dstStride = newW * 4;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var srcOff = y * stride + x * 4;
                var dstX = h - 1 - y;
                var dstY = x;
                var dstOff = dstY * dstStride + dstX * 4;
                dst[dstOff] = src[srcOff];
                dst[dstOff + 1] = src[srcOff + 1];
                dst[dstOff + 2] = src[srcOff + 2];
                dst[dstOff + 3] = src[srcOff + 3];
            }
        }

        return (dst, newW, newH);
    }

    public (byte[] pixels, int width, int height, int stride)? GetCurrentPixelData()
    {
        if (_currentLease?.Image is null || _currentLease.Image.Frames.Count == 0)
            return null;

        var frame = _currentLease.Image.Frames[0];
        if (_isDirty)
        {
            var transformed = ApplyTransform(frame);
            return (transformed.Pixels, transformed.Width, transformed.Height, transformed.Stride);
        }

        return (frame.Pixels, frame.Width, frame.Height, frame.Stride);
    }

    public async Task ShowInExplorerAsync()
    {
        var path = CurrentEntry?.FullPath;
        if (path is null)
        {
            return;
        }

        await RunSafeAsync(() =>
        {
            _services.ShellService.ShowInExplorer(path);
            return Task.CompletedTask;
        }, "Show in Explorer");
    }

    public async Task OpenPropertiesAsync()
    {
        var path = CurrentEntry?.FullPath;
        if (path is null)
        {
            return;
        }

        await RunSafeAsync(() =>
        {
            _services.ShellService.OpenProperties(path);
            return Task.CompletedTask;
        }, "Open properties");
    }

    public async Task PrintAsync()
    {
        var path = CurrentEntry?.FullPath;
        if (path is null)
        {
            return;
        }

        await RunSafeAsync(() =>
        {
            _services.ShellService.Print(path);
            return Task.CompletedTask;
        }, "Print image");
    }

    public async Task DeleteAsync()
    {
        var path = CurrentEntry?.FullPath;
        if (path is null) return;

        var deletedIndex = _currentIndex;

        await RunSafeAsync(async () =>
        {
            await _services.ShellService.DeleteToRecycleBinAsync(path, CancellationToken.None);

            var list = _images.ToList();
            if (deletedIndex >= 0 && deletedIndex < list.Count)
                list.RemoveAt(deletedIndex);
            _images = list;

            if (_images.Count == 0)
            {
                _currentIndex = -1;
                _currentLease?.Dispose();
                _currentLease = null;
                CurrentImage = null;
                RaiseNavigationProperties();
                return;
            }

            _currentIndex = deletedIndex < _images.Count ? deletedIndex : _images.Count - 1;
            RaiseNavigationProperties();
            await LoadCurrentAsync();
        }, "Delete image");
    }

    public async Task SetRatingAsync(uint stars)
    {
        var entry = CurrentEntry;
        if (entry is null) return;

        await RunSafeAsync(async () =>
        {
            await _services.RatingService.SetRatingAsync(entry.FullPath, stars, CancellationToken.None);
            CurrentRating = stars;

            if (_currentIndex >= 0 && _currentIndex < _images.Count)
            {
                var list = _images.ToList();
                list[_currentIndex] = entry with { Rating = stars == 0 ? null : stars };
                _images = list;
            }
        }, "Set rating");
    }

    public async Task SaveAsync()
    {
        var path = CurrentEntry?.FullPath;
        if (path is null) return;

        await RunSafeAsync(async () =>
        {
            await SaveToPathAsync(path);
            ClearTransformState();
            await ReloadCurrentAsync();
        }, "Save image");
    }

    public async Task SaveAsAsync(string destPath)
    {
        await RunSafeAsync(async () =>
        {
            await SaveToPathAsync(destPath);
            ClearTransformState();
        }, "Save As image");
    }

    private async Task SaveToPathAsync(string destPath)
    {
        var data = GetCurrentPixelData();
        if (data is null) return;

        var (pixels, w, h, stride) = data.Value;
        var ext = Path.GetExtension(destPath).ToLowerInvariant();

        var skFormat = ext switch
        {
            ".jpg" or ".jpeg" => SkiaSharp.SKEncodedImageFormat.Jpeg,
            ".webp" => SkiaSharp.SKEncodedImageFormat.Webp,
            ".bmp" => SkiaSharp.SKEncodedImageFormat.Bmp,
            _ => SkiaSharp.SKEncodedImageFormat.Png,
        };

        var quality = ext is ".jpg" or ".jpeg" ? _settings.JpegFallbackQuality : 100;

        await Task.Run(() =>
        {
            using var skBmp = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(w, h, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul));
            var dstPtr = skBmp.GetPixels();
            var dstStride = skBmp.RowBytes;

            unsafe
            {
                fixed (byte* srcBase = pixels)
                {
                    var dst = (byte*)dstPtr;
                    for (var y = 0; y < h; y++)
                    {
                        Buffer.MemoryCopy(
                            srcBase + y * stride,
                            dst + y * dstStride,
                            dstStride,
                            w * 4);
                    }
                }
            }

            using var encoded = skBmp.Encode(skFormat, quality);
            if (encoded is null) throw new InvalidOperationException("Failed to encode image.");

            var dir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            var tempPath = destPath + ".tmp";
            using (var fs = File.Create(tempPath))
            {
                encoded.SaveTo(fs);
            }
            File.Move(tempPath, destPath, overwrite: true);
        });
    }

    private async Task ReloadCurrentAsync()
    {
        if (_currentIndex < 0 || _currentIndex >= _images.Count) return;

        var targetPath = _images[_currentIndex].FullPath;
        _loadCoordinator.BeginNavigation();
        var newCts = new CancellationTokenSource();
        var old = Interlocked.Exchange(ref _loadCts, newCts);
        old.Cancel();
        old.Dispose();

        try
        {
            _services.DecodePipeline.Invalidate(targetPath);
            var lease = await _loadCoordinator.LoadCurrentAsync(targetPath, newCts.Token);
            if (lease is null) return;

            SetCurrentLease(lease);
            ClearTransformState();

            var frame = lease.Image.Frames[0];
            var bitmap = PixelFrameAvaloniaBlitter.CreateWriteableBitmap(frame);
            CurrentImage = bitmap;

            OnPropertyChanged(nameof(ExifText));
            OnPropertyChanged(nameof(WindowTitle));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _services.CrashLogger.Log(ex, $"Reload image '{targetPath}'");
        }
    }

    public string BuildCapsFileName(string sourceFileName, string extension, int sequence)
    {
        var baseName = Path.GetFileNameWithoutExtension(sourceFileName);
        var stamp = DateTime.Now.ToString("HHmmssfff");
        return $"{baseName}_cap_{stamp}_{sequence}.{extension.TrimStart('.')}";
    }

    public PixelSize ApplyCapsConstraints(PixelSize sourceSize)
    {
        var caps = _settings.Caps;
        return CapsConstraintEvaluator.ApplyCaptureModes(sourceSize, new CapsConstraintOptions
        {
            AspectRatioEnabled = caps.AspectRatioEnabled,
            AspectRatioX = caps.AspectRatioX,
            AspectRatioY = caps.AspectRatioY,
            FixedSizeEnabled = caps.FixedSizeEnabled,
            FixedWidth = caps.FixedWidth,
            FixedHeight = caps.FixedHeight,
            ResizeLargestDimensionEnabled = caps.ResizeLargestDimensionEnabled,
            ResizeLargestDimension = caps.ResizeLargestDimension,
        });
    }

    private void StartDirectoryRefreshTimer()
    {
        _dirRefreshTimer?.Stop();
        _dirRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _dirRefreshTimer.Tick += async (_, _) => await RefreshDirectoryAsync();
        _dirRefreshTimer.Start();
    }

    private async Task RefreshDirectoryAsync()
    {
        if (_disposed || _currentFolder is null || !Directory.Exists(_currentFolder)) return;

        try
        {
            var currentFiles = new HashSet<string>(
                Directory.EnumerateFiles(_currentFolder)
                    .Where(SupportedFormats.IsSupported)
                    .Select(Path.GetFullPath),
                StringComparer.OrdinalIgnoreCase);

            var knownFiles = new HashSet<string>(
                _images.Select(e => e.FullPath),
                StringComparer.OrdinalIgnoreCase);

            if (currentFiles.SetEquals(knownFiles)) return;

            var selectedPath = CurrentEntry?.FullPath;

            var raw = await _services.RatingService.GetAllRatingsAsync(CancellationToken.None);
            var ratings = raw.ToDictionary(
                kv => kv.Key, kv => (uint?)kv.Value, StringComparer.OrdinalIgnoreCase);

            var firstFile = selectedPath ?? currentFiles.FirstOrDefault();
            if (firstFile is null) return;

            _images = _indexBuilder.Build(firstFile, ratings);
            _images = _imageSorter.Sort(_images, _settings.SortField, _settings.SortDirection);

            if (selectedPath is not null)
            {
                _currentIndex = _images.ToList().FindIndex(
                    x => x.FullPath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase));
            }
            if (_currentIndex < 0 && _images.Count > 0)
                _currentIndex = 0;

            RaiseNavigationProperties();
        }
        catch (Exception ex)
        {
            _services.CrashLogger.Log(ex, "Directory refresh");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _dirRefreshTimer?.Stop();
        _dirRefreshTimer = null;

        StopAnimation();

        _loadCts.Cancel();
        _loadCts.Dispose();

        _currentLease?.Dispose();
        _currentLease = null;

        CurrentImage = null;

        _loadCoordinator.Dispose();
        _settingsWriter.Dispose();
    }

    public async Task LoadFolderAndSelectAsync(string selectedPath)
    {
        var raw = await _services.RatingService.GetAllRatingsAsync(CancellationToken.None);
        var ratings = raw.ToDictionary(
            kv => kv.Key,
            kv => (uint?)kv.Value,
            StringComparer.OrdinalIgnoreCase);
        _images = _indexBuilder.Build(selectedPath, ratings);
        _images = _imageSorter.Sort(_images, _settings.SortField, _settings.SortDirection);

        _currentIndex = _images.ToList().FindIndex(x => x.FullPath.Equals(Path.GetFullPath(selectedPath), StringComparison.OrdinalIgnoreCase));
        if (_currentIndex < 0 && _images.Count > 0)
            _currentIndex = 0;

        _currentFolder = Path.GetDirectoryName(Path.GetFullPath(selectedPath));
        StartDirectoryRefreshTimer();

        RaiseNavigationProperties();
        await LoadCurrentAsync();
    }

    private async Task LoadCurrentAsync()
    {
        StopAnimation();

        if (_currentIndex < 0 || _currentIndex >= _images.Count)
        {
            SetCurrentLease(null);
            return;
        }

        var myGen = Interlocked.Increment(ref _loadGeneration);
        var targetPath = _images[_currentIndex].FullPath;

        var cachedLease = _services.DecodePipeline.TryAcquireCached(targetPath);
        if (cachedLease is not null)
        {
            if (myGen <= Volatile.Read(ref _appliedGeneration))
            {
                cachedLease.Dispose();
                return;
            }
            Volatile.Write(ref _appliedGeneration, myGen);
            var navGen = _loadCoordinator.BeginNavigation();
            ApplyLease(cachedLease, navGen);
            return;
        }

        var navGeneration = _loadCoordinator.BeginNavigation();

        try
        {
            var lease = await _services.DecodePipeline.LoadAsync(targetPath, CancellationToken.None);

            if (myGen <= Volatile.Read(ref _appliedGeneration))
            {
                lease.Dispose();
                return;
            }

            Volatile.Write(ref _appliedGeneration, myGen);
            ApplyLease(lease, navGeneration);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _services.CrashLogger.Log(ex, $"Load image '{targetPath}'");
        }
    }

    private void ApplyLease(ImageCacheLease lease, long navGeneration)
    {
        SetCurrentLease(lease);
        ClearTransformState();

        var frame = lease.Image.Frames[0];
        var bitmap = PixelFrameAvaloniaBlitter.CreateWriteableBitmap(frame);
        CurrentImage = bitmap;

        IsAnimated = lease.Image.IsAnimated;
        AnimFrameIndex = 0;
        OnPropertyChanged(nameof(AnimFrameCount));

        if (IsAnimated)
            StartAnimation();

        OnPropertyChanged(nameof(ExifText));
        OnPropertyChanged(nameof(WindowTitle));

        LoadCurrentRating();
        SchedulePreload(navGeneration);
    }

    private async void LoadCurrentRating()
    {
        var path = CurrentEntry?.FullPath;
        if (path is null) { CurrentRating = 0; return; }

        try
        {
            var raw = await _services.RatingService.GetRatingAsync(path, CancellationToken.None);
            CurrentRating = raw ?? 0;
        }
        catch
        {
            CurrentRating = 0;
        }
    }

    private void SchedulePreload(long navGeneration)
    {
        if (_currentIndex < 0 || _images.Count == 0)
            return;

        var preload = new List<string>(4);
        for (var i = -2; i <= 2; i++)
        {
            if (i == 0) continue;
            var index = _currentIndex + i;
            if (index >= 0 && index < _images.Count)
                preload.Add(_images[index].FullPath);
        }

        _loadCoordinator.PreloadNeighbors(preload, navGeneration);
    }

    private void SetCurrentLease(ImageCacheLease? lease)
    {
        _currentLease?.Dispose();
        _currentLease = lease;
    }

    public void PersistSettings()
    {
        _settingsWriter.ScheduleSave(_settings);
    }

    private void RaiseNavigationProperties()
    {
        OnPropertyChanged(nameof(CurrentEntry));
        OnPropertyChanged(nameof(CurrentIndex));
        OnPropertyChanged(nameof(ImageCount));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(ExifText));
        OnPropertyChanged(nameof(SortField));
        OnPropertyChanged(nameof(SortDirection));
    }

    private async Task RunSafeAsync(Func<Task> action, string context)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _services.CrashLogger.Log(ex, context);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
