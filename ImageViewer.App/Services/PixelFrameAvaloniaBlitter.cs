using Avalonia;
using Avalonia.Media.Imaging;
using ImageViewer.Imaging.Models;

namespace ImageViewer.App.Services;

public static class PixelFrameAvaloniaBlitter
{
    public static WriteableBitmap CreateWriteableBitmap(DecodedFrame frame)
    {
        var writeable = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul);

        Blit(frame, writeable);
        return writeable;
    }

    public static void Blit(DecodedFrame frame, WriteableBitmap target)
    {
        using var targetLock = target.Lock();
        var copyBytesPerRow = frame.Width * 4;

        unsafe
        {
            fixed (byte* sourceBase = frame.Pixels)
            {
                var targetBase = (byte*)targetLock.Address;
                for (var y = 0; y < frame.Height; y++)
                {
                    Buffer.MemoryCopy(
                        sourceBase + (y * frame.Stride),
                        targetBase + (y * targetLock.RowBytes),
                        targetLock.RowBytes,
                        copyBytesPerRow);
                }
            }
        }
    }
}
